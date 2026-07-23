using System;

namespace Playserv.Proxy.Common
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PlayServLocalExecutionFactoryAttribute : Attribute
    {
        public PlayServLocalExecutionFactoryAttribute(Type implementationType, int priority)
        {
            ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
            Priority = priority;
        }

        public Type ImplementationType { get; }

        public int Priority { get; }
    }
}
