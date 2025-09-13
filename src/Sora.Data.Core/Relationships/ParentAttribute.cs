using System;

namespace Sora.Data.Core.Relationships
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParentAttribute : Attribute
    {
        public Type ParentType { get; }

        public ParentAttribute(Type parentType)
        {
            ParentType = parentType;
        }
    }
}
