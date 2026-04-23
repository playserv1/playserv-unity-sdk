using System;

namespace Playserv.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PlayServJsonNameAttribute : Attribute
    {
        public PlayServJsonNameAttribute(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }
    }
}
