﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace SkyBuilding.ORM
{
    /// <summary>
    /// SQL写入流
    /// </summary>
    public class Writer
    {
        private readonly IWriterMap _writer;

        private StringBuilder _orderBy;

        private readonly StringBuilder _builder;

        private readonly ISQLCorrectSettings settings;

        protected internal int ParameterIndex { get; internal set; } = 0;
        protected virtual string ParameterName
        {
            get
            {
                return string.Concat("__variable_", ParameterIndex += 1);
            }
        }

        internal int AppendAt = -1;
        public int Length => _builder.Length + (_orderBy?.Length ?? 0);
        internal bool Not { get; set; }
        internal int Take { get; set; }
        internal int Skip { get; set; }

        private bool _isUnion = false;

        internal bool IsUnion
        {
            get => _isUnion;
            set
            {
                _isUnion = value;

                if (_isOrderBy && _isUnion)
                {
                    _orderBy = _orderBy ?? new StringBuilder();
                }
            }
        }

        private bool _isOrderBy = false;
        internal bool IsOrderBy
        {
            get => _isOrderBy; set
            {
                _isOrderBy = value;

                if (_isOrderBy && _isUnion)
                {
                    _orderBy = _orderBy ?? new StringBuilder();
                }
            }
        }
        internal void Remove(int index, int lenght) => _builder.Remove(index, lenght);

        private Dictionary<string, object> paramsters;

        public Dictionary<string, object> Parameters { internal set => paramsters = value; get => paramsters ?? (paramsters = new Dictionary<string, object>()); }
        public Writer(ISQLCorrectSettings settings, IWriterMap writer)
        {
            _builder = new StringBuilder();

            _writer = writer ?? throw new ArgumentNullException(nameof(writer));

            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }
        public void CloseBrace() => Write(_writer.CloseBrace);
        public void Delimiter() => Write(_writer.Delimiter);
        public void Distinct() => Write("DISTINCT" + _writer.WhiteSpace);
        public void EmptyString() => Write(_writer.EmptyString);
        public void Exists()
        {
            if (Not) WriteNot();

            Write("EXISTS");
        }
        public void Like()
        {
            WhiteSpace();

            if (Not) WriteNot();

            Write("LIKE");

            WhiteSpace();
        }
        public void Contains()
        {
            WhiteSpace();

            if (Not) WriteNot();

            Write("IN");
        }
        public void From() => Write(_writer.WhiteSpace + "FROM" + _writer.WhiteSpace);
        public void Join() => Write(_writer.WhiteSpace + "LEFT" + _writer.WhiteSpace + "JOIN" + _writer.WhiteSpace);
        public void OpenBrace() => Write(_writer.OpenBrace);
        public void OrderBy() => Write(_writer.WhiteSpace + "ORDER" + _writer.WhiteSpace + "BY" + _writer.WhiteSpace);
        public virtual void Parameter(object value)
        {
            if (value == null)
            {
                WriteNull();
                return;
            }

            string paramterName = settings.ParamterName(ParameterName);

            Write(paramterName);

            Parameters.Add(paramterName, value);
        }
        public void Parameter(string name, object value)
        {
            if (value == null)
            {
                WriteNull();
                return;
            }

            if (name == null || name.Length == 0)
            {
                Parameter(value);
                return;
            }

            string paramterName = settings.ParamterName(name);

            if (!Parameters.TryGetValue(paramterName, out object data))
            {
                Write(paramterName);

                Parameters.Add(paramterName, value);

                return;
            }

            if (Equals(value, data))
            {
                Write(paramterName);

                return;
            }

            paramterName = settings.ParamterName(string.Concat(name, ParameterIndex += 1));

            Write(paramterName);

            Parameters.Add(paramterName, value);
        }
        public void Select() => Write("SELECT" + _writer.WhiteSpace);

        public void Insert() => Write("INSERT" + _writer.WhiteSpace + "INTO" + _writer.WhiteSpace);

        public void Values() => Write("VALUES");

        public void Update() => Write("UPDATE" + _writer.WhiteSpace);

        public void Set() => Write(_writer.WhiteSpace + "SET" + _writer.WhiteSpace);

        public void Delete() => Write("DELETE" + _writer.WhiteSpace);

        public void Where() => Write(_writer.WhiteSpace + "WHERE" + _writer.WhiteSpace);
        public void WriteAnd() => Write(_writer.WhiteSpace + (Not ? "OR" : "AND") + _writer.WhiteSpace);
        public void WriteDesc() => Write(_writer.WhiteSpace + "DESC");
        public void WriteIS() => Write(_writer.WhiteSpace + "IS" + _writer.WhiteSpace);
        public void WriteNot() => Write("NOT" + _writer.WhiteSpace);
        public virtual void WriteNull() => Write("NULL");
        public void WriteOr() => Write(_writer.WhiteSpace + (Not ? "AND" : "OR") + _writer.WhiteSpace);
        public void WritePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;

            Name(prefix);

            Write(".");
        }
        public void Alias(string name) => Write(_writer.Alias(name));
        public void As() => Write(_writer.WhiteSpace + "AS" + _writer.WhiteSpace);
        public void As(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            As();

            Alias(name);
        }
        public void Name(string name) => Write(_writer.Name(name));
        public void TableName(string name) => Write(_writer.TableName(name));
        /// <summary>
        /// 空格
        /// </summary>
        public void WhiteSpace() => Write(_writer.WhiteSpace);
        public void Name(string prefix, string name)
        {
            WritePrefix(prefix);

            Name(name);
        }
        public void IsNull()
        {
            WriteIS();

            if (Not)
            {
                WriteNot();
            }

            WriteNull();
        }
        public void LengthMethod()
        {
            Write(settings.Length);
        }
        public void IndexOfMethod()
        {
            Write(settings.IndexOf);
        }
        public void SubstringMethod()
        {
            Write(settings.Substring);
        }
        public void TableName(string name, string alias)
        {
            TableName(name);

            if (string.IsNullOrEmpty(alias)) return;

            WhiteSpace();

            Alias(alias);
        }
        public void Write(string value)
        {
            if (value == null || value.Length == 0)
                return;

            if (IsUnion && IsOrderBy)
            {
                _orderBy.Append(value);
                return;
            }

            if (AppendAt > -1)
            {
                _builder.Insert(AppendAt, value);
                AppendAt += value.Length;
            }
            else
            {
                _builder.Append(value);
            }
        }
        public void Write(ExpressionType nodeType)
        {
            Write(ExpressionExtensions.GetOperator(Not ? nodeType.ReverseWhere() : nodeType));
        }
        public void Equal() => Write(ExpressionType.Equal);
        public void NotEqual() => Write(ExpressionType.NotEqual);
        public virtual void BooleanFalse()
        {
            if (Not)
            {
                Parameter("__variable_true", true);
            }
            else
            {
                Parameter("__variable_false", false);
            }
        }
        public virtual void BooleanTrue()
        {
            if (Not)
            {
                Parameter("__variable_false", false);
            }
            else
            {
                Parameter("__variable_true", true);
            }

        }
        public virtual string ToSQL()
        {
            if (Take > 0 || Skip > 0)
                return IsUnion ?
                    settings.PageUnionSql(_builder.ToString(), Take, Skip, _orderBy?.ToString()) :
                    settings.PageSql(_builder.ToString(), Take, Skip);
            return _builder.ToString();
        }
    }
}
