using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Playserv.DataSubscription
{
    internal static class QueryBuilder
    {
        public static string BuildQuery<T>(string entityType, object key, Expression<Func<T, object>> selector = null)
        {
            var sb = new StringBuilder();
            sb.Append(entityType);
            sb.Append("(id: $id)");

            if (selector != null)
            {
                sb.Append(" { ");
                BuildSelection(selector.Body, sb);
                sb.Append(" }");
            }
            else
            {
                sb.Append(" { * }");
            }

            return sb.ToString();
        }

        private static void BuildSelection(Expression expression, StringBuilder sb)
        {
            switch (expression)
            {
                case MemberExpression member:
                    sb.Append(member.Member.Name);
                    break;

                case NewExpression newExpr:
                    var first = true;
                    foreach (var arg in newExpr.Arguments)
                    {
                        if (!first) sb.Append(" ");
                        BuildSelection(arg, sb);
                        first = false;
                    }
                    break;

                case MemberInitExpression init:
                    first = true;
                    foreach (var binding in init.Bindings)
                    {
                        if (binding is MemberAssignment assignment)
                        {
                            if (!first) sb.Append(" ");
                            sb.Append(assignment.Member.Name);
                            if (assignment.Expression is MemberExpression me)
                            {
                                sb.Append(" { ");
                                BuildSelection(me, sb);
                                sb.Append(" }");
                            }
                            first = false;
                        }
                    }
                    break;

                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                    BuildSelection(unary.Operand, sb);
                    break;

                default:
                    break;
            }
        }

        public static Dictionary<string, object> BuildVariables(object key)
        {
            return new Dictionary<string, object> { { "id", key } };
        }
    }
}
