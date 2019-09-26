using SkyBuilding.ORM;
using SkyBuilding.ORM.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyBuilding.SqlServer
{
    /// <summary>
    /// SqlServer矫正设置
    /// </summary>
    public class SqlServerCorrectSettings : ISQLCorrectSettings
    {
        private readonly static ConcurrentDictionary<string, Tuple<string, bool>> mapperCache = new ConcurrentDictionary<string, Tuple<string, bool>>();

        private readonly static Regex PatternColumn = new Regex(@"\bselect[\x20\t\r\n\f]+(?<column>((?!\b(select|where))[\s\S])+(select((?!\b(from|select)\b)[\s\S])+from((?!\b(from|select)\b)[\s\S])+)*((?!\b(from|select)\b)[\s\S])*)[\x20\t\r\n\f]+from[\x20\t\r\n\f]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly static Regex PatternOrderBy = new Regex(@"\border[\x20\t\r\n\f]+by[\x20\t\r\n\f]+[\s\S]+?$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.RightToLeft);

        private readonly static Regex PatternSingleAsColumn = new Regex(@"([\x20\t\r\n\f]+as[\x20\t\r\n\f]+)?(\[\w+\]\.)*(?<name>(\[\w+\]))[\x20\t\r\n\f]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.RightToLeft);

        public string Substring => "SUBSTRING";

        public string IndexOf => "CHARINDEX";

        public string Length => "LEN";

        public bool IndexOfSwapPlaces => true;

        private List<IVisitter> visitters;

        /// <summary>
        /// 格式化
        /// </summary>
        public IList<IVisitter> Visitters => visitters ?? (visitters = new List<IVisitter>());

        public string Name(string name) => string.Concat("[", name, "]");
        public string AsName(string name) => Name(name);
        public string TableName(string name) => Name(name);
        public string ParamterName(string name) => string.Concat("@", name);
        public virtual string PageSql(string sql, int take, int skip)
        {
            var sb = new StringBuilder();
            Match match;
            if (skip < 1)
            {
                match = PatternColumn.Match(sql);

                sql = sql.Substring(match.Length);

                return sb.Append(" SELECT TOP ")
                     .Append(take)
                     .Append(" ")
                     .Append(match.Groups["column"].Value)
                     .Append(" FROM ")
                     .Append(sql)
                     .ToString();
            }

            match = PatternOrderBy.Match(sql);

            if (!match.Success)
                throw new DException("使用Skip函数需要设置排序字段!");

            string orderBy = match.Value;

            sql = sql.Substring(0, sql.Length - match.Length);

            match = PatternColumn.Match(sql);

            sql = sql.Substring(match.Length);

            string value = match.Groups["column"].Value;

            Tuple<string, bool> tuple = GetColumns(value);

            if (tuple.Item2)
            {
                value += " AS " + tuple.Item1;
            }

            sb.Append("SELECT ")
                .Append(tuple.Item1)
                .Append(" FROM (")
                .Append("SELECT ")
                .Append(value)
                .Append(", ROW_NUMBER() OVER(")
                .Append(orderBy)
                .Append(") AS [__Row_number_]")
                .Append(" FROM ")
                .Append(sql)
                .Append(") [CTE]")
                .Append(" WHERE ");

            if (skip > 0)
            {
                sb.Append("[__Row_number_] > ")
                    .Append(skip);
            }

            if (skip > 0 && take > 0)
            {
                sb.Append(" AND ");
            }

            if (take > 0)
            {
                sb.Append("[__Row_number_]<=")
                    .Append(skip + take);
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

                return Tuple.Create("[__sql_server_col]", true);
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

        /// <summary>
        /// 组合查询语句
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="take"></param>
        /// <param name="skip"></param>
        /// <param name="orderBy"></param>
        /// <returns></returns>
        public virtual string PageUnionSql(string sql, int take, int skip, string orderBy)
        {
            var sb = new StringBuilder();
            if (skip < 1)
            {
                return sb.Append("SELECT TOP ")
                     .Append(take)
                     .Append(" * FROM (")
                     .Append(sql)
                     .Append(") [CTE]")
                     .Append(orderBy)
                     .ToString();
            }
            if (string.IsNullOrEmpty(orderBy))
                throw new DException("使用Skip函数需要设置排序字段!");

            Match match = PatternColumn.Match(sql);

            string value = match.Groups["column"].Value;

            Tuple<string, bool> tuple = GetColumns(value);

            if (tuple.Item2)
            {
                throw new DException("组合查询必须指定字段名!");
            }

            sb.Append("SELECT ")
                .Append(tuple.Item1)
                .Append(" FROM (")
                .Append("SELECT ")
                .Append("ROW_NUMBER() OVER(")
                .Append(orderBy)
                .Append(") AS [__Row_number_],")
                .Append(tuple.Item1)
                .Append(" FROM (")
                .Append(sql)
                .Append(") [CTE_ROW_NUMBER]")
                .Append(") [CTE]")
                .Append(" WHERE ");

            if (skip > 0)
            {
                sb.Append("[__Row_number_] > ")
                    .Append(skip);
            }

            if (skip > 0 && take > 0)
            {
                sb.Append(" AND ");
            }

            if (take > 0)
            {
                sb.Append("[__Row_number_]<=")
                    .Append(skip + take);
            }
            return sb.ToString();
        }
    }
}
