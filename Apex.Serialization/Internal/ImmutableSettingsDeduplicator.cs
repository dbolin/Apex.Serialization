using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Apex.Serialization
{
    internal sealed class ImmutableSettingsDeduplicator : IEqualityComparer<ImmutableSettings>
    {
        public bool Equals([AllowNull] ImmutableSettings x, [AllowNull] ImmutableSettings y)
        {
            return
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                x.AllowFunctionSerialization == y.AllowFunctionSerialization
                && x.InliningMaxDepth == y.InliningMaxDepth
                && x.SerializationMode == y.SerializationMode
                && x.SupportSerializationHooks == y.SupportSerializationHooks
                && x.UseConstructors == y.UseConstructors
                && x.FlattenClassHierarchy == y.FlattenClassHierarchy
                && x.ForceReflectionToSetReadonlyFields == y.ForceReflectionToSetReadonlyFields
                && x.UseSerializedVersionId == y.UseSerializedVersionId
                && x.CustomActionSerializers.SequenceEqual(y.CustomActionSerializers)
                && x.CustomActionDeserializers.SequenceEqual(y.CustomActionDeserializers)
                && x.WhitelistedTypes.SequenceEqual(y.WhitelistedTypes)
                && x.WhitelistFuncs.SequenceEqual(y.WhitelistFuncs);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        public int GetHashCode([DisallowNull] ImmutableSettings obj)
        {
            return obj.GetHashCode();
        }
    }
}