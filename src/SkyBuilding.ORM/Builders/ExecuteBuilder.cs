using SkyBuilding.ORM.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace SkyBuilding.ORM.Builders
{
    /// <summary>
    /// 执行构造器
    /// </summary>
    public class ExecuteBuilder<T> : Builder, IBuilder<T>
    {
        private SmartSwitch _whereSwitch = null;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="settings">修正配置</param>
        public ExecuteBuilder(ISQLCorrectSettings settings) : base(settings)
        {
        }

        /// <summary>
        /// 表达式测评
        /// </summary>
        /// <param name="node">表达式</param>
        public override void Evaluate(Expression node)
        {
            _whereSwitch = new SmartSwitch(SQLWriter.Where, SQLWriter.WriteAnd);

            base.Evaluate(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Executeable))
            {
                return VisitExecuteMethodCall(node);
            }

            return base.VisitMethodCall(node);
        }

        public ExecuteBehavior Behavior { get; private set; }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (typeof(IExecuteable).IsAssignableFrom(node.Type))
                return node;

            return base.VisitConstant(node);
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

        private Expression VisitExecuteMethodCall(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case MethodCall.From:

                    var value = (Func<ITableRegions, string>)node.Arguments[1].GetValueFromExpression();

                    if (value == null)
                        throw new DException("指定表名称不能为空!");

                    SetTableFactory(value);

                    base.Visit(node.Arguments[0]);

                    return node;

                case MethodCall.Where:
                    if (Behavior == ExecuteBehavior.Insert)
                        throw new ExpressionNotSupportedException("插入语句不支持条件，请在查询器中使用条件过滤!");

                    return MakeWhereNode(node);
                case MethodCall.Update:
                    Behavior = ExecuteBehavior.Update;

                    base.Visit(node.Arguments[0]);

                    SQLWriter.AppendAt = 0;

                    SQLWriter.Update();

                    SQLWriter.Alias(GetOrAddTablePrefix(typeof(T)));

                    SQLWriter.Set();

                    base.Visit(node.Arguments[1]);

                    SQLWriter.From();

                    WriteTable(typeof(T));

                    SQLWriter.AppendAt = -1;

                    return node;
                case MethodCall.Delete:
                    Behavior = ExecuteBehavior.Delete;

                    base.Visit(node.Arguments[0]);

                    SQLWriter.AppendAt = 0;

                    SQLWriter.Delete();

                    SQLWriter.Alias(GetOrAddTablePrefix(typeof(T)));

                    SQLWriter.From();

                    WriteTable(typeof(T));

                    SQLWriter.AppendAt = -1;

                    return node;
                case MethodCall.Insert:
                    Behavior = ExecuteBehavior.Insert;

                    base.Visit(node.Arguments[0]);

                    SQLWriter.AppendAt = 0;
                    SQLWriter.Insert();

                    WriteTable(typeof(T));

                    SQLWriter.AppendAt = -1;

                    VisitBuilder(node.Arguments[1]);

                    return node;
            }

            throw new ExpressionNotSupportedException();
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            var regions = MakeTableRegions(typeof(T));

            if (regions.ReadWrites.TryGetValue(node.Member.Name, out string value))
            {
                SQLWriter.Name(value);
                SQLWriter.Write("=");

                return base.VisitMemberAssignment(node);
            }

            throw new ExpressionNotSupportedException($"{node.Member.Name}字段不可写!");
        }
        protected override Builder CreateBuilder(ISQLCorrectSettings settings) => new QueryBuilder(settings);
    }
}
