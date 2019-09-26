using System;

namespace SkyBuilding
{
    /// <summary>
    /// 单例封装类
    /// </summary>
    /// <typeparam name="T">基类类型</typeparam>
    public static class Singleton<T>
    {
        /// <summary>
        /// 静态构造函数
        /// </summary>
        static Singleton() { }

        /// <summary>
        /// 单例
        /// </summary>
        public static T Instance = (T)Activator.CreateInstance(typeof(T), true);
    }
}
