﻿using SkyBuilding.ORM.Exceptions;
using SkyBuilding;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace SkyBuilding.ORM.Builders
{
    /// <summary>
    /// 构建器
    /// </summary>
    public abstract class Builder : ExpressionVisitor, IBuilder, IDisposable
    {
        /// <summary>
        /// 替换表达式
        /// </summary>
        private class ReplaceExpressionVisitor : ExpressionVisitor
        {
            private readonly Expression _oldExpression;
            private readonly Expression _newExpression;

            public ReplaceExpressionVisitor(Expression oldExpression, Expression newExpression)
            {
                _oldExpression = oldExpression;
                _newExpression = newExpression;
            }
            public override Expression Visit(Expression node)
            {
                if (_oldExpression == node)
                    return base.Visit(_newExpression);
                return base.Visit(node);
            }
        }

        public static readonly List<IVisitter> Empty = new List<IVisitter>();

        private static readonly ConcurrentDictionary<Type, Type> EntryCache = new ConcurrentDictionary<Type, Type>();

        private Writer _write;

        protected Writer SQLWriter => _write ?? (_write = CreateWriter(settings, CreateWriterMap(settings)));

        private readonly ISQLCorrectSettings settings;

        private bool buildWhereBoth = false;//建立And或Or条件

        private bool isIgnoreNullable = false; //忽略Nullable的成员

        private ITableRegions _CurrentRegions = null;

        private Func<ITableRegions, string> tableFactory;

        private ConcurrentDictionary<Type, string> _PrefixCache;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="settings">SQL矫正设置</param>
        public Builder(ISQLCorrectSettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        /// <summary>
        /// 创建写入映射关系
        /// </summary>
        /// <param name="settings">修正配置</param>
        /// <returns></returns>
        protected virtual IWriterMap CreateWriterMap(ISQLCorrectSettings settings) => new WriterMap(settings);

        /// <summary>
        /// 创建写入流
        /// </summary>
        /// <param name="settings">修正配置</param>
        /// <param name="writeMap">写入映射关系</param>
        /// <returns></returns>
        protected virtual Writer CreateWriter(ISQLCorrectSettings settings, IWriterMap writeMap) => new Writer(settings, writeMap);

        /// <summary>
        /// 创建构造器
        /// </summary>
        /// <param name="settings">修正配置</param>
        /// <param name="writeMap">写入映射关系</param>
        /// <returns></returns>
        protected abstract Builder CreateBuilder(ISQLCorrectSettings settings);

        /// <summary>
        /// 用于承载子父关系
        /// </summary>
        protected Builder Parent { get; private set; }
        /// <summary>
        /// 构建条件语句
        /// </summary>
        protected internal bool BuildWhere { get; protected set; }

        private Action<Writer> Ready;

        private Action<Writer> Complete;

        /// <summary>
        /// 创建构造器（保持参数传递）
        /// </summary>
        /// <returns></returns>
        private Builder CreateBuilder()
        {
            var builder = CreateBuilder(settings);

            builder.Parent = this;

            builder.Ready = writer =>
            {
                writer.ParameterIndex = SQLWriter.ParameterIndex;
                writer.Parameters = Parameters;
            };

            builder.Complete = writer => SQLWriter.ParameterIndex = writer.ParameterIndex;

            return builder;
        }

        /// <summary>
        /// 表达式测评
        /// </summary>
        /// <param name="node">表达式</param>
        public virtual void Evaluate(Expression node)
        {
            _PrefixCache = new ConcurrentDictionary<Type, string>();

            Ready?.Invoke(SQLWriter);

            base.Visit(node);

            Complete?.Invoke(SQLWriter);
        }

        protected virtual Type GetRealType(Type type) => type;

        protected Type GetInitialType(Type type, bool throwsError = true)
        {
            while (type.IsSubclassOf(typeof(Expression)) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<,>))
            {
                if (type.IsGenericType)
                {
                    type = type.GetGenericArguments().First(item => item.IsClass);
                }
                else
                {
                    type = type.BaseType;
                }
            }

            return GetEntityType(GetRealType(type), throwsError);
        }

        protected string GetOrAddTablePrefix(Type tableType, string name = null)
        {
            string highName = MakePrefixFrom(tableType = GetInitialType(tableType), name);

            if (highName == null)
            {
                highName = MakePrefixFrom(tableType, name, true);
            }

            if (highName == null)
            {
                name = name ?? string.Empty;

                if (name.Length > 0 && _PrefixCache.Values.Contains(name))
                {
                    throw new DException($"表别名（{name}）已被使用!");
                }
                _PrefixCache.TryAdd(tableType, name);
            }

            return highName ?? name;
        }

        protected void SetTableFactory(Func<ITableRegions, string> table) => tableFactory = table;

        private string MakePrefixFrom(Type type, string name, bool noCheck = false)
        {
            if (_PrefixCache.TryGetValue(type, out string highName))
            {
                if (noCheck || name is null || name.Length == 0 || name == highName)
                    return highName;
            }

            return Parent?.MakePrefixFrom(type, name, noCheck);
        }

        public void WriteTable(Type tableType) => SQLWriter.TableName(GetTableName(MakeTableRegions(tableType)), GetOrAddTablePrefix(tableType));

        public void WriteTable(ITableRegions tableRegions) => SQLWriter.TableName(GetTableName(tableRegions), GetOrAddTablePrefix(tableRegions.TableType));

        private string GetTableName(ITableRegions regions) => tableFactory?.Invoke(regions) ?? regions.TableName;

        protected T WrapNot<T>(Func<T> factory)
        {
            SQLWriter.Not ^= true;

            var value = factory.Invoke();

            SQLWriter.Not ^= true;

            return value;
        }

        protected void WrapNot(Action factory)
        {
            SQLWriter.Not ^= true;

            factory.Invoke();

            SQLWriter.Not ^= true;
        }

        /// <summary>
        /// 生成Like方法
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private Expression VisitLike(MethodCallExpression node)
        {
            Expression arg = node.Arguments[0];

            object value = TryThrow(() => arg.GetValueFromExpression());

            if (value == null) return node;

            if (!(value is string likeStr))
                throw new ExpressionNotSupportedException($"仅支持参数类型为System.String的函数({node.Method.Name})方法。");

            if (likeStr.Length == 0)
            {
                base.Visit(node.Object);

                WrapNot(SQLWriter.IsNull);

                return node;
            }

            base.Visit(node.Object);

            SQLWriter.Like();

            if (node.Method.Name == MethodCall.EndsWith || node.Method.Name == MethodCall.Contains)
                likeStr = "%" + likeStr;

            if (node.Method.Name == MethodCall.StartsWith || node.Method.Name == MethodCall.Contains)
                likeStr += "%";

            if (arg is MemberExpression member)
            {
                SQLWriter.Parameter(member.Member.Name, likeStr);
            }
            else
            {
                SQLWriter.Parameter(likeStr);
            }

            return node;
        }

        private void BooleanFalse(bool allwaysFalse = false)
        {
            if (allwaysFalse || !SQLWriter.Not)
            {
                SQLWriter.BooleanTrue();

                SQLWriter.Equal();

                SQLWriter.BooleanFalse();
            }
        }

        private T TryThrow<T>(Func<T> factory)
        {
            try
            {
                return factory();
            }
            catch (ArgumentException arg)
            {
                throw new ExpressionNotSupportedException("表达式参数异常!", arg);
            }
            catch (Exception ex)
            {
                throw new ExpressionNotSupportedException("无法分析的表达式!", ex);
            }
        }

        private Expression InMethod(MethodCallExpression node)
        {
            bool whereIsNotEmpty = false;

            WriteAppendAtFix(() =>
            {
                if (whereIsNotEmpty)
                    base.Visit(node.Arguments[node.Object == null ? 1 : 0]);
                else
                    BooleanFalse();

            }, () =>
              {
                  var enumerable = TryThrow(() => (IEnumerable)(node.Object ?? node.Arguments[0]).GetValueFromExpression());
                  var enumerator = enumerable.GetEnumerator();

                  if (enumerator.MoveNext())
                  {
                      SQLWriter.Contains();
                      SQLWriter.OpenBrace();
                      SQLWriter.Parameter(enumerator.Current);
                      while (enumerator.MoveNext())
                      {
                          SQLWriter.Delimiter();
                          SQLWriter.Parameter(enumerator.Current);
                      }
                      SQLWriter.CloseBrace();

                      whereIsNotEmpty = true;
                  }
              });

            return node;
        }

        private Expression AnyMethod(MethodCallExpression node)
        {
            var enumerable = TryThrow(() => (IEnumerable)(node.Object ?? node.Arguments[0]).GetValueFromExpression());
            var enumerator = enumerable.GetEnumerator();

            int lamdaIndex = node.Object == null ? 1 : 0;

            if (node.Arguments.Count == lamdaIndex)
            {
                if (enumerator.MoveNext() ^ SQLWriter.Not)
                    return node;

                BooleanFalse(true);

                return node;
            }

            var lamda = node.Arguments[lamdaIndex] as LambdaExpression;

            var paramter = lamda.Parameters.First();

            void VisitDynamic(object value)
            {
                var expression = new ReplaceExpressionVisitor(paramter, Expression.Constant(value, paramter.Type)).Visit(lamda.Body);

                base.Visit(expression);
            }

            if (enumerator.MoveNext())
            {
                if (enumerator.Current == null)
                    throw new ArgumentNullException(paramter.Name);

                SQLWriter.OpenBrace();

                VisitDynamic(enumerator.Current);

                while (enumerator.MoveNext())
                {
                    if (enumerator.Current == null)
                        throw new ArgumentNullException(paramter.Name);

                    SQLWriter.WriteOr();

                    VisitDynamic(enumerator.Current);
                }
                SQLWriter.CloseBrace();

                return node;
            }

            BooleanFalse();

            return node;
        }

        protected ITableRegions MakeTableRegions(Type type)
        {
            Type entityType = GetInitialType(type);

            if (_CurrentRegions == null)
            {
                return _CurrentRegions = MapperRegions.Resolve(entityType);
            }

            return _CurrentRegions.TableType == entityType ? _CurrentRegions : _CurrentRegions = MapperRegions.Resolve(entityType);
        }

        private bool IsStaticVariable(Expression node, bool tryBinary = false)
        {
            if (node.NodeType == ExpressionType.Constant) return true;

            if (node.NodeType == ExpressionType.Parameter) return false;

            if (node is MemberExpression member) return member.Expression == null || IsStaticVariable(member.Expression);

            if (node is MethodCallExpression method)
            {
                if (method.Object == null || IsStaticVariable(method.Object))
                {
                    return method.Arguments.Count == 0 || method.Arguments.All(arg => IsStaticVariable(arg));
                }
            }

            return tryBinary
                   && (node is BinaryExpression binary)
                   && IsStaticVariable(binary.Left, tryBinary)
                   && IsStaticVariable(binary.Right, tryBinary);
        }

        private Expression TrimMethod(MethodCallExpression node)
        {
            if (node.Method.Name == MethodCall.TrimStart || node.Method.Name == MethodCall.Trim)
            {
                SQLWriter.Write("LTRIM");
                SQLWriter.OpenBrace();
            }

            if (node.Method.Name == MethodCall.TrimEnd || node.Method.Name == MethodCall.Trim)
            {
                SQLWriter.Write("RTRIM");
                SQLWriter.OpenBrace();
            }

            base.Visit(node.Object);

            if (node.Method.Name == MethodCall.TrimStart || node.Method.Name == MethodCall.Trim)
            {
                SQLWriter.CloseBrace();
            }

            if (node.Method.Name == MethodCall.TrimEnd || node.Method.Name == MethodCall.Trim)
            {
                SQLWriter.CloseBrace();
            }
            return node;
        }

        private Expression IsNullOrEmptyMethod(MethodCallExpression node)
        {
            SQLWriter.OpenBrace();

            base.Visit(node.Arguments.Count > 0 ? node.Arguments[0] : node.Object);

            SQLWriter.IsNull();

            SQLWriter.WriteOr();

            base.Visit(node.Arguments.Count > 0 ? node.Arguments[0] : node.Object);

            SQLWriter.Equal();
            SQLWriter.EmptyString();
            SQLWriter.CloseBrace();

            return node;
        }

        protected Type GetEntityType(Type repositoryType, bool throwsError = true)
        {
            if (EntryCache.TryGetValue(repositoryType, out Type value)) return value;

            Type baseType = repositoryType;

            while (baseType.IsQueryable())
            {
                if (baseType.IsGenericType)
                {
                    foreach (Type type in baseType.GetGenericArguments())
                    {
                        if (type.IsValueType || !type.IsClass || type == typeof(string)) continue;

                        return EntryCache.GetOrAdd(repositoryType, type);
                    }
                }

                baseType = baseType.BaseType;
            };

            if (baseType is null || baseType.IsValueType || !baseType.IsClass || baseType == typeof(string))
            {
                if (throwsError)
                {
                    throw new TypeAccessInvalidException($"访问类型({repositoryType.Namespace}.{repositoryType.Name})无效!");
                }

                return null;
            }

            if (typeof(IEnumerable).IsAssignableFrom(baseType))
            {
                throw new TypeAccessInvalidException($"访问类型({repositoryType.Namespace}.{repositoryType.Name})的泛型参数或基类({baseType.Namespace}.{baseType.Name})是迭代类型,不被支持!");
            }

            return EntryCache.GetOrAdd(repositoryType, baseType);
        }

        protected void WriteAppendAtFix(Action coreFactory, Action beforeFactory = null, Action<int> afterFactory = null)
        {
            var index = SQLWriter.Length;
            var length = SQLWriter.Length;
            var appendAt = SQLWriter.AppendAt;

            if (appendAt > -1)
            {
                index -= (index - appendAt);
            }

            beforeFactory?.Invoke();

            SQLWriter.AppendAt = index;

            coreFactory.Invoke();

            afterFactory?.Invoke(index);

            if (appendAt > -1)
            {
                appendAt += SQLWriter.Length - length;
            }
            SQLWriter.AppendAt = appendAt;
        }

        #region 重写

        protected virtual IEnumerable<MemberInfo> FilterMembers(IEnumerable<MemberInfo> members) => members;

        protected override Expression VisitNew(NewExpression node)
        {
            Type type = node.Type;

            if (type.IsValueType || type == typeof(string))
            {
                if (node.Arguments.Count > 1)
                    throw new ExpressionNotSupportedException();

                if (node.Arguments.Count == 0)
                    SQLWriter.Parameter(node.GetValueFromExpression());
                else
                    base.Visit(node.Arguments[0]);

                return node;
            }

            var members = FilterMembers(node.Members);

            if (members.Count() == 0)
                throw new DException("未指定查询字段!");

            members.ForEach((member, index) =>
            {
                if (index > 0)
                {
                    SQLWriter.Delimiter();
                }

                Visit(node.Arguments[index]);
                SQLWriter.As(member.Name);
            });

            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.IsBoolean())
                throw new ExpressionNotSupportedException("禁止使用布尔常量作为条件语句或结果!");

            var value = node.Value as ConstantExpression;

            SQLWriter.Parameter((value ?? node).GetValueFromExpression());

            return node;
        }

        protected virtual IEnumerable<IVisitter> GetCustomVisitters() => Empty;

        /// <summary>
        /// 格式化函数
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns></returns>
        protected virtual Expression VisitFormatterMethodCall(MethodCallExpression node)
        {
            foreach (var item in GetCustomVisitters() ?? Empty)
            {
                if (item.CanResolve(node)) return item.Visit(this, SQLWriter, node);
            }

            foreach (var item in settings.Visitters ?? Empty)
            {
                if (item.CanResolve(node)) return item.Visit(this, SQLWriter, node);
            }

            var type = node.Method.ReturnType;

            if (type.IsValueType || type == typeof(string))
            {
                SQLWriter.Parameter(TryThrow(() => node.GetValueFromExpression()));

                return node;
            }

            type = node.Method.DeclaringType;

            throw new ExpressionNotSupportedException($"命名空间({type.Namespace})下的类({type.Name})中的方法({node.Method.Name})不被支持!");
        }

        /// <summary>
        /// System.Linq.Enumerable 的函数
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected virtual Expression VisitEnumerableMethodCall(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case MethodCall.Any:

                    return AnyMethod(node);

                case MethodCall.Contains:

                    return InMethod(node);

                case MethodCall.Single:
                case MethodCall.SingleOrDefault:

                case MethodCall.First:
                case MethodCall.FirstOrDefault:

                case MethodCall.Last:
                case MethodCall.LastOrDefault:

                case MethodCall.Max:
                case MethodCall.Min:
                case MethodCall.Sum:
                case MethodCall.Count:
                case MethodCall.Average:
                case MethodCall.LongCount:
                case MethodCall.ElementAt:
                case MethodCall.ElementAtOrDefault:

                    SQLWriter.Parameter(TryThrow(() => node.GetValueFromExpression()));

                    return node;
                default:
                    return VisitFormatterMethodCall(node);
            }
        }

        /// <summary>
        /// System.Linq.Enumerable 的函数
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected virtual Expression VisitIEnumerableMethodCall(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case MethodCall.Any:

                    return AnyMethod(node);

                case MethodCall.Contains:

                    return InMethod(node);

                case MethodCall.Single:
                case MethodCall.SingleOrDefault:

                case MethodCall.First:
                case MethodCall.FirstOrDefault:

                case MethodCall.Last:
                case MethodCall.LastOrDefault:

                case MethodCall.Max:
                case MethodCall.Min:
                case MethodCall.Sum:
                case MethodCall.Count:
                case MethodCall.Average:
                case MethodCall.LongCount:

                    SQLWriter.Parameter(TryThrow(() => node.GetValueFromExpression()));

                    return node;
                default:
                    return VisitFormatterMethodCall(node);
            }
        }

        /// <summary>
        /// System.String 的函数
        /// </summary>
        /// <param name="node">表达式</param>
        /// <returns></returns>
        protected virtual Expression VisitStringMethodCall(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case MethodCall.Contains:
                case MethodCall.EndsWith:
                case MethodCall.StartsWith:
                    return VisitLike(node);
                case MethodCall.IsNullOrEmpty:
                    return IsNullOrEmptyMethod(node);
                case MethodCall.Replace:
                    SQLWriter.Write(node.Method.Name);
                    SQLWriter.OpenBrace();
                    base.Visit(node.Object);
                    foreach (Expression item in node.Arguments)
                    {
                        SQLWriter.Delimiter();
                        base.Visit(item);
                    }
                    SQLWriter.CloseBrace();
                    return node;
                case MethodCall.Substring:
                    SQLWriter.SubstringMethod();
                    SQLWriter.OpenBrace();
                    base.Visit(node.Object);
                    SQLWriter.Delimiter();
                    base.Visit(node.Arguments[0]);
                    SQLWriter.Write(" + 1");
                    SQLWriter.Delimiter();
                    if (node.Arguments.Count > 1)
                    {
                        base.Visit(node.Arguments[1]);
                    }
                    else
                    {
                        SQLWriter.LengthMethod();
                        SQLWriter.OpenBrace();
                        base.Visit(node.Object);
                        SQLWriter.CloseBrace();
                        SQLWriter.Write(" - ");
                        base.Visit(node.Arguments[0]);
                    }
                    SQLWriter.CloseBrace();
                    return node;
                case MethodCall.ToUpper:
                case MethodCall.ToLower:
                    SQLWriter.Write(node.Method.Name.Substring(2));
                    SQLWriter.OpenBrace();
                    base.Visit(node.Object);
                    SQLWriter.CloseBrace();
                    return node;
                case MethodCall.Trim:
                case MethodCall.TrimEnd:
                case MethodCall.TrimStart:
                    return TrimMethod(node);
                case MethodCall.IndexOf:
                    SQLWriter.IndexOfMethod();
                    SQLWriter.OpenBrace();
                    base.Visit(settings.IndexOfSwapPlaces ? node.Arguments[0] : node.Object);
                    SQLWriter.Delimiter();
                    base.Visit(settings.IndexOfSwapPlaces ? node.Object : node.Arguments[0]);
                    if (node.Arguments.Count > 1)
                    {
                        SQLWriter.Delimiter();
                        base.Visit(node.Arguments[1]);
                        SQLWriter.Write(" + 1");
                    }
                    SQLWriter.CloseBrace();
                    return node;
                default:
                    return VisitFormatterMethodCall(node);
            }
        }

        /// <summary>
        /// 函数调用
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns></returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var declaringType = node.Method.DeclaringType;

            if (declaringType == typeof(Enumerable))
            {
                return VisitEnumerableMethodCall(node);
            }

            if (declaringType == typeof(string))
            {
                return VisitStringMethodCall(node);
            }

            if (typeof(IEnumerable).IsAssignableFrom(declaringType))
            {
                return VisitIEnumerableMethodCall(node);
            }

            return VisitFormatterMethodCall(node);
        }

        /// <summary>
        /// 表达式
        /// </summary>
        /// <typeparam name="T">节点</typeparam>
        /// <param name="node">元素</param>
        /// <returns></returns>
        protected sealed override Expression VisitLambda<T>(Expression<T> node)
        {
            if (node.Parameters.Count > 1)
                throw new ExpressionNotSupportedException("不支持多个参数!");

            return VisitLambda(node, (type, name) =>
             {
                 _PrefixCache.GetOrAdd(type, name);
             });
        }

        protected virtual Expression VisitLambda<T>(Expression<T> node, Action<Type, string> addPrefixCache)
        {
            var parameter = node.Parameters[0];

            addPrefixCache.Invoke(parameter.Type, parameter.Name);

            base.Visit(node.Body);

            return node;
        }

        protected virtual void VisitBuilder(Expression node)
        {
            using (var builder = CreateBuilder())
            {
                builder.Evaluate(node);

                SQLWriter.Write(builder.ToSQL());
            }
        }

        private void VisitEvaluate(Expression node)
        {
            if (node.NodeType == ExpressionType.Call && node is MethodCallExpression methodCall)
            {
                Type declaringType = methodCall.Method.DeclaringType;

                if (declaringType == typeof(Queryable) || declaringType == typeof(QueryableExtentions))
                {
                    if (methodCall.Method.Name == MethodCall.Contains)
                    {
                        base.Visit(methodCall.Arguments[1]);
                        SQLWriter.Contains();
                        node = methodCall.Arguments[0];
                    }

                    SQLWriter.OpenBrace();

                    VisitBuilder(node);

                    SQLWriter.CloseBrace();

                    return;
                }
            }

            base.Visit(node);
        }

        /// <summary>
        /// Invoke
        /// </summary>
        /// <param name="node">参数</param>
        /// <returns></returns>
        protected override Expression VisitInvocation(InvocationExpression node)
        {
            return base.Visit(node.Expression);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            VisitEvaluate(node.Expression);

            return node;
        }

        protected virtual IEnumerable<MemberBinding> FilterMemberBindings(IEnumerable<MemberBinding> bindings) => bindings;

        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            var bindings = FilterMemberBindings(node.Bindings);

            if (bindings.Count() == 0)
            {
                throw new DException("未指定查询字段!");
            }

            bindings.ForEach((item, index) =>
            {
                if (index > 0)
                {
                    SQLWriter.Delimiter();
                }
                base.VisitMemberBinding(item);
            });

            return node;
        }

        private Expression VisitMemberVariable(MemberExpression node)
        {
            var value = node.GetValueFromExpression();

            if (value == null)
            {
                if (isIgnoreNullable && node.IsNullable())
                    return node;
            }
            else if (value is IQueryable queryable)
            {
                base.Visit(queryable.Expression);

                return node;
            }
            else if (value is IExecuteable executeable)
            {
                base.Visit(executeable.Expression);

                return node;
            }

            if (buildWhereBoth && value.Equals(!SQLWriter.Not))
                return node;

            SQLWriter.Parameter(node.Member.Name, value);

            if (buildWhereBoth)
            {
                SQLWriter.BooleanFalse();
            }
            return node;
        }

        protected virtual Expression VisitMemberParameter(MemberExpression node, Action factoryPrefix)
        {
            string name = node.Member.Name;

            var regions = MakeTableRegions(node.Expression.Type);

            if (!regions.ReadOrWrites.TryGetValue(name, out string value))
                throw new ExpressionNotSupportedException($"{name}不可读写!");

            factoryPrefix.Invoke();

            SQLWriter.Name(value);

            if (buildWhereBoth && node.IsBoolean())
            {
                SQLWriter.BooleanTrue();
            }

            return node;
        }

        private Expression VisitMemberSimple(MemberExpression node)
        {
            if (node.IsVariable())
                return VisitMemberVariable(node);

            if (node.IsLength())
            {
                SQLWriter.LengthMethod();
                SQLWriter.OpenBrace();
                var me = base.VisitMember(node);
                SQLWriter.CloseBrace();
                return me;
            }


            if (node.Expression?.NodeType == ExpressionType.Parameter)
                return VisitMemberParameter(node, () => base.Visit(node.Expression));


            if (node.Expression is MemberExpression member)
            {
                MemberExpression memberExpression = member;

                while (memberExpression?.Expression?.NodeType == ExpressionType.MemberAccess)
                {
                    memberExpression = memberExpression.Expression as MemberExpression;
                }

                if (memberExpression?.Expression?.NodeType == ExpressionType.Parameter)
                    return VisitMemberParameter(node, () => SQLWriter.WritePrefix(GetOrAddTablePrefix(member.Type, member.Member.Name)));
            }

            var value = node.GetValueFromExpression();

            if (value != null && !node.Type.IsValueType && value is IQueryable queryable)
            {
                base.Visit(queryable.Expression);
            }
            else
            {
                SQLWriter.Parameter("__variable_" + node.Member.Name.ToLower(), value);
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == null)
                return VisitMemberSimple(node);

            if ((node.Expression is MemberExpression member) && member.IsNullable())
            {
                if (node.IsValue())
                {
                    VisitMemberSimple(member);
                    return node;
                }
                if (node.IsHasValue())
                {
                    VisitMemberSimple(member);

                    WrapNot(SQLWriter.IsNull);

                    return node;
                }
            }

            return VisitMemberSimple(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            SQLWriter.WritePrefix(GetOrAddTablePrefix(node.Type, node.Name));
            return node;
        }

        private Expression VisitConditionBinary(BinaryExpression node, bool isAndLike)
        {
            int indexBefore = SQLWriter.Length;

            VisitEvaluate(node.Left);

            int indexStep = SQLWriter.Length;

            VisitEvaluate(node.Right);

            if (indexStep > indexBefore && SQLWriter.Length > indexStep)
            {
                int index = SQLWriter.Length;

                int length = SQLWriter.Length;

                int appendAt = SQLWriter.AppendAt;

                if (appendAt > -1)
                {
                    indexStep -= (index - appendAt);

                    indexBefore -= (index - appendAt);
                }

                SQLWriter.AppendAt = indexBefore;

                SQLWriter.OpenBrace();

                SQLWriter.AppendAt = indexStep + 1;

                if (isAndLike)
                {
                    SQLWriter.WriteAnd();
                }
                else
                {
                    SQLWriter.WriteOr();
                }

                if (appendAt > -1)
                {
                    appendAt += SQLWriter.Length - length;
                }

                SQLWriter.AppendAt = appendAt;

                SQLWriter.CloseBrace();
            }

            return node;
        }

        private Expression VisitOperationBinary(BinaryExpression node, ExpressionType expressionType)
        {
            int indexBefore = SQLWriter.Length;

            VisitEvaluate(node.Right);

            int indexStep = SQLWriter.Length;

            if (indexStep == indexBefore)
                return node;

            int index = SQLWriter.Length;

            int length = SQLWriter.Length;

            int appendAt = SQLWriter.AppendAt;

            if (appendAt > -1)
            {
                indexBefore -= index - appendAt;
            }

            SQLWriter.AppendAt = indexBefore;

            SQLWriter.OpenBrace();

            int indexNext = SQLWriter.Length;

            VisitEvaluate(node.Left);

            if (SQLWriter.Length == indexNext)
            {
                SQLWriter.Remove(indexBefore, indexNext - indexBefore);

                SQLWriter.AppendAt = appendAt;

                return node;
            }

            SQLWriter.Write(expressionType);

            if (appendAt > -1)
            {
                appendAt += SQLWriter.Length - length;
            }

            SQLWriter.AppendAt = appendAt;

            SQLWriter.CloseBrace();

            return node;
        }

        private Expression BuildBinaryWarp(Func<Expression> factory, bool buildBoth = false)
        {
            bool both = buildWhereBoth;

            buildWhereBoth = buildBoth;

            var me = factory();

            buildWhereBoth = both;

            return me;
        }

        /// <summary>
        /// 条件
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns></returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            Expression left = node.Left;
            Expression right = node.Right;

            if (node.NodeType == ExpressionType.Coalesce)
            {
                if (left.IsVariable() && IsStaticVariable(right))
                {
                    string name = node.GetPropertyMemberNameFromExpression();

                    SQLWriter.Parameter(name, node.GetValueFromExpression());

                    return node;
                }

                SQLWriter.OpenBrace();

                SQLWriter.Write("CASE WHEN ");

                VisitEvaluate(left);

                SQLWriter.IsNull();
                SQLWriter.Write(" THEN ");

                VisitEvaluate(right);

                SQLWriter.Write(" ELSE ");

                VisitEvaluate(left);

                SQLWriter.Write(" END");

                SQLWriter.CloseBrace();
                return node;
            }

            var nodeType = node.NodeType;

            bool isAndLike = nodeType == ExpressionType.AndAlso;
            bool isOrLike = nodeType == ExpressionType.OrElse;

            bool isAndOrLike = isAndLike || isOrLike;

            if (isAndOrLike && IsStaticVariable(left))
            {
                if (SQLWriter.Not)
                {
                    isAndLike ^= isOrLike;
                    isOrLike ^= isAndLike;
                    isAndLike ^= isOrLike;
                }

                var value = left.GetValueFromExpression();
                if (value == null || value.Equals(false))
                {
                    if (isOrLike)
                    {
                        VisitEvaluate(right);
                    }
                    return node;
                }

                if (isAndLike)
                {
                    VisitEvaluate(right);
                }

                return node;
            }


            if (isAndOrLike) return BuildBinaryWarp(() => VisitConditionBinary(node, isAndLike), true);

            if (left.Type.IsValueType || right.Type.IsValueType)
            {
                return BuildBinaryWarp(() =>
                {
                    isIgnoreNullable = true;
                    var me = VisitOperationBinary(node, nodeType);
                    isIgnoreNullable = false;
                    return me;
                });
            }

            return BuildBinaryWarp(() =>
            {
                SQLWriter.OpenBrace();

                VisitEvaluate(left);

                SQLWriter.Write(nodeType);

                VisitEvaluate(right);

                SQLWriter.CloseBrace();

                return node;
            });
        }

        /// <summary>
        /// 三目运算
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns></returns>
        protected override Expression VisitConditional(ConditionalExpression node)
        {
            if (IsStaticVariable(node.Test, true))
            {
                var value = node.Test.GetValueFromExpression();
                if (Equals(value, true))
                {
                    VisitEvaluate(node.IfTrue);
                }
                else
                {
                    VisitEvaluate(node.IfFalse);
                }

                return node;
            }

            SQLWriter.OpenBrace();
            SQLWriter.Write("CASE WHEN ");
            VisitEvaluate(node.Test);
            SQLWriter.Write(" THEN ");
            VisitEvaluate(node.IfTrue);
            SQLWriter.Write(" ELSE ");
            VisitEvaluate(node.IfFalse);
            SQLWriter.Write(" END");
            SQLWriter.CloseBrace();

            return node;
        }

        /// <summary>
        /// 包装表达式
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns></returns>
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                return WrapNot(() =>
                {
                    return base.VisitUnary(node);
                });
            }
            return base.VisitUnary(node);
        }

        protected override Expression VisitSwitch(SwitchExpression node)
        {
            SQLWriter.Write("CASE ");
            base.Visit(node.SwitchValue);
            node.Cases.ForEach(item =>
            {
                VisitSwitchCase(item);
            });
            SQLWriter.Write(" ELSE ");
            base.Visit(node.DefaultBody);
            SQLWriter.Write(" END");
            return node;
        }

        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            SQLWriter.Write(" WHEN ");

            node.TestValues.ForEach((item, index) =>
            {
                if (index > 0) SQLWriter.WriteOr();

                base.Visit(item);
            });

            SQLWriter.Write(" THEN ");

            base.Visit(node.Body);

            return node;
        }

        #endregion

        #region 不支持的重写
        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override Expression VisitBlock(BlockExpression node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override Expression VisitGoto(GotoExpression node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override Expression VisitLabel(LabelExpression node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override Expression VisitLoop(LoopExpression node)
        {
            throw new ExpressionNotSupportedException();
        }

        protected override Expression VisitTry(TryExpression node)
        {
            throw new ExpressionNotSupportedException();
        }
        #endregion

        public Dictionary<string, object> Parameters => SQLWriter?.Parameters;

        public string ToSQL() => SQLWriter?.ToSQL();

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)。
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
