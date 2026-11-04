using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

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

        private byte[] _lastFoundId = Array.Empty<byte>();
        private Type? _lastFoundType;

        public TypeLookup()
        {
            _bySpan = _types.GetAlternateLookup<ReadOnlySpan<byte>>();
        }

        public Type? Find(ReadOnlySpan<byte> typeId)
        {
            if (_lastFoundType != null && typeId.SequenceEqual(_lastFoundId))
            {
                return _lastFoundType;
            }

            if (!_bySpan.TryGetValue(typeId, out var id, out var type))
            {
                return null;
            }

            _lastFoundId = id;
            _lastFoundType = type;
            return type;
        }

        public void Add(ReadOnlySpan<byte> typeId, Type type) => _bySpan.TryAdd(typeId, type);

        internal sealed class ContentComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
        {
            internal static readonly ContentComparer Instance = new ContentComparer();

            private static readonly ulong Seed = (ulong)Random.Shared.NextInt64(long.MinValue, long.MaxValue);

            private const ulong P1 = 0x9E3779B185EBCA87, P2 = 0xC2B2AE3D27D4EB4F, P3 = 0x165667B19E3779F9;

            public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

            public int GetHashCode(byte[] obj) => GetHashCode((ReadOnlySpan<byte>)obj);

            public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

            /// <summary>
            /// Not <see cref="HashCode.AddBytes"/>: on .NET 10 its inlined state goes through memory, so every four bytes
            /// wait on a store and a reload.  <see cref="TypeLookup.Find"/> hashes an id of around a hundred bytes for each
            /// type's first reference in a payload, unless it is the id found last.
            /// <para>
            /// The seed doesn't make the table resist flooding (an unkeyed round like this one has differentials that hold
            /// for every seed).  What bounds it is the reader, whose <c>ReadTypeRef</c> adds only ids that are the
            /// canonical name of a type it resolved, so the table holds at most one entry per type.
            /// </para>
            /// </summary>
            public int GetHashCode(ReadOnlySpan<byte> alternate)
            {
                var length = alternate.Length;
                ulong a = Seed + P1 + P2, b = Seed + P2, c = Seed, d = Seed - P1;
                if (length >= 32)
                {
                    for (int i = 0; i < length - 32; i += 32)
                    {
                        a = Round(a, Word(alternate, i));
                        b = Round(b, Word(alternate, i + 8));
                        c = Round(c, Word(alternate, i + 16));
                        d = Round(d, Word(alternate, i + 24));
                    }

                    var last = length - 32;
                    a = Round(a, Word(alternate, last));
                    b = Round(b, Word(alternate, last + 8));
                    c = Round(c, Word(alternate, last + 16));
                    d = Round(d, Word(alternate, last + 24));
                }
                else if (length >= 8)
                {
                    a = Round(a, Word(alternate, 0));
                    b = Round(b, Word(alternate, length - 8));
                    if (length > 16)
                    {
                        c = Round(c, Word(alternate, 8));
                        d = Round(d, Word(alternate, length - 16));
                    }
                }
                else
                {
                    ulong word = 0;
                    foreach (var value in alternate)
                    {
                        word = (word << 8) | value;
                    }

                    a = Round(a, word);
                }

                var hash = BitOperations.RotateLeft(a, 1) + BitOperations.RotateLeft(b, 7) + BitOperations.RotateLeft(c, 12)
                    + BitOperations.RotateLeft(d, 18) + (ulong)length;
                hash = (hash ^ (hash >> 33)) * P2;
                hash = (hash ^ (hash >> 29)) * P3;
                return (int)(hash ^ (hash >> 32));
            }

            private static ulong Round(ulong lane, ulong word) => BitOperations.RotateLeft(lane + word * P2, 31) * P1;

            private static ulong Word(ReadOnlySpan<byte> bytes, int start) => MemoryMarshal.Read<ulong>(bytes.Slice(start, sizeof(ulong)));

            public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
        }
    }
}
