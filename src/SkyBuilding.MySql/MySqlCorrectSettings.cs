using SkyBuilding.ORM;
using SkyBuilding.ORM.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyBuilding.MySql
{
    public class MySqlCorrectSettings : ISQLCorrectSettings
    {
        private readonly static ConcurrentDictionary<string, Tuple<string, bool>> mapperCache = new ConcurrentDictionary<string, Tuple<string, bool>>();

        private readonly static Regex PatternColumn = new Regex("select\\s+(?<column>((?!select|where).)+(select((?!from|select).)+from((?!from|select).)+)*((?!from|select).)*)\\s+from\\s+", RegexOptions.IgnoreCase);

        private readonly static Regex PatternSingleAsColumn = new Regex(@"([\x20\t\r\n\f]+as[\x20\t\r\n\f]+)?(\[\w+\]\.)*(?<name>(\[\w+\]))[\x20\t\r\n\f]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.RightToLeft);

        public string Substring => "SUBSTRING";

        public string IndexOf => "LOCATE";

        public string Length => "LENGTH";

        public bool IndexOfSwapPlaces => true;

        private List<IVisitter> visitters;

        /// <summary>
        /// 访问器
        /// </summary>
        public IList<IVisitter> Visitters => visitters ?? (visitters = new List<IVisitter>());

        public string Name(string name) => string.Concat("`", name, "`");
        public string AsName(string name) => Name(name);
        public string TableName(string name) => Name(name);
        public string ParamterName(string name) => string.Concat("?", name);
        public virtual string PageSql(string sql, int take, int skip)
        {
            var sb = new StringBuilder();

            sb.Append(sql)
                .Append(" LIMIT ");

            if (skip > 0)
            {
                sb.Append(skip)
                    .Append(",");
            }
            if (take > 0)
            {
                sb.Append(take);
            }
            else
            {
                sb.Append(-1);
            }

            return sb.ToString();
        }

        private Tuple<string, bool> GetColumns(string columns) => mapperCache.GetOrAdd(columns, _ =>
        {
            var list = SingleColumnCodeBlock(columns);

            if (list.Count == 1)
            {
                var match = PatternSingleAsColumn.Match(list.First());

                if (match.Success)
                {
                    return Tuple.Create(match.Groups["name"].Value, false);
                }

                return Tuple.Create("[__my_sql_col]", true);
            }

            return Tuple.Create(string.Join(",", list.ConvertAll(item =>
            {
                var match = PatternSingleAsColumn.Match(item);

                if (match.Success)
                {
                    return match.Groups["name"].Value;
                }

                throw new DException("分页且多字段时,必须指定字段名!");
            })), false);
        });

        /// <summary>
        /// 参数分析
        /// </summary>
        /// <param name="value">执行语句</param>
        /// <param name="startIndex">参数起始位置</param>
        /// <param name="leftCount">左括号出现次数</param>
        /// <param name="rightCount">右括号出现次数</param>
        /// <returns></returns>
        private static int ParameterAnalysis(string value, int startIndex, int leftCount, int rightCount)
        {
            int index = Array.FindIndex(value.ToCharArray(startIndex, value.Length - startIndex), item =>
            {
                if (item == ')')
                {
                    rightCount += 1;

                    return rightCount == leftCount;
                }

                if (item == '(')
                {
                    leftCount += 1;
                }

                return false;
            });

            return index > -1 ? index + startIndex + 1 : index;
        }

        /// <summary>
        /// 参数分析
        /// </summary>
        /// <param name="value">查询语句</param>
        /// <param name="startIndex">参数起始位置</param>
        /// <returns></returns>
        private static int ParameterAnalysis(string value, int startIndex)
        {
            int index = value.IndexOf(',', startIndex);

            //? 不包含分割符
            if (index == -1) return index;

            int leftIndex = value.IndexOf('(', startIndex, index - startIndex);

            //? 不包含左括号
            if (leftIndex == -1) return index;

            return ParameterAnalysis(value, leftIndex + 1, 1, 0);
        }

        /// <summary>
        /// 每列代码块（如:[x].[id],substring([x].[value],[x].[index],[x].[len]) as [total] => new List<string>{ "[x].[id]","substring([x].[value],[x].[index],[x].[len]) as [total]" }）
        /// </summary>
        /// <param name="columns">以“,”分割的列集合</param>
        /// <returns></returns>
        protected virtual List<string> SingleColumnCodeBlock(string columns)
        {
            int startIndex = 0, nextIndex = 0, length = columns.Length;
            List<string> list = new List<string>();
            while (nextIndex > -1 && startIndex < length)
            {
                nextIndex = ParameterAnalysis(columns, startIndex);

                if (nextIndex == -1)
                {
                    list.Add(columns.Substring(startIndex));
                }
                else
                {
                    list.Add(columns.Substring(startIndex, nextIndex - startIndex));
                }

                startIndex = nextIndex + 1;
            }

            return list;
        }
        public virtual string PageUnionSql(string sql, int take, int skip, string orderBy)
        {
            var sb = new StringBuilder();

            var match = PatternColumn.Match(sql);

            string value = match.Groups["column"].Value;

            Tuple<string, bool> tuple = GetColumns(value);

            if (tuple.Item2)
            {
                throw new DException("组合查询必须指定字段名!");
            }

            sb.Append("SELECT ")
                .Append(tuple.Item1)
                .Append(" FROM (")
                .Append(sql)
                .Append(") `CTE` ")
                .Append(orderBy)
                .Append(" LIMIT ");

            if (skip > 0)
            {
                sb.Append(skip)
                    .Append(",");
            }
            if (take > 0)
            {
                sb.Append(take);
            }
            else
            {
                sb.Append(-1);
            }

            return sb.ToString();
        }
    }
}
