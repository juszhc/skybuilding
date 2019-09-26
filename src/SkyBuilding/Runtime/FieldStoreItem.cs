﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SkyBuilding.Runtime
{
    /// <summary>
    /// 字段仓储
    /// </summary>
    public class FieldStoreItem : StoreItem<FieldInfo>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="info">字段信息</param>
        public FieldStoreItem(FieldInfo info) : base(info)
        {
        }

        /// <summary>
        /// 字段类型
        /// </summary>
        public override Type MemberType => Member.FieldType;
        /// <summary>
        /// 可读
        /// </summary>
        public override bool CanRead => Member.IsPublic;
        /// <summary>
        /// 可写
        /// </summary>
        public override bool CanWrite => Member.IsPublic && !Member.IsInitOnly;
    }
}
