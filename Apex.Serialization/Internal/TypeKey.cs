using System;
using System.Collections.Generic;

namespace Apex.Serialization.Internal
{
    internal struct TypeKey : IEquatable<TypeKey>
    {
        public Type Type;
        public ImmutableSettings Settings;
        public bool IncludesTypeInfo;

        public TypeKey(Type type, ImmutableSettings settings, bool includesTypeInfo)
        {
            Type = type;
            Settings = settings;
            IncludesTypeInfo = includesTypeInfo;
        }

        public override bool Equals(object? obj)
        {
            return obj is TypeKey key && Equals(key);
        }

        public bool Equals(TypeKey other)
        {
            return EqualityComparer<Type>.Default.Equals(Type, other.Type) &&
                   EqualityComparer<ImmutableSettings>.Default.Equals(Settings, other.Settings) &&
                   IncludesTypeInfo == other.IncludesTypeInfo;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, Settings, IncludesTypeInfo);
        }

        public static bool operator ==(TypeKey left, TypeKey right) => left.Equals(right);
        public static bool operator !=(TypeKey left, TypeKey right) => !(left == right);
    }
}
