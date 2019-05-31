using System;

namespace Apex.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AfterDeserialization : Attribute
    {
    }
}
