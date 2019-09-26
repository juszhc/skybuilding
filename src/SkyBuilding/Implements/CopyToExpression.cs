using SkyBuilding.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace SkyBuilding.Implements
{
    /// <summary>
    /// 拷贝表达式
    /// </summary>
    public class CopyToExpression : ProfileExpression<CopyToExpression>, ICopyToExpression, IProfileConfiguration, IProfile
    {
        /// <summary>
        /// 类型创建器
        /// </summary>
        public Func<Type, object> ServiceCtor { get; } = Activator.CreateInstance;

        /// <summary>
        /// 匹配模式
        /// </summary>
        public PatternKind Kind { get; } = PatternKind.Property;

        /// <summary>
        /// 允许空目标值。
        /// </summary>
        public bool? AllowNullDestinationValues { get; } = true;

        /// <summary>
        /// 允许空值传播映射。
        /// </summary>
        public bool? AllowNullPropagationMapping { get; } = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        public CopyToExpression() { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="profile"></param>
        public CopyToExpression(IProfileConfiguration profile)
        {
            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            ServiceCtor = profile.ServiceCtor ?? Activator.CreateInstance;
            Kind = profile.Kind;
            AllowNullDestinationValues = profile.AllowNullDestinationValues ?? true;
            AllowNullPropagationMapping = profile.AllowNullPropagationMapping ?? false;
        }


        /// <summary>
        /// 对象复制
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="def">默认值</param>
        /// <returns></returns>
        public T CopyTo<T>(T source, T def = default)
        {
            if (source == null)
                return def;

            try
            {
                return UnsafeCopyTo(source);
            }
            catch
            {
                return def;
            }
        }

        /// <summary>
        /// 对象复制
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        public object CopyTo(object source, Type conversionType)
        {
            if (conversionType is null)
                throw new ArgumentNullException(nameof(conversionType));

            if (source is null) return null;

            var sourceType = source.GetType();

            if (!(sourceType == conversionType || conversionType.IsAssignableFrom(sourceType)))
                return null;

            var invoke = Create(sourceType, conversionType);

            try
            {
                return invoke.Invoke(source);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 对象复制（不安全）
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="def">默认值</param>
        /// <returns></returns>
        private T UnsafeCopyTo<T>(T source)
        {
            var invoke = Create<T>(source.GetType());

            return invoke.Invoke(source);
        }

        #region 反射使用

        private static MethodInfo GetMethodInfo<T>(Func<object, T> func) => func.Method;

        private static IEnumerable GetEnumerableByObject(object source)
        {
            yield return source.CopyTo();
        }

        private static IEnumerable<T> GetEnumerableByObjectG<T>(object source, CopyToExpression copyTo)
        {
            yield return copyTo.UnsafeCopyTo((T)source);
        }

        private static IEnumerable<T> GetEnumerableByEnumerableG<T>(IEnumerable source, CopyToExpression copyTo)
        {
            foreach (T item in source)
            {
                yield return copyTo.UnsafeCopyTo(item);
            }
        }

        private static ICollection<T> GetListByObject<T>(object source, CopyToExpression copyTo) => GetCollectionByObject<T, List<T>>(source, copyTo);

        private static TResult GetCollectionByObject<T, TResult>(object source, CopyToExpression copyTo) where TResult : ICollection<T>
        {
            var value = copyTo.UnsafeCopyTo((T)source);

            var list = (TResult)copyTo.ServiceCtor(typeof(TResult));

            list.Add(value);

            return list;
        }

        private static ICollection<T> GetListByEnumerable<T>(IEnumerable source, CopyToExpression copyTo) => GetCollectionByEnumerableG<T, List<T>>(source, copyTo);

        private static TResult GetCollectionByEnumerableG<T, TResult>(IEnumerable source, CopyToExpression copyTo) where TResult : ICollection<T>
        {
            var list = (TResult)copyTo.ServiceCtor(typeof(TResult));

            foreach (T item in source)
            {
                list.Add(copyTo.UnsafeCopyTo(item));
            }

            return list;
        }

        #endregion

        /// <summary>
        /// 相似的对象（相同类型或目标类型继承源类型）
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByLike<TResult>(Type sourceType, Type conversionType)
        {
            var method = ServiceCtor.Method;

            Expression bodyExp = Convert(method.IsStatic
                ? Call(method, Constant(conversionType))
                : Call(Constant(ServiceCtor.Target), method, Constant(conversionType))
                , conversionType);

            var typeStore = RuntimeTypeCache.Instance.GetCache(conversionType);

            var list = new List<Expression>();

            var parameterExp = Parameter(typeof(object), "source");

            var nullCst = Constant(null);

            var valueExp = Variable(sourceType, "value");

            var targetExp = Variable(conversionType, "target");

            list.Add(Assign(valueExp, Convert(parameterExp, sourceType)));

            list.Add(Assign(targetExp, bodyExp));

            if (conversionType == sourceType)
            {
                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanWrite && x.CanRead)
                       .ForEach(info => Config(info, Property(targetExp, info.Member), Property(valueExp, info.Member)));
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanWrite && x.CanRead)
                       .ForEach(info => Config(info, Field(targetExp, info.Member), Field(valueExp, info.Member)));
                }
            }
            else
            {
                var typeCache = RuntimeTypeCache.Instance.GetCache(sourceType);

                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanWrite).ForEach(info =>
                    {
                        var item = typeCache.PropertyStores.First(x => x.CanRead && x.Name == info.Name);

                        Config(info, Property(targetExp, info.Member), Property(valueExp, info.Member));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanWrite).ForEach(info =>
                    {
                        var item = typeCache.FieldStores.First(x => x.CanRead && x.Name == info.Name);

                        Config(info, Field(targetExp, info.Member), Field(valueExp, item.Member));
                    });
                }
            }

            list.Add(targetExp);

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { valueExp, targetExp }, list), parameterExp);

            return lamdaExp.Compile();

            void Config<T>(StoreItem<T> info, Expression left, Expression right) where T : MemberInfo
            {
                if (left.Type.IsValueType || AllowNullDestinationValues.Value && AllowNullPropagationMapping.Value)
                {
                    list.Add(Assign(left, right));
                    return;
                }

                if (info.CanRead && !AllowNullPropagationMapping.Value && !AllowNullDestinationValues.Value && left.Type == typeof(string))
                {
                    list.Add(Assign(left, Coalesce(right, Coalesce(left, Constant(string.Empty)))));
                    return;
                }

                if (!AllowNullPropagationMapping.Value)
                {
                    list.Add(IfThen(NotEqual(right, nullCst), Assign(left, right)));
                }
                else if (!info.CanRead || left.Type != typeof(string))
                {
                    list.Add(Assign(left, right));
                    return;
                }

                if (!AllowNullDestinationValues.Value)
                {
                    list.Add(IfThen(Equal(left, nullCst), Assign(left, Constant(string.Empty))));
                }
            }
        }

        /// <summary>
        /// 可空类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ToNullable<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            if (sourceType != conversionType)
                throw new InvalidCastException();

            var parameterExp = Parameter(typeof(object), "source");

            var valueExp = Variable(conversionType, "value");

            var typeStore = RuntimeTypeCache.Instance.GetCache(conversionType);

            var ctorInfo = typeStore.ConstructorStores.First(x => x.ParameterStores.Count == 1);

            var propInfo = typeStore.PropertyStores.First(x => x.Name == "Value");

            var bodyExp = New(ctorInfo.Member, Property(valueExp, propInfo.Member));

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { valueExp }, Assign(valueExp, Convert(parameterExp, conversionType)), bodyExp), parameterExp);

            return lamdaExp.Compile();

        }

        /// <summary>
        /// 值类型转目标类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByValueType<TResult>(Type sourceType, Type conversionType) => source => (TResult)source;

        /// <summary>
        /// 源类型转值类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected override Func<object, TResult> ToValueType<TResult>(Type sourceType, Type conversionType) => source => (TResult)source;

        /// <summary>
        /// 对象转可迭代类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected virtual Func<object, TResult> ByObjectToEnumarable<TResult>(Type sourceType, Type conversionType)
        {
            var parameterExp = Parameter(typeof(object), "source");

            var method = GetMethodInfo(GetEnumerableByObject);

            var bodyExp = Call(method, parameterExp);

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        /// <summary>
        /// 对象转可迭代类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByObjectToEnumarableG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var parameterExp = Parameter(typeof(object), "source");

            var method = typeof(CopyToExpression).GetMethod(nameof(GetEnumerableByObjectG), BindingFlags.NonPublic | BindingFlags.Static);

            var methodG = method.MakeGenericMethod(genericType);

            var bodyExp = Call(methodG, parameterExp, Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        /// <summary>
        /// 对象转普通数据
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByObjectToCommon<TResult>(Type sourceType, Type conversionType)
        {
            if (conversionType == typeof(IEnumerable))
            {
                return ByObjectToEnumarable<TResult>(sourceType, conversionType);
            }

            if (conversionType.IsAbstract || conversionType.IsInterface)
                throw new InvalidCastException();

            throw new InvalidCastException();
        }

        /// <summary>
        /// 对象转集合
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByObjectToCollectionG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var parameterExp = Parameter(typeof(object), "source");

            MethodInfo methodG;
            if (conversionType.IsInterface)
            {
                var method = typeof(CopyToExpression).GetMethod(nameof(GetListByObject), BindingFlags.NonPublic | BindingFlags.Static);
                methodG = method.MakeGenericMethod(genericType);
            }
            else
            {
                var method = typeof(CopyToExpression).GetMethod(nameof(GetCollectionByObject), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType, conversionType);
            }

            var bodyExp = Call(methodG, parameterExp, Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        /// <summary>
        /// 可迭代类型转可迭代类型
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByEnumarableToEnumarableG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var parameterExp = Parameter(typeof(object), "source");

            var method = typeof(CopyToExpression).GetMethod(nameof(GetEnumerableByEnumerableG), BindingFlags.NonPublic | BindingFlags.Static);

            var methodG = method.MakeGenericMethod(genericType);

            var bodyExp = Call(methodG, Convert(parameterExp, typeof(IEnumerable)), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        /// <summary>
        /// 可迭代类型转集合
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByEnumarableToCollectionG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var parameterExp = Parameter(typeof(object), "source");

            MethodInfo methodG;
            if (conversionType.IsInterface)
            {
                var method = typeof(CopyToExpression).GetMethod(nameof(GetListByEnumerable), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType);
            }
            else
            {
                var method = typeof(CopyToExpression).GetMethod(nameof(GetCollectionByEnumerableG), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType, conversionType);
            }

            var bodyExp = Call(methodG, Convert(parameterExp, typeof(IEnumerable)), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }
    }
}
