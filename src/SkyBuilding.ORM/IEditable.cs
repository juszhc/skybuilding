﻿using System.Collections.Generic;
using System.Linq.Expressions;

namespace SkyBuilding.ORM
{
    /// <summary>
    /// 可编辑能力
    /// </summary>
    public interface IEditable
    {
        /// <summary>
        /// SQL矫正
        /// </summary>
        ISQLCorrectSimSettings Settings { get; }

        /// <summary>
        /// 执行
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="parameters">参数</param>
        /// <returns></returns>
        int Excute(string sql, Dictionary<string, object> parameters);
    }

    /// <summary>
    /// 可编辑能力
    /// </summary>
    /// <typeparam name="T">项</typeparam>
    public interface IEditable<T> : IEditable
    {
        /// <summary>
        /// 执行
        /// </summary>
        /// <param name="expression">表达式</param>
        /// <returns></returns>
        int Excute(Expression expression);
    }
}
