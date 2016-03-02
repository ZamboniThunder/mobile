using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Toggl.Phoebe
{
    public static class ExpressionExtensions
    {
        public static string ToPropertyName<T> (this Expression<Func<T>> expr, object instance)
        {
            if (expr == null) {
                return null;
            }

            var member = expr.Body as MemberExpression;
            if (member == null) {
                throw new ArgumentException ("Expression should be in the format of: () => PropertyName", "expr");
            }

            var prop = member.Member as PropertyInfo;
            if (prop == null
                    || prop.DeclaringType == null
                    || !prop.DeclaringType.IsAssignableFrom (instance.GetType ())
                    || prop.GetGetMethod (true).IsStatic) {
                throw new ArgumentException ("Expression should be in the format of: () => PropertyName", "expr");
            }

            return prop.Name;
        }

        public static string ToPropertyName<K, T> (this Expression<Func<K, T>> expr)
        {
            if (expr == null) {
                return null;
            }

            var member = expr.Body as MemberExpression;
            if (member == null) {
                throw new ArgumentException ("Expression should be in the format of: (o) => o.PropertyName", "expr");
            }

            var prop = member.Member as PropertyInfo;
            if (prop == null
                    || prop.DeclaringType == null
                    || !prop.DeclaringType.IsAssignableFrom (typeof (K))
                    || prop.GetGetMethod (true).IsStatic) {
                throw new ArgumentException ("Expression should be in the format of: (o) => o.PropertyName", "expr");
            }

            return prop.Name;
        }
    }
}
