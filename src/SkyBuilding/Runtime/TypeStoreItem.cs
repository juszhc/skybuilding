﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace SkyBuilding.Runtime
{
    /// <summary>
    /// 类型地图
    /// </summary>
    public class TypeStoreItem : StoreItem
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="type">类型</param>
        public TypeStoreItem(Type type) : base(Attribute.GetCustomAttributes(type))
        {
            Type = type;
        }

        /// <summary>
        /// 类型名称
        /// </summary>
        public override string Name => Type.Name;

        /// <summary>
        /// 类型全名
        /// </summary>
        public string FullName => Type.FullName;

        /// <summary>
        /// 类型
        /// </summary>
        public Type Type { get; }

        private ReadOnlyCollection<PropertyStoreItem> propertyStores;
        private readonly static object Lock_PropertyObj = new object();

        /// <summary>
        /// 属性
        /// </summary>
        public ReadOnlyCollection<PropertyStoreItem> PropertyStores
        {
            get
            {
                if (propertyStores == null)
                {
                    lock (Lock_PropertyObj)
                    {
                        if (propertyStores == null)
                        {
                            propertyStores = Type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                .Select(info => new PropertyStoreItem(info))
                                .ToList().AsReadOnly();
                        }
                    }
                }

                return propertyStores;
            }
        }

        private ReadOnlyCollection<FieldStoreItem> fieldStores;
        private readonly static object Lock_FieldObj = new object();
        /// <summary>
        /// 字段
        /// </summary>
        public ReadOnlyCollection<FieldStoreItem> FieldStores
        {
            get
            {
                if (fieldStores == null)
                {
                    lock (Lock_FieldObj)
                    {
                        if (fieldStores == null)
                        {
                            fieldStores = Type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                                .Select(info => new FieldStoreItem(info))
                                .ToList().AsReadOnly();
                        }
                    }
                }
                return fieldStores;
            }
        }

        private ReadOnlyCollection<MethodStoreItem> methodStores;
        private readonly static object Lock_MethodObj = new object();
        /// <summary>
        /// 方法
        /// </summary>
        public ReadOnlyCollection<MethodStoreItem> MethodStores
        {
            get
            {
                if (methodStores == null)
                {
                    lock (Lock_MethodObj)
                    {
                        if (methodStores == null)
                        {
                            methodStores = Type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                .Select(info => new MethodStoreItem(info))
                                .ToList().AsReadOnly();
                        }
                    }
                }
                return methodStores;
            }
        }

        private ReadOnlyCollection<ConstructorStoreItem> constructorStores;
        private readonly static object Lock_ConstructorObj = new object();
        /// <summary>
        /// 构造函数
        /// </summary>
        public ReadOnlyCollection<ConstructorStoreItem> ConstructorStores
        {
            get
            {
                if (constructorStores == null)
                {
                    lock (Lock_ConstructorObj)
                    {
                        if (constructorStores == null)
                        {
                            constructorStores = Type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                                .Select(info => new ConstructorStoreItem(info))
                                .ToList().AsReadOnly();
                        }
                    }
                }
                return constructorStores;
            }
        }
    }
}
