﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SkyBuilding.Runtime
{
    /// <summary>
    /// 方法仓库
    /// </summary>
    public class MethodStoreItem : StoreItem<MethodInfo>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="info">方法</param>
        public MethodStoreItem(MethodInfo info) : base(info)
        {
        }

        /// <summary>
        /// 放回值类型
        /// </summary>
        public override Type MemberType => Member.ReturnType;

        /// <summary>
        /// 可以调用
        /// </summary>
        public override bool CanRead => Member.IsPublic;

        /// <summary>
        /// 可修改
        /// </summary>
        public override bool CanWrite => false;

        private ReadOnlyCollection<ParameterStoreItem> parameterStores;
        private readonly static object Lock_ParameterObj = new object();
        /// <summary>
        /// 参数信息
        /// </summary>
        public ReadOnlyCollection<ParameterStoreItem> ParameterStores
        {
            get
            {
                if (parameterStores == null)
                {
                    lock (Lock_ParameterObj)
                    {
                        if (parameterStores == null)
                        {
                            parameterStores = Member.GetParameters()
                                .Select(info => new ParameterStoreItem(info))
                                .ToList().AsReadOnly();
                        }
                    }
                }
                return parameterStores;
            }
        }
    }
}
