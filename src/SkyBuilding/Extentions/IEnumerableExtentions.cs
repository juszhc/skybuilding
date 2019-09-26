using System.Linq;
using System.Text;

namespace System.Collections
{
    /// <summary>
    /// 迭代扩展
    /// </summary>
    public static class IEnumerableExtentions
    {
        /// <summary>
        /// 字符串拼接
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="separator">分隔符</param>
        /// <returns></returns>
        public static string Join(this IEnumerable source, string separator = ",")
        {
            var sb = new StringBuilder();

            var enumerator = source.GetEnumerator();

            if (enumerator.MoveNext())
            {
                sb.Append(enumerator.Current);

                while (enumerator.MoveNext())
                {
                    sb.Append(separator);
                    sb.Append(enumerator.Current);
                }
            }

            return sb.ToString();
        }
    }
}

namespace System.Collections.Generic
{
    /// <summary>
    /// 迭代扩展
    /// </summary>
    public static class IEnumerableExtentions
    {
        /// <summary>
        /// 对数据中的每个元素执行指定操作
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">数据源</param>
        /// <param name="action">要对数据源的每个元素执行的委托。</param>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (T item in source)
            {
                action.Invoke(item);
            }
        }

        /// <summary>
        /// 对数据中的每个元素执行指定操作
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">数据源</param>
        /// <param name="action">要对数据源的每个元素执行的委托。</param>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T, int> action)
        {
            int index = -1;
            foreach (T item in source)
            {
                action.Invoke(item, index += 1);
            }
        }
    }
}
