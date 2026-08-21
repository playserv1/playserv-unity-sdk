using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Playserv.Data
{
    internal static class PlayServRecordExpressionParser
    {
        public static IReadOnlyList<PlayServQueryFilter> Parse<T>(
            Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            var filters = new List<PlayServQueryFilter>();
            ParseNode(predicate.Body, predicate.Parameters[0], filters);
            if (filters.Count == 0)
                throw Unsupported(predicate.Body);
            return filters;
        }

        private static void ParseNode(
            Expression expression,
            ParameterExpression parameter,
            ICollection<PlayServQueryFilter> filters)
        {
            expression = StripConvert(expression);
            if (expression is BinaryExpression andAlso &&
                andAlso.NodeType == ExpressionType.AndAlso)
            {
                ParseNode(andAlso.Left, parameter, filters);
                ParseNode(andAlso.Right, parameter, filters);
                return;
            }

            if (expression is BinaryExpression comparison &&
                TryMapComparison(comparison.NodeType, out var op))
            {
                AddComparison(comparison.Left, comparison.Right, op, parameter, filters);
                return;
            }

            if (expression is MethodCallExpression call &&
                TryParseEnumerableContains(call, parameter, out var filter))
            {
                filters.Add(filter);
                return;
            }

            throw Unsupported(expression);
        }

        private static void AddComparison(
            Expression left,
            Expression right,
            PlayServQueryOperator op,
            ParameterExpression parameter,
            ICollection<PlayServQueryFilter> filters)
        {
            if (TryGetField(left, parameter, out var field))
            {
                var value = EvaluateValue(right, parameter);
                filters.Add(CreateComparison(field, op, value));
                return;
            }

            if (TryGetField(right, parameter, out field))
            {
                var value = EvaluateValue(left, parameter);
                filters.Add(CreateComparison(field, Reverse(op), value));
                return;
            }

            throw new ArgumentException(
                "A query comparison must compare one model field with a constant or captured value.");
        }

        private static PlayServQueryFilter CreateComparison(
            string field,
            PlayServQueryOperator op,
            object value)
        {
            if (value != null)
                return new PlayServQueryFilter(field, op, value, null);

            if (op == PlayServQueryOperator.Eq)
                return new PlayServQueryFilter(field, PlayServQueryOperator.IsNull, null, null);
            if (op == PlayServQueryOperator.Neq)
                return new PlayServQueryFilter(field, PlayServQueryOperator.IsNotNull, null, null);

            throw new ArgumentException("Only equality and inequality comparisons can use null.");
        }

        private static bool TryParseEnumerableContains(
            MethodCallExpression call,
            ParameterExpression parameter,
            out PlayServQueryFilter filter)
        {
            filter = null;

            Expression valuesExpression;
            Expression fieldExpression;
            if (call.Method.IsStatic &&
                call.Method.DeclaringType == typeof(Enumerable) &&
                string.Equals(call.Method.Name, "Contains", StringComparison.Ordinal) &&
                call.Arguments.Count == 2)
            {
                valuesExpression = call.Arguments[0];
                fieldExpression = call.Arguments[1];
            }
            else if (!call.Method.IsStatic &&
                     string.Equals(call.Method.Name, "Contains", StringComparison.Ordinal) &&
                     call.Object != null &&
                     call.Object.Type != typeof(string) &&
                     call.Arguments.Count == 1)
            {
                valuesExpression = call.Object;
                fieldExpression = call.Arguments[0];
            }
            else
            {
                return false;
            }

            if (!TryGetField(fieldExpression, parameter, out var field))
                throw new ArgumentException("Contains must test a model field against a captured collection.");

            var values = EvaluateValue(valuesExpression, parameter);
            if (values == null || values is string || !(values is IEnumerable enumerable))
                throw new ArgumentException("The collection used by Contains must be a non-null enumerable value.");

            var materialized = new List<object>();
            foreach (var value in enumerable)
                materialized.Add(value);

            filter = new PlayServQueryFilter(field, PlayServQueryOperator.In, materialized, null);
            return true;
        }

        private static bool TryGetField(
            Expression expression,
            ParameterExpression parameter,
            out string field)
        {
            field = null;
            expression = StripConvert(expression);
            if (!(expression is MemberExpression member))
                return false;

            var owner = StripConvert(member.Expression);
            if (owner != parameter)
                return false;

            field = PlayServRecordWireNames.FromMember(member.Member, nameof(expression));
            return true;
        }

        private static object EvaluateValue(
            Expression expression,
            ParameterExpression parameter)
        {
            expression = StripConvert(expression);
            switch (expression)
            {
                case ConstantExpression constant:
                    return constant.Value;

                case MemberExpression member:
                    if (ContainsParameter(member, parameter))
                        throw new ArgumentException("Field-to-field query comparisons are not supported.");

                    var target = member.Expression == null
                        ? null
                        : EvaluateValue(member.Expression, parameter);
                    if (member.Member is FieldInfo field)
                        return field.GetValue(target);
                    if (member.Member is PropertyInfo property && property.GetIndexParameters().Length == 0)
                        return property.GetValue(target, null);
                    throw Unsupported(expression);

                case NewArrayExpression array:
                    var elementType = expression.Type.GetElementType() ?? typeof(object);
                    var result = Array.CreateInstance(elementType, array.Expressions.Count);
                    for (var index = 0; index < array.Expressions.Count; index++)
                        result.SetValue(EvaluateValue(array.Expressions[index], parameter), index);
                    return result;

                default:
                    throw Unsupported(expression);
            }
        }

        private static bool ContainsParameter(Expression expression, ParameterExpression parameter)
        {
            var visitor = new ParameterSearchVisitor(parameter);
            visitor.Visit(expression);
            return visitor.Found;
        }

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert ||
                    unary.NodeType == ExpressionType.ConvertChecked))
            {
                expression = unary.Operand;
            }

            return expression;
        }

        private static bool TryMapComparison(
            ExpressionType nodeType,
            out PlayServQueryOperator op)
        {
            switch (nodeType)
            {
                case ExpressionType.Equal:
                    op = PlayServQueryOperator.Eq;
                    return true;
                case ExpressionType.NotEqual:
                    op = PlayServQueryOperator.Neq;
                    return true;
                case ExpressionType.GreaterThan:
                    op = PlayServQueryOperator.Gt;
                    return true;
                case ExpressionType.GreaterThanOrEqual:
                    op = PlayServQueryOperator.Gte;
                    return true;
                case ExpressionType.LessThan:
                    op = PlayServQueryOperator.Lt;
                    return true;
                case ExpressionType.LessThanOrEqual:
                    op = PlayServQueryOperator.Lte;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static PlayServQueryOperator Reverse(PlayServQueryOperator op)
        {
            switch (op)
            {
                case PlayServQueryOperator.Gt: return PlayServQueryOperator.Lt;
                case PlayServQueryOperator.Gte: return PlayServQueryOperator.Lte;
                case PlayServQueryOperator.Lt: return PlayServQueryOperator.Gt;
                case PlayServQueryOperator.Lte: return PlayServQueryOperator.Gte;
                default: return op;
            }
        }

        private static ArgumentException Unsupported(Expression expression) =>
            new ArgumentException(
                $"Unsupported query expression '{expression}'. Use comparisons joined with &&, " +
                "capturedCollection.Contains(x.Field), or the explicit selector/operator overloads.");

        private sealed class ParameterSearchVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;

            public ParameterSearchVisitor(ParameterExpression parameter)
            {
                _parameter = parameter;
            }

            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _parameter)
                    Found = true;
                return base.VisitParameter(node);
            }
        }
    }
}
