using System;
using System.Collections.Generic;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// The types a reader has resolved, keyed by the whole serialized type id including its length header, so two ids
    /// with the same bytes split differently between type and assembly name stay apart.
    /// </summary>
    internal sealed class TypeLookup
    {
        private readonly Dictionary<byte[], Type> _types = new Dictionary<byte[], Type>(ContentComparer.Instance);
        private readonly Dictionary<byte[], Type>.AlternateLookup<ReadOnlySpan<byte>> _bySpan;

        public TypeLookup()
        {
            _bySpan = _types.GetAlternateLookup<ReadOnlySpan<byte>>();
        }

        public Type? Find(ReadOnlySpan<byte> typeId) => _bySpan.TryGetValue(typeId, out var type) ? type : null;

        public void Add(ReadOnlySpan<byte> typeId, Type type) => _bySpan.TryAdd(typeId, type);

        private sealed class ContentComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
        {
            internal static readonly ContentComparer Instance = new ContentComparer();

            public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

            public int GetHashCode(byte[] obj) => GetHashCode((ReadOnlySpan<byte>)obj);

            public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

            public int GetHashCode(ReadOnlySpan<byte> alternate)
            {
                var hash = new HashCode();
                hash.AddBytes(alternate);
                return hash.ToHashCode();
            }

            public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
        }
    }
}
