using System;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class StaticTypeInfo<T>
    {
        internal static bool IsSealed = typeof(T).IsSealed;
        internal static bool IsValueType = typeof(T).IsValueType;
        internal static bool IsNullable = typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}
