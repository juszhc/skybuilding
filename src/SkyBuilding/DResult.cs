﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SkyBuilding
{
    /// <summary>
    /// 数据结果
    /// </summary>
    public class DResult
    {
        /// <summary>
        /// 错误信息实体
        /// </summary>
        private class ErrorDresult : DResult
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="errorMsg">错误消息</param>
            /// <param name="statusCode">错误编码</param>
            public ErrorDresult(string errorMsg, int statusCode) : base(statusCode) => ErrorMsg = errorMsg;

            /// <summary>
            /// 错误信息
            /// </summary>
            public string ErrorMsg { get; }
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="statusCode">状态码</param>
        public DResult(int statusCode = 200) => StatusCode = statusCode;

        /// <summary>
        /// 状态码
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success => StatusCode == 200;

        /// <summary>
        /// 成功
        /// </summary>
        public static DResult Ok() => new DResult();

        /// <summary>
        /// 成功
        /// </summary>
        /// <param name="total">总数</param>
        /// <param name="data">数据</param>
        /// <returns></returns>
        public static DResult<T> Ok<T>(T data) => new DResult<T>(data);

        /// <summary>
        /// 成功
        /// </summary>
        /// <param name="total">总数</param>
        /// <param name="data">数据</param>
        /// <returns></returns>
        public static DResults<T> Ok<T>(PagedList<T> data) => new DResults<T>(data);

        /// <summary>
        /// 成功
        /// </summary>
        /// <param name="total">总数</param>
        /// <param name="data">数据</param>
        /// <returns></returns>
        public static DResults<T> Ok<T>(int total, List<T> data) => new DResults<T>(total, data);

        /// <summary>
        /// 错误信息
        /// </summary>
        /// <param name="errorMsg">错误信息</param>
        /// <param name="statusCode">状态码</param>
        /// <returns></returns>
        public static DResult Error(string errorMsg, int statusCode = 500) => new ErrorDresult(errorMsg, statusCode);
    }

    /// <summary>
    /// 数据结果
    /// </summary>
    /// <typeparam name="T">数据</typeparam>
    public class DResult<T> : DResult
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="data">数据</param>
        public DResult(T data) : base() => Data = data;

        /// <summary>
        /// 数据
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// 类型默认转换
        /// </summary>
        /// <param name="data">数据</param>
        public static implicit operator DResult<T>(T data) => Ok(data);
    }

    /// <summary>
    /// 数据结果
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DResults<T> : DResult<List<T>>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="data">分页的数据</param>
        public DResults(PagedList<T> data) : base(data?.ToList()) => Total = data?.Count ?? 0;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="total">总条数</param>
        /// <param name="data">数据</param>
        public DResults(int total, List<T> data) : base(data) => Total = total;

        /// <summary>
        /// 总条数
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// 类型默认转换
        /// </summary>
        /// <param name="data">数据</param>
        public static implicit operator DResults<T>(PagedList<T> data) => Ok(data);
    }
}
