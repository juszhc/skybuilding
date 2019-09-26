﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SkyBuilding.ORM
{
    /// <summary>
    /// 实体基类
    /// </summary>
    public class BaseEntity : IEntiy
    {
    }
    /// <summary>
    /// 实体基类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BaseEntity<T> : BaseEntity
    {
        [Key]
        public virtual T Id { set; get; }
    }
}
