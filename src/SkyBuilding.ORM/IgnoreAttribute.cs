﻿using System;

namespace SkyBuilding.ORM
{
    /// <summary>
    /// 忽略的键
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class IgnoreAttribute : Attribute
    {
    }
}
