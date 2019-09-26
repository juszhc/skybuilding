using SkyBuilding.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace SkyBuilding.Implements
{
    /// <summary>
    /// 数据映射
    /// </summary>
    public class MapToExpression : ProfileExpression<MapToExpression>, IMapToExpression, IProfileConfiguration, IProfile
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
        public MapToExpression() { }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="profile">配置</param>
        public MapToExpression(IProfileConfiguration profile)
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
        /// 对象映射
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="obj">数据源</param>
        /// <param name="def">默认值</param>
        /// <returns></returns>
        public T MapTo<T>(object obj, T def = default)
        {
            if (obj == null) return def;

            try
            {
                var value = UnsafeMapTo<T>(obj);

                if (value == null)
                    return def;

                return value;
            }
            catch
            {
                return def;
            }
        }

        /// <summary>
        /// 不安全的映射（有异常）
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="source">数据源</param>
        /// <returns></returns>
        private T UnsafeMapTo<T>(object source)
        {
            var invoke = Create<T>(source.GetType());

            return invoke.Invoke(source);
        }

        /// <summary>
        /// 对象映射
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        public object MapTo(object source, Type conversionType)
        {
            if (source == null) return null;

            try
            {
                var invoke = Create(source.GetType(), conversionType);

                return invoke.Invoke(source);
            }
            catch
            {
                return null;
            }
        }

        #region 反射使用

        private static MethodInfo GetMethodInfo<T>(Func<object, T> func) => func.Method;

        private static MethodInfo GetMethodInfo<T1, T2>(Func<T1, MapToExpression, T2> func) => func.Method;

        private static MethodInfo GetMethodInfo<T1, T2, T3>(Func<T1, T2, T3> func) => func.Method;

        private static IEnumerable GetEnumerableByObject(object source)
        {
            yield return source.CopyTo();
        }

        private static IEnumerable<T> GetEnumerableByObjectG<T>(object source, MapToExpression mapTo)
        {
            yield return mapTo.UnsafeMapTo<T>(source);
        }

        private static IEnumerable<T> GetEnumerableByEnumerableG<T>(IEnumerable source, MapToExpression mapTo)
        {
            foreach (var item in source)
            {
                yield return mapTo.UnsafeMapTo<T>(item);
            }
        }

        private static ICollection<T> GetListByObject<T>(object source, MapToExpression mapTo) => GetCollectionByObject<T, List<T>>(source, mapTo);

        private static TResult GetCollectionByObject<T, TResult>(object source, MapToExpression mapTo) where TResult : ICollection<T>
        {
            var value = mapTo.UnsafeMapTo<T>(source);

            var list = (TResult)mapTo.ServiceCtor(typeof(TResult));

            list.Add(value);

            return list;
        }

        private static ICollection<T> GetListByEnumerable<T>(IEnumerable source, MapToExpression mapTo) => GetCollectionByEnumerableG<T, List<T>>(source, mapTo);

        private static TResult GetCollectionByEnumerableG<T, TResult>(IEnumerable source, MapToExpression mapTo) where TResult : ICollection<T>
        {
            var list = (TResult)mapTo.ServiceCtor(typeof(TResult));

            foreach (object item in source)
            {
                list.Add(mapTo.UnsafeMapTo<T>(item));
            }

            return list;
        }


        private static bool EqaulsString(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<KeyValuePair<string, object>> ByDataRowToEnumarableG(DataRow dr, MapToExpression mapTo)
        {
            foreach (DataColumn item in dr.Table.Columns)
            {
                yield return new KeyValuePair<string, object>(item.ColumnName, dr[item.ColumnName]);
            }
        }

        private static IDictionary<string, object> ByDataRowToIDictionary(DataRow dr, MapToExpression mapTo) => ByDataRowToDictionary<Dictionary<string, object>>(dr, mapTo);

        private static TResult ByDataRowToDictionary<TResult>(DataRow dr, MapToExpression mapTo) where TResult : IDictionary<string, object>
        {
            var dic = (TResult)mapTo.ServiceCtor(typeof(TResult));

            foreach (DataColumn item in dr.Table.Columns)
            {
                dic.Add(item.ColumnName, dr[item.ColumnName]);
            }

            return dic;
        }
        private static List<T> ByDataTableToListG<T>(DataTable table, MapToExpression mapTo)
        {
            var list = new List<T>();

            foreach (var item in table.Rows)
            {
                list.Add(mapTo.UnsafeMapTo<T>(item));
            }

            return list;
        }
        private static TResult ByDataTableToCollectionG<T, TResult>(DataTable table, MapToExpression mapTo) where TResult : ICollection<T>
        {
            var list = (TResult)mapTo.ServiceCtor(typeof(TResult));

            foreach (var item in table.Rows)
            {
                list.Add(mapTo.UnsafeMapTo<T>(item));
            }

            return list;
        }

        #endregion

        protected override Func<object, TResult> CreateExpression<TResult>(Type sourceType)
        {
            if (typeof(IEnumerable).IsAssignableFrom(sourceType))
                return ByDataReaderToObject<TResult>(sourceType, typeof(TResult));

            return base.CreateExpression<TResult>(sourceType);
        }

        /// <summary>
        /// 相似的对象（相同类型或目标类型继承源类型）
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByLike<TResult>(Type sourceType, Type conversionType) => ByObjectToObject<TResult>(sourceType, conversionType);

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
            if (genericType.IsGenericType && genericType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return ByObjectToEnumarableKvG<TResult>(sourceType, conversionType, genericType);
            }

            var parameterExp = Parameter(typeof(object), "source");

            var method = typeof(MapToExpression).GetMethod(nameof(GetEnumerableByObjectG), BindingFlags.NonPublic | BindingFlags.Static);

            var methodG = method.MakeGenericMethod(genericType);

            var bodyExp = Call(methodG, parameterExp, Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByObjectToEnumarableKvG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var types = genericType.GetGenericArguments();

            var typeStore = RuntimeTypeCache.Instance.GetCache(sourceType);

            var list = new List<Expression>();

            var resultExp = Variable(conversionType, "result");

            var targetExp = Variable(sourceType, "target");

            var sourceExp = Parameter(typeof(object), "source");

            list.Add(Assign(targetExp, Convert(sourceExp, sourceType)));

            var methodCtor = ServiceCtor.Method;

            var targetType = typeof(List<KeyValuePair<string, object>>);

            var bodyExp = methodCtor.IsStatic ?
                Call(methodCtor, Constant(targetType)) :
                Call(Constant(ServiceCtor.Target), methodCtor, Constant(targetType));

            list.Add(Assign(resultExp, Convert(bodyExp, targetType)));

            var method = conversionType.GetMethod("Add", new Type[] { genericType }) ?? throw new NotSupportedException();

            var typeStore2 = RuntimeTypeCache.Instance.GetCache(genericType);

            var ctorSotre = typeStore2.ConstructorStores.Where(x => x.ParameterStores.Count == 2).First();

            if (Kind == PatternKind.Property || Kind == PatternKind.All)
            {
                typeStore.PropertyStores.Where(x => x.CanRead).ForEach(info =>
                {
                    list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Property(targetExp, info.Member), types[1]))));
                });
            }

            if (Kind == PatternKind.Field || Kind == PatternKind.All)
            {
                typeStore.FieldStores.Where(x => x.CanRead).ForEach(info =>
                {
                    list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Field(targetExp, info.Member), types[1]))));
                });
            }
            list.Add(Convert(resultExp, conversionType));

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { targetExp, resultExp }, list), resultExp, sourceExp);

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
            if (sourceType == typeof(DataSet))
                throw new InvalidCastException();

            if (sourceType == typeof(DataTable))
            {
                return ByDataTable<TResult>(sourceType, conversionType);
            }

            if (sourceType == typeof(DataRow))
            {
                return ByDataRow<TResult>(sourceType, conversionType);
            }

            if (sourceType.IsAssignableFrom(typeof(IDataRecord)))
                return ByDataReaderToObject<TResult>(sourceType, conversionType);

            if (conversionType == typeof(IEnumerable))
            {
                return ByObjectToEnumarable<TResult>(sourceType, conversionType);
            }

            if (conversionType.IsAbstract || conversionType.IsInterface)
                throw new InvalidCastException();

            return ByObjectToObject<TResult>(sourceType, conversionType);
        }

        /// <summary>
        /// DataRow 数据源
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected virtual Func<object, TResult> ByDataRow<TResult>(Type sourceType, Type conversionType)
        {
            if (conversionType.IsValueType || conversionType == typeof(string))
            {
                return source =>
                {
                    if (source is DataRow dr)
                    {
                        return dr.IsNull(0) ? default : (TResult)System.Convert.ChangeType(dr[0], conversionType);
                    }

                    return default;
                };
            }

            if (conversionType.IsGenericType)
            {
                if (conversionType.IsInterface)
                {
                    if (conversionType == typeof(IDictionary<string, object>))
                        return ByDataRowToIDictionary<TResult>(sourceType);

                    if (conversionType == typeof(IEnumerable<KeyValuePair<string, object>>))
                        return ByDataRowToEnumarableKv<TResult>(sourceType);

                }

                if (conversionType.IsClass && !conversionType.IsAbstract)
                {
                    var types = conversionType.GetInterfaces();

                    if (types.Any(x => x.IsGenericType && x == typeof(IDictionary<string, object>)))
                        return ByDataRowToDictionary<TResult>(sourceType, conversionType);
                }
            }

            return ByDataRowToObject<TResult>(conversionType);
        }

        private Func<object, TResult> ByDataRowToIDictionary<TResult>(Type sourceType)
        {
            var method = GetMethodInfo<DataRow, IDictionary<string, object>>(ByDataRowToIDictionary);

            var paramterExp = Parameter(typeof(object), "source");

            var bodyExp = Call(method, Convert(paramterExp, sourceType), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, paramterExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByDataRowToObject<TResult>(Type conversionType)
        {
            var list = new List<SwitchCase>();

            var resultExp = Parameter(conversionType, "result");

            var nameExp = Parameter(typeof(string), "name");

            var valueExp = Parameter(typeof(object), "value");

            var typeStore = RuntimeTypeCache.Instance.GetCache(conversionType);

            if (Kind == PatternKind.Property || Kind == PatternKind.All)
            {
                typeStore.PropertyStores.Where(x => x.CanWrite).ForEach(info =>
                {
                    list.Add(SwitchCase(Constant(info.Name), Assign(Property(resultExp, info.Member), Convert(valueExp, info.MemberType))));
                });
            }

            if (Kind == PatternKind.Field || Kind == PatternKind.All)
            {
                typeStore.FieldStores.Where(x => x.CanWrite).ForEach(info =>
                {
                    list.Add(SwitchCase(Constant(info.Name), Assign(Field(resultExp, info.Member), Convert(valueExp, info.MemberType))));
                });
            }

            var bodyExp = Switch(nameExp, null, GetMethodInfo<string, string, bool>(EqaulsString), list.ToArray());

            var lamdaExp = Lambda<Action<TResult, string, object>>(bodyExp, resultExp, nameExp, valueExp);

            var invoke = lamdaExp.Compile();

            return source =>
            {
                if (source is DataRow dr)
                {
                    var result = (TResult)ServiceCtor(conversionType);

                    foreach (DataColumn item in dr.Table.Columns)
                    {
                        invoke.Invoke(result, item.ColumnName, dr[item]);
                    }

                    return result;
                }

                return default;
            };
        }

        private Func<object, TResult> ByDataRowToEnumarableKv<TResult>(Type sourceType)
        {
            var method = GetMethodInfo<DataRow, IEnumerable<KeyValuePair<string, object>>>(ByDataRowToEnumarableG);

            var paramterExp = Parameter(typeof(object), "source");

            var bodyExp = Call(method, Convert(paramterExp, sourceType), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, paramterExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByDataRowToDictionary<TResult>(Type sourceType, Type conversionType)
        {
            var method = typeof(MapToExpression).GetMethod(nameof(ByDataRowToDictionary));

            var methodG = method.MakeGenericMethod(conversionType);

            var paramterExp = Parameter(typeof(object), "source");

            var bodyExp = Call(methodG, Convert(paramterExp, sourceType), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, paramterExp);

            return lamdaExp.Compile();
        }

        /// <summary>
        /// DataTable 数据源
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected virtual Func<object, TResult> ByDataTable<TResult>(Type sourceType, Type conversionType)
        {
            if (conversionType.IsValueType || conversionType == typeof(string))
            {
                return source =>
                {
                    if (source is DataTable dt)
                    {
                        foreach (var dr in dt.Rows)
                        {
                            return UnsafeMapTo<TResult>(dr);
                        }
                    }

                    return default;
                };
            }

            if (conversionType.IsGenericType)
            {
                if (conversionType.IsInterface)
                {
                    if (conversionType.GetGenericTypeDefinition() == typeof(ICollection<>))
                        return ByDataTableToList<TResult>(sourceType, conversionType, conversionType.GetGenericArguments().First());

                    if (conversionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        return ByDataTableToList<TResult>(sourceType, conversionType, conversionType.GetGenericArguments().First());

                }

                var types = conversionType.GetInterfaces();

                if (conversionType.IsClass && !conversionType.IsAbstract)
                {
                    if (types.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)))
                        return ByDataTableToCollection<TResult>(sourceType, conversionType, types.First(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)).GetGenericArguments().First());
                }
            }

            return source =>
            {
                if (source is DataTable dt)
                {
                    foreach (var dr in dt.Rows)
                    {
                        return UnsafeMapTo<TResult>(dr);
                    }
                }

                return default;
            };
        }

        private Func<object, TResult> ByDataTableToList<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            //? 只取第一条
            if (genericType.IsGenericType && genericType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return source =>
                {
                    if (source is DataTable table)
                    {
                        foreach (var item in table.Rows)
                        {
                            return UnsafeMapTo<TResult>(item);
                        }
                    }

                    return default;
                };

            var method = typeof(MapToExpression).GetMethod(nameof(ByDataTableToListG), BindingFlags.NonPublic | BindingFlags.Static);

            var methodG = method.MakeGenericMethod(genericType);

            var paramterExp = Parameter(typeof(object), "source");

            var bodyExp = Call(methodG, Convert(paramterExp, sourceType), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(Convert(bodyExp, conversionType), paramterExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByDataTableToCollection<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            //? 只取第一条
            if (genericType.IsGenericType && genericType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return source =>
                {
                    if (source is DataTable table)
                    {
                        foreach (var item in table.Rows)
                        {
                            return UnsafeMapTo<TResult>(item);
                        }
                    }

                    return default;
                };

            var parameterExp = Parameter(typeof(object), "source");

            var method = typeof(MapToExpression).GetMethod(nameof(ByDataTableToCollectionG), BindingFlags.NonPublic | BindingFlags.Static);

            var methodG = method.MakeGenericMethod(genericType, conversionType);

            var bodyExp = Call(methodG, Convert(parameterExp, sourceType), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        protected virtual Func<object, TResult> ByDataReaderToObject<TResult>(Type sourceType, Type conversionType)
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

            var indexExp = Variable(typeof(int), "index");

            var negativeExp = Constant(-1);

            list.Add(Assign(valueExp, Convert(parameterExp, sourceType)));

            list.Add(Assign(targetExp, bodyExp));

            var typeMap = new Dictionary<Type, MethodInfo>();

            var getOrdinal = sourceType.GetMethod("GetOrdinal", new Type[] { typeof(string) });

            var isDBNull = sourceType.GetMethod("IsDBNull", new Type[] { typeof(int) });

            if (Kind == PatternKind.Property || Kind == PatternKind.All)
            {
                typeStore.PropertyStores.Where(x => x.CanWrite && x.CanRead)
                   .ForEach(info => Config(info, Property(targetExp, info.Member)));
            }

            if (Kind == PatternKind.Field || Kind == PatternKind.All)
            {
                typeStore.FieldStores.Where(x => x.CanWrite && x.CanRead)
                   .ForEach(info => Config(info, Field(targetExp, info.Member)));
            }

            list.Add(targetExp);

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { valueExp, indexExp, targetExp }, list), parameterExp);

            return lamdaExp.Compile();

            void Config<T>(StoreItem<T> info, Expression left) where T : MemberInfo
            {
                var memberType = info.MemberType;

                if (memberType.IsNullable())
                {
                    memberType = Nullable.GetUnderlyingType(memberType);
                }

                list.Add(Assign(indexExp, Call(valueExp, getOrdinal, Constant(info.Name))));

                var testIndexExp = GreaterThan(indexExp, negativeExp);

                var testNullExp = Not(Call(valueExp, isDBNull, indexExp));

                var testExp = AndAlso(testIndexExp, testNullExp);

                if (typeMap.TryGetValue(memberType, out MethodInfo methodInfo))
                {
                    list.Add(IfThen(testExp, Assign(left, Call(valueExp, methodInfo, indexExp))));

                    return;
                }

                list.Add(IfThen(testExp, Assign(left, Convert(Call(valueExp, typeMap[typeof(object)], indexExp), memberType))));
            }
        }

        /// <summary>
        /// 对象转对象
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <returns></returns>
        protected virtual Func<object, TResult> ByObjectToObject<TResult>(Type sourceType, Type conversionType)
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
                        var item = typeCache.PropertyStores.FirstOrDefault(x => x.CanRead && x.Name == info.Name);

                        if (item is null) return;

                        Config(info, Property(targetExp, info.Member), Property(valueExp, item.Member));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanWrite).ForEach(info =>
                    {
                        var item = typeCache.FieldStores.First(x => x.CanRead && x.Name == info.Name);

                        if (item is null) return;

                        Config(info, Field(targetExp, info.Member), Field(valueExp, item.Member));
                    });
                }
            }

            list.Add(targetExp);

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { valueExp, targetExp }, list), parameterExp);

            return lamdaExp.Compile();

            void Config<T>(StoreItem<T> info, Expression left, Expression right) where T : MemberInfo
            {
                if (left.Type != right.Type)
                {
                    right = Convert(right, left.Type);
                }

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
        /// 对象转集合
        /// </summary>
        /// <typeparam name="TResult">目标类型</typeparam>
        /// <param name="sourceType">源类型</param>
        /// <param name="conversionType">目标类型</param>
        /// <param name="genericType">泛型参数</param>
        /// <returns></returns>
        protected override Func<object, TResult> ByObjectToCollectionG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            if (genericType.IsGenericType && genericType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return conversionType.IsInterface ? ByObjectToICollectionKvG<TResult>(sourceType, conversionType, genericType) : ByObjectToCollectionKvG<TResult>(sourceType, conversionType, genericType);
            }

            var parameterExp = Parameter(typeof(object), "source");

            MethodInfo methodG;
            if (conversionType.IsInterface)
            {
                var method = typeof(MapToExpression).GetMethod(nameof(GetListByObject), BindingFlags.NonPublic | BindingFlags.Static);
                methodG = method.MakeGenericMethod(genericType);
            }
            else
            {
                var method = typeof(MapToExpression).GetMethod(nameof(GetCollectionByObject), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType, conversionType);
            }

            var bodyExp = Call(methodG, parameterExp, Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByObjectToCollectionKvG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var types = genericType.GetGenericArguments();

            var typeStore = RuntimeTypeCache.Instance.GetCache(sourceType);

            var list = new List<Expression>();

            var resultExp = Variable(conversionType, "result");

            var targetExp = Variable(sourceType, "target");

            var sourceExp = Parameter(typeof(object), "source");

            var method = conversionType.GetMethod("Add", types);

            list.Add(Assign(targetExp, Convert(sourceExp, sourceType)));

            var methodCtor = ServiceCtor.Method;

            var bodyExp = methodCtor.IsStatic ?
                Call(methodCtor, Constant(conversionType)) :
                Call(Constant(ServiceCtor.Target), methodCtor, Constant(conversionType));

            list.Add(Assign(resultExp, Convert(bodyExp, conversionType)));

            if (method is null)
            {
                method = conversionType.GetMethod("Add", new Type[] { genericType }) ?? throw new NotSupportedException();

                var typeStore2 = RuntimeTypeCache.Instance.GetCache(genericType);

                var ctorSotre = typeStore2.ConstructorStores.Where(x => x.ParameterStores.Count == 2).First();

                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Property(targetExp, info.Member), types[1]))));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Field(targetExp, info.Member), types[1]))));
                    });
                }
            }
            else
            {
                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, Convert(Constant(info.Name), types[0]), Convert(Property(targetExp, info.Member), types[1])));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, Convert(Constant(info.Name), types[0]), Convert(Field(targetExp, info.Member), types[1])));
                    });
                }
            }

            list.Add(resultExp);

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { targetExp, resultExp }, list), resultExp, sourceExp);

            return lamdaExp.Compile();
        }

        private Func<object, TResult> ByObjectToICollectionKvG<TResult>(Type sourceType, Type conversionType, Type genericType)
        {
            var types = genericType.GetGenericArguments();

            var typeStore = RuntimeTypeCache.Instance.GetCache(sourceType);

            var list = new List<Expression>();

            var resultExp = Variable(conversionType, "result");

            var targetExp = Variable(sourceType, "target");

            var sourceExp = Parameter(typeof(object), "source");

            var method = conversionType.GetMethod("Add", types);

            list.Add(Assign(targetExp, Convert(sourceExp, sourceType)));

            var methodCtor = ServiceCtor.Method;

            var targetType = typeof(List<>).MakeGenericType(genericType);

            var bodyExp = methodCtor.IsStatic ?
                Call(methodCtor, Constant(targetType)) :
                Call(Constant(ServiceCtor.Target), methodCtor, Constant(targetType));

            list.Add(Assign(resultExp, Convert(bodyExp, targetType)));

            if (method is null)
            {
                method = conversionType.GetMethod("Add", new Type[] { genericType }) ?? throw new NotSupportedException();

                var typeStore2 = RuntimeTypeCache.Instance.GetCache(genericType);

                var ctorSotre = typeStore2.ConstructorStores.Where(x => x.ParameterStores.Count == 2).First();

                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Property(targetExp, info.Member), types[1]))));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, New(ctorSotre.Member, Convert(Constant(info.Name), types[0]), Convert(Field(targetExp, info.Member), types[1]))));
                    });
                }
            }
            else
            {
                if (Kind == PatternKind.Property || Kind == PatternKind.All)
                {
                    typeStore.PropertyStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, Convert(Constant(info.Name), types[0]), Convert(Property(targetExp, info.Member), types[1])));
                    });
                }

                if (Kind == PatternKind.Field || Kind == PatternKind.All)
                {
                    typeStore.FieldStores.Where(x => x.CanRead).ForEach(info =>
                    {
                        list.Add(Call(resultExp, method, Convert(Constant(info.Name), types[0]), Convert(Field(targetExp, info.Member), types[1])));
                    });
                }
            }

            list.Add(Convert(resultExp, conversionType));

            var lamdaExp = Lambda<Func<object, TResult>>(Block(new[] { targetExp, resultExp }, list), resultExp, sourceExp);

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

            var method = typeof(MapToExpression).GetMethod(nameof(GetEnumerableByEnumerableG), BindingFlags.NonPublic | BindingFlags.Static);

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
                var method = typeof(MapToExpression).GetMethod(nameof(GetListByEnumerable), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType);
            }
            else
            {
                var method = typeof(MapToExpression).GetMethod(nameof(GetCollectionByEnumerableG), BindingFlags.NonPublic | BindingFlags.Static);

                methodG = method.MakeGenericMethod(genericType, conversionType);
            }

            var bodyExp = Call(methodG, Convert(parameterExp, typeof(IEnumerable)), Constant(this));

            var lamdaExp = Lambda<Func<object, TResult>>(bodyExp, parameterExp);

            return lamdaExp.Compile();
        }
    }
}
