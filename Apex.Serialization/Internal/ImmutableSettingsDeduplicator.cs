using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Apex.Serialization
{
    internal sealed class ImmutableSettingsDeduplicator : IEqualityComparer<ImmutableSettings>
    {
        public bool Equals([AllowNull] ImmutableSettings x, [AllowNull] ImmutableSettings y)
        {
            return
                x.AllowFunctionSerialization == y.AllowFunctionSerialization
                && x.InliningMaxDepth == y.InliningMaxDepth
                && x.SerializationMode == y.SerializationMode
                && x.SupportSerializationHooks == y.SupportSerializationHooks
                && x.UseConstructors == x.UseConstructors;
        }

        public int GetHashCode([DisallowNull] ImmutableSettings obj)
        {
            return obj.GetHashCode();
        }
    }
}