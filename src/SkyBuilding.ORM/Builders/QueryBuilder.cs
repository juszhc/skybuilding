using SkyBuilding.ORM.Exceptions;
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
    /// 构造器
    /// </summary>
    public class QueryBuilder : Builder, IBuilder, IDisposable
    {
        private bool buildSelect = true;

        private bool buildJoin = false;

        private bool buildFrom = true;

        private bool buildExists = false;

        private bool buildCast = false;

        private bool useCast = false;

        private bool isDistinct = false;

        private bool isSingleField = false;//只构建一个字段

        private bool isAggregation = false; // 聚合函数

        private bool isNoParameterCount = false;//是否为Count函数

        private bool isContainsOrderBy = false; //包含OrderBy

        private bool isOrderByReverse = false; //倒序

        private int _MethodLevel = 0;

        private SmartSwitch _whereSwitch = null;

        private SmartSwitch _orderBySwitch = null;

        private SmartSwitch _fromSwitch = null;

        private List<string> _CastList = null;

        private ConcurrentDictionary<Type, Type> _TypeCache;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="settings">SQL矫正设置</param>
        public QueryBuilder(ISQLCorrectSettings settings) : base(settings) { }

        /// <summary>
        /// 表达式测评
        /// </summary>
        /// <param name="node">表达式</param>
        public override void Evaluate(Expression node)
        {
            _whereSwitch = new SmartSwitch(SQLWriter.Where, SQLWriter.WriteAnd);

            _orderBySwitch = new SmartSwitch(SQLWriter.OrderBy, SQLWriter.Delimiter);

            _fromSwitch = new SmartSwitch(SQLWriter.From, SQLWriter.Join);

            _TypeCache = new ConcurrentDictionary<Type, Type>();

            base.Evaluate(node);
        }

        private bool InCastList(string name) => _CastList is null || _CastList.Contains(name.ToLower());

        private void UnWrap(Action action) => _fromSwitch.UnWrap(() => _whereSwitch.UnWrap(() => _orderBySwitch.UnWrap(action)));

        private Expression VisitExists(MethodCallExpression node, bool isNotWrap = false)
        {
            SQLWriter.Exists();

            SQLWriter.OpenBrace();

            bool join = buildJoin;

            buildJoin = false;
            buildSelect = buildFrom = true;

            if (isNotWrap || node.Arguments.Count == 1)
            {
                UnWrap(() =>
                {
                    buildExists = true;

                    base.Visit(node.Arguments[0]);

                    buildExists = false;

                    if (node.Arguments.Count > 1)
                    {
                        _whereSwitch.Execute();

                        WrapNot(() =>
                        {
                            base.Visit(node.Arguments[1]);
                        });
                    }
                });
            }
            else
            {
                UnWrap(() =>
                {
                    int length = 0;

                    WriteAppendAtFix(() =>
                    {
                        buildExists = true;

                        base.Visit(node.Arguments[0]);

                        buildExists = false;

                    }, () =>
                    {
                        base.Visit(node.Arguments[1]);

                        length = SQLWriter.Length;

                    }, index =>
                    {
                        SQLWriter.AppendAt = SQLWriter.Length - (length - index);

                        _whereSwitch.Execute();
                    });
                });
            }

            SQLWriter.CloseBrace();

            buildJoin = join;

            return node;
        }

        private Expression VisitMethodAll(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable))
            {
                VisitExists(node);

                SQLWriter.WriteAnd();

                return WrapNot(() =>
                {
                    _fromSwitch.UnWrap(() => _whereSwitch.UnWrap(() => _orderBySwitch.UnWrap(() =>
                    {
                        VisitExists(node, true);
                    })));

                    return node;
                });
            }
            throw new ExpressionNotSupportedException($"仅支持“System.Linq.Queryable”中的{node.Method.Name}函数!");
        }

        private void BuildSingleField(string methodName, string prefix, ITableRegions regions)
        {
            IEnumerable<KeyValuePair<string, string>> names = regions.ReadOrWrites;

            if (buildCast)
            {
                names = names.Where(kv => InCastList(kv.Key));
            }

            if (names.Count() == 0)
            {
                throw new DException("未指定查询字段!");
            }

            names.ForEach((kv, index) =>
            {
                if (index > 0)
                {
                    SQLWriter.Delimiter();
                }

                SQLWriter.Write(methodName);
                SQLWriter.OpenBrace();

                SQLWriter.Name(prefix, kv.Value);

                SQLWriter.CloseBrace();
                SQLWriter.As(kv.Key);
            });
        }

        private Expression BuildSingleOneArgField(string name, Expression node)
        {
            var regions = MakeTableRegions(node.Type);

            string prefix = GetOrAddTablePrefix(regions.TableType);

            BuildSingleField(name, prefix, regions);

            return node;
        }

        private Expression SingleFieldTwoArgOrCountMethod(string name, MethodCallExpression node)
        {
            WriteAppendAtFix(() =>
            {
                SQLWriter.Select();

                SQLWriter.Write(name);
                SQLWriter.OpenBrace();

                if (node.Arguments.Count > 1)
                {
                    base.Visit(node.Arguments[1]);
                }
                else
                {
                    SQLWriter.Write("1");
                }

                SQLWriter.CloseBrace();

                if (SQLWriter.IsUnion)
                {
                    SQLWriter.From();
                    SQLWriter.OpenBrace();
                }

            }, () => base.Visit(node.Arguments[0]));

            if (SQLWriter.IsUnion)
            {
                SQLWriter.CloseBrace();
                SQLWriter.WhiteSpace();

                SQLWriter.TableName("CTE_UNION");
            }

            return node;
        }

        private Expression SingleFieldMethod(MethodCallExpression node)
        {
            string name = node.Method.Name;

            if (name == MethodCall.Average)
                name = "Avg";
            else if (name == MethodCall.LongCount)
                name = MethodCall.Count;

            isAggregation = true;

            if (node.Arguments.Count > 1 || !(isNoParameterCount = name == MethodCall.Count))
            {
                _MethodLevel += 1;
            }

            if (buildSelect)
            {
                buildSelect = false;

                if (node.Arguments.Count > 1 || name == MethodCall.Count)
                {
                    return SingleFieldTwoArgOrCountMethod(name, node);
                }

                return SingleFieldOnlyArgMethod(name, node.Arguments[0]);
            }

            if (node.Arguments.Count > 1 || name == MethodCall.Count)
            {
                SQLWriter.Write(name);
                SQLWriter.OpenBrace();
                if (node.Arguments.Count > 1)
                {
                    base.Visit(node.Arguments[1]);
                }
                else
                {
                    SQLWriter.Write("1");
                }
                SQLWriter.CloseBrace();
                return node;
            }

            return BuildSingleOneArgField(name, node);
        }

        private Expression JoinMethod(MethodCallExpression node)
        {
            WriteAppendAtFix(() =>
            {
                buildJoin = true;

                buildFrom = true;

                base.Visit(node.Arguments[0]);

                buildFrom = true;

                base.Visit(node.Arguments[1]);

                buildFrom = false;
            }, () =>
            {
                SQLWriter.Write(" ON ");

                buildFrom = false;

                base.Visit(node.Arguments[2]);

                SQLWriter.Equal();

                base.Visit(node.Arguments[3]);
            });

            return node;
        }

        private void BuildColumns(ITableRegions regions)
        {
            string prefix = GetOrAddTablePrefix(regions.TableType);

            if (buildExists && regions.ReadOrWrites.Count > 0)
            {
                regions.ReadOrWrites.ForEach((kv, index) =>
                {
                    if (index > 0) SQLWriter.Delimiter();

                    SQLWriter.Name(prefix, kv.Value);

                    if (kv.Key.ToLower() != kv.Value.ToLower())
                    {
                        SQLWriter.As(kv.Key);
                    }
                });
                return;
            }

            IEnumerable<KeyValuePair<string, string>> names = regions.ReadOrWrites;

            if (buildCast)
            {
                names = names.Where(x => InCastList(x.Key));
            }

            if (names.Count() == 0)
            {
                throw new DException("未指定查询字段!");
            }

            names.ForEach((kv, index) =>
            {
                if (index > 0) SQLWriter.Delimiter();

                SQLWriter.Name(prefix, kv.Value);

                if (kv.Key.ToLower() != kv.Value.ToLower())
                {
                    SQLWriter.As(kv.Key);
                }
            });
        }

        private void MakeFrom(ITableRegions regions)
        {
            buildFrom = false;

            _fromSwitch.Execute();

            WriteTable(regions);
        }

        private void MakeSelectFrom(Type type)
        {
            var regions = MakeTableRegions(type);

            if (buildSelect)
            {
                buildSelect = false;

                SQLWriter.Select();

                if (isDistinct) SQLWriter.Distinct();

                BuildColumns(regions);
            }

            if (buildFrom)
            {
                MakeFrom(regions);
            }
        }

        private Expression SingleFieldOnlyArgMethod(string name, Expression node)
        {
            WriteAppendAtFix(() =>
            {
                SQLWriter.Select();

                BuildSingleOneArgField(name, node);

                if (SQLWriter.IsUnion)
                {
                    _fromSwitch.Execute();
                    SQLWriter.OpenBrace();
                    SQLWriter.AppendAt = -1;
                    SQLWriter.CloseBrace();
                    SQLWriter.WhiteSpace();
                    SQLWriter.TableName("CTE_UNION");
                }
            }, () => base.Visit(node));

            return node;
        }

        private Expression MakeWhereNode(MethodCallExpression node)
        {
            bool whereIsNotEmpty = false;

            base.Visit(node.Arguments[0]);

            WriteAppendAtFix(() =>
            {
                if (whereIsNotEmpty)
                {
                    _whereSwitch.Execute();
                }
            }, () =>
            {
                int length = SQLWriter.Length;

                BuildWhere = true;
                base.Visit(node.Arguments[1]);
                BuildWhere = false;

                whereIsNotEmpty = SQLWriter.Length > length;
            });

            return node;
        }

        private Expression MakeWhere(MethodCallExpression node)
        {
            if (buildFrom || buildSelect)
            {
                bool select = buildSelect;
                bool from = buildFrom;

                buildSelect = buildFrom = false;

                WriteAppendAtFix(() =>
                {
                    buildSelect = select;
                    buildFrom = from && !buildJoin;//Join函数

                    if (buildSelect || buildFrom)
                    {
                        MakeSelectFrom(node.Type);
                    }

                }, () => MakeWhereNode(node));

                return node;
            }

            return MakeWhereNode(node);
        }

        #region 重写

        protected override Type GetRealType(Type type)
        {
            if (_TypeCache.TryGetValue(type, out Type realType))
                return realType;

            return base.GetRealType(type);
        }
        protected override IEnumerable<MemberInfo> FilterMembers(IEnumerable<MemberInfo> members) => members.Where(x => InCastList(x.Name));

        protected override Expression VisitMemberParameter(MemberExpression node, Action factoryPrefix)
        {
            string name = node.Member.Name;

            if (isSingleField && !InCastList(name))
                throw new DException("未指定查询字段!");

            return base.VisitMemberParameter(node, factoryPrefix);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            var me = base.VisitMemberAssignment(node);

            SQLWriter.As(node.Member.Name);

            return me;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type.IsQueryable())
            {
                if (buildSelect || buildFrom)
                {
                    MakeSelectFrom(node.Type);
                }
                return node;
            }

            return base.VisitConstant(node);
        }

        /// <summary>
        ///  System.Linq.Queryable 的函数
        /// </summary>
        /// <param name="node">表达式</param>
        /// <returns></returns>
        protected virtual Expression VisitQueryableMethodCall(MethodCallExpression node)
        {
            //? 函数名称
            string name = node.Method.Name;

            if (node.Arguments.Count > 1 ?
                !(name == MethodCall.Take || name == MethodCall.Skip || name == MethodCall.TakeLast || name == MethodCall.SkipLast) :
                (name == MethodCall.Sum || name == MethodCall.Max || name == MethodCall.Min || name == MethodCall.Average)
            )
            {
                _MethodLevel += 1;
            }

            switch (name)
            {
                case MethodCall.Any:

                    if (Parent?.BuildWhere ?? BuildWhere) return VisitExists(node);

                    if (buildSelect)
                    {
                        SQLWriter.Select();
                    }

                    SQLWriter.Write("CASE WHEN ");

                    VisitExists(node);

                    SQLWriter.Write(" THEN ");
                    SQLWriter.Parameter("__variable_true", true);
                    SQLWriter.Write(" ELSE ");
                    SQLWriter.Parameter("__variable_false", false);
                    SQLWriter.Write(" END");

                    return node;
                case MethodCall.All:

                    if (Parent?.BuildWhere ?? BuildWhere)
                    {
                        SQLWriter.OpenBrace();

                        VisitMethodAll(node);

                        SQLWriter.CloseBrace();
                    }
                    else
                    {
                        if (buildSelect)
                        {
                            SQLWriter.Select();
                        }

                        SQLWriter.Write("CASE WHEN ");

                        VisitMethodAll(node);

                        SQLWriter.Write(" THEN ");
                        SQLWriter.Parameter("__variable_true", true);
                        SQLWriter.Write(" ELSE ");
                        SQLWriter.Parameter("__variable_false", false);
                        SQLWriter.Write(" END");
                    }
                    return node;
                case MethodCall.ElementAt:
                case MethodCall.ElementAtOrDefault:

                    base.Visit(node.Arguments[0]);

                    int index = (int)node.Arguments[1].GetValueFromExpression();

                    if (index < 0)
                        throw new IndexOutOfRangeException();

                    if (SQLWriter.Take > 0 && index < SQLWriter.Take)
                        throw new IndexOutOfRangeException();

                    SQLWriter.Take = 1;

                    SQLWriter.Skip += index;

                    return node;
                case MethodCall.Take:
                case MethodCall.TakeLast:

                    if (isAggregation)
                        throw new ExpressionNotSupportedException($"使用聚合函数时，禁止使用分页函数({name})!");

                    if (name == MethodCall.TakeLast)
                        isOrderByReverse ^= true;

                    base.Visit(node.Arguments[0]);

                    if (!isContainsOrderBy && name == MethodCall.TakeLast)
                        throw new ExpressionNotSupportedException($"使用函数({name})时，必须使用排序函数(OrderBy/OrderByDescending)!");

                    int take = (int)node.Arguments[1].GetValueFromExpression();

                    if (take < 0)
                        throw new ArgumentOutOfRangeException($"使用{name}函数,参数值不能小于零!");

                    if (SQLWriter.Take > 0 && take < SQLWriter.Take)
                        throw new IndexOutOfRangeException();

                    if (SQLWriter.Take == 0)
                        SQLWriter.Take = take;

                    return node;
                case MethodCall.Single:
                case MethodCall.SingleOrDefault:
                case MethodCall.First:
                case MethodCall.FirstOrDefault:

                    // TOP(1)
                    SQLWriter.Take = 1;

                    if (node.Arguments.Count > 1)
                    {
                        MakeWhere(node);
                    }
                    else
                    {
                        base.Visit(node.Arguments[0]);
                    }

                    return node;
                case MethodCall.Last:
                case MethodCall.LastOrDefault:

                    // TOP(..)
                    SQLWriter.Take = 1;

                    isOrderByReverse ^= true;

                    if (node.Arguments.Count > 1)
                    {
                        MakeWhere(node);
                    }
                    else
                    {
                        base.Visit(node.Arguments[0]);
                    }

                    if (!isContainsOrderBy)
                        throw new ExpressionNotSupportedException($"使用函数({name})时，必须使用排序函数(OrderBy/OrderByDescending)!");

                    return node;
                case MethodCall.Skip:
                case MethodCall.SkipLast:

                    if (isAggregation)
                        throw new ExpressionNotSupportedException($"使用聚合函数时，禁止使用分页函数({name})!");

                    if (name == MethodCall.SkipLast)
                        isOrderByReverse ^= true;

                    base.Visit(node.Arguments[0]);

                    if (!isContainsOrderBy && name == MethodCall.SkipLast)
                        throw new ExpressionNotSupportedException($"使用函数({name})时，必须使用排序函数(OrderBy/OrderByDescending)!");

                    int skip = (int)node.Arguments[1].GetValueFromExpression();

                    if (skip < 0)
                        throw new ArgumentOutOfRangeException($"使用({name})函数,参数值不能小于零!");

                    SQLWriter.Skip += skip;

                    return node;
                case MethodCall.Distinct:

                    isDistinct = true;

                    return base.Visit(node.Arguments[0]);

                case MethodCall.Reverse:

                    isOrderByReverse ^= true;

                    base.Visit(node.Arguments[0]);

                    if (!isContainsOrderBy)
                        throw new ExpressionNotSupportedException($"使用函数({name})时，必须使用排序函数(OrderBy/OrderByDescending)!");

                    return node;
                case MethodCall.OrderBy:
                case MethodCall.ThenBy:
                case MethodCall.OrderByDescending:
                case MethodCall.ThenByDescending:

                    isContainsOrderBy = true;

                    base.Visit(node.Arguments[0]);

                    if (isAggregation) return node;

                    SQLWriter.IsOrderBy = true;

                    _orderBySwitch.Execute();

                    base.Visit(node.Arguments[1]);

                    if (isOrderByReverse ^ name.EndsWith("Descending"))
                    {
                        SQLWriter.WriteDesc();
                    }
                    SQLWriter.IsOrderBy = false;
                    return node;
                case MethodCall.Where:
                case MethodCall.TakeWhile:
                    return MakeWhere(node);
                case MethodCall.SkipWhile:
                    return WrapNot(() =>
                    {
                        return MakeWhere(node);
                    });
                case MethodCall.Select:

                    if (_MethodLevel > 1)
                        throw new ExpressionNotSupportedException($"请将函数({name})置于查询最后一个包含入参的函数之后!");

                    if (isNoParameterCount)
                        return base.Visit(node.Arguments[0]);

                    buildSelect = false;

                    SQLWriter.Select();

                    buildFrom = false;

                    WriteAppendAtFix(() =>
                    {
                        if (isDistinct) SQLWriter.Distinct();

                        buildFrom = true;
                        base.Visit(node.Arguments[1]);
                        buildFrom = false;

                    }, () => base.Visit(node.Arguments[0]));

                    return node;
                case MethodCall.Max:
                case MethodCall.Min:
                case MethodCall.Sum:
                case MethodCall.Count:
                case MethodCall.Average:
                case MethodCall.LongCount:
                    return SingleFieldMethod(node);
                case MethodCall.Join:
                    return JoinMethod(node);
                case MethodCall.Union:
                case MethodCall.Concat:
                case MethodCall.Intersect:

                    buildSelect = false;

                    SQLWriter.IsUnion = false;

                    VisitBuilder(node.Arguments[0]);

                    if (name == MethodCall.Intersect)
                    {
                        SQLWriter.Write(" INTERSECT ");
                    }
                    else
                    {
                        SQLWriter.Write(MethodCall.Union == name ? " UNION " : " UNION ALL ");
                    }

                    VisitBuilder(node.Arguments[1]);

                    SQLWriter.IsUnion = true;

                    return node;

                case MethodCall.Cast:

                    Type type = node.Type.GetGenericArguments().First();

                    if (type.IsValueType || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type))
                    {
                        throw new TypeAccessInvalidException($"{name}函数泛型参数类型不能是值类型、字符串类型或迭代类型!");
                    }

                    var castToType = node.Arguments
                        .Select(x => x.Type)
                        .First();

                    if (node.Type == castToType)
                        return base.Visit(node.Arguments[0]);

                    useCast = true;

                    _TypeCache.GetOrAdd(type, _ => GetInitialType(castToType));

                    if (!buildSelect)
                        return base.Visit(node.Arguments[0]);//? 说明Cast函数在Select函数之前，不需要进行函数分析!

                    var entry = RuntimeTypeCache.Instance.GetCache(type);

                    if (_CastList is null)
                        _CastList = entry.PropertyStores
                            .Select(x => x.Name.ToLower())
                            .ToList();
                    else //? 取交集
                        _CastList = _CastList
                            .Intersect(entry.PropertyStores.Select(x => x.Name.ToLower()))
                            .ToList();

                    if (_CastList.Count == 0)
                    {
                        throw new DException("未指定查询字段!");
                    }

                    buildCast = true;

                    return base.Visit(node.Arguments[0]);
                default:
                    return VisitFormatterMethodCall(node);
            }
        }

        /// <summary>
        /// Queryable 拓展方法
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected virtual Expression VisitQueryableExtentionsMethodCall(MethodCallExpression node)
        {
            string name = node.Method.Name;

            switch (name)
            {
                case MethodCall.From:

                    var value = (Func<ITableRegions, string>)node.Arguments[1].GetValueFromExpression();

                    if (value == null)
                        throw new DException("指定表名称不能为空!");

                    if (!buildFrom)
                        base.Visit(node.Arguments[0]);

                    SetTableFactory(value);

                    if (buildFrom)
                        return base.Visit(node.Arguments[0]);

                    return node;
                case MethodCall.TakeFirst:
                case MethodCall.TakeFirstOrDefault:
                case MethodCall.TakeSingle:
                case MethodCall.TakeSingleOrDefault:

                    // TOP(1)
                    SQLWriter.Take = 1;

                    buildSelect = false;

                    SQLWriter.Select();

                    buildFrom = false;

                    WriteAppendAtFix(() =>
                    {
                        if (isDistinct) SQLWriter.Distinct();

                        buildFrom = true;
                        base.Visit(node.Arguments[1]);
                        buildFrom = false;

                    }, () => base.Visit(node.Arguments[0]));

                    return node;
                case MethodCall.TakeLast:
                case MethodCall.TakeLastOrDefault:

                    // TOP(..)
                    SQLWriter.Take = 1;

                    isOrderByReverse ^= true;

                    buildSelect = false;

                    SQLWriter.Select();

                    buildFrom = false;

                    WriteAppendAtFix(() =>
                    {
                        if (isDistinct) SQLWriter.Distinct();

                        buildFrom = true;
                        base.Visit(node.Arguments[1]);
                        buildFrom = false;

                    }, () => base.Visit(node.Arguments[0]));

                    if (!isContainsOrderBy)
                        throw new ExpressionNotSupportedException($"使用函数({name})时，必须使用排序函数(OrderBy/OrderByDescending)!");

                    return node;
                default:
                    return VisitFormatterMethodCall(node);
            }
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable))
            {
                return VisitQueryableMethodCall(node);
            }

            if (node.Method.DeclaringType == typeof(QueryableExtentions))
            {
                return VisitQueryableExtentionsMethodCall(node);
            }

            return base.VisitMethodCall(node);
        }

        private Expression VisitLambdaFrom<T>(Expression<T> node)
        {
            var parameter = node.Parameters.First();

            var type = parameter.Type;

            var regions = MakeTableRegions(type);

            if (node.Body.NodeType == ExpressionType.Parameter)
            {
                GetOrAddTablePrefix(regions.TableType, parameter.Name);
            }
            else
            {
                GetOrAddTablePrefix(regions.TableType, node.Name ?? parameter.Name);
            }

            if (node.Body.NodeType == ExpressionType.Parameter)
            {
                BuildColumns(regions);
            }
            else
            {
                isSingleField = node.Body.NodeType == ExpressionType.MemberAccess || node.Body.NodeType == ExpressionType.Call;

                base.Visit(node.Body);

                isSingleField = false;
            }

            MakeFrom(regions);

            return node;
        }

        protected override Expression VisitLambda<T>(Expression<T> node, Action<Type, string> addPrefixCache)
        {
            if (node.Parameters.Count > 1)
                throw new ExpressionNotSupportedException("不支持多个参数!");

            if (buildJoin || SQLWriter.IsUnion)
            {
                base.Visit(node.Body);

                return node;
            }

            var parameter = node.Parameters[0];

            var type = parameter.Type;

            if (useCast)
            {
                type = GetInitialType(type);
            }

            addPrefixCache.Invoke(type, parameter.Name);

            if (buildFrom && (
                !buildSelect || //? 查询中使用了方法。
                node.Body.NodeType == ExpressionType.Parameter ||
                node.Body.NodeType == ExpressionType.New ||
                node.Body.NodeType == ExpressionType.MemberAccess ||
                node.Body.NodeType == ExpressionType.MemberInit)
             )
            {
                return VisitLambdaFrom(node);
            }

            base.Visit(node.Body);

            return node;
        }

        protected override IEnumerable<MemberBinding> FilterMemberBindings(IEnumerable<MemberBinding> bindings) => bindings.Where(item => InCastList(item.Member.Name));

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (SQLWriter.IsUnion) return node;

            return base.VisitParameter(node);
        }

        protected override Builder CreateBuilder(ISQLCorrectSettings settings) => new QueryBuilder(settings);

        #endregion
    }
}
