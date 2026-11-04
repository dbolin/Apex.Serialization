using System;
using System.Numerics;

namespace Apex.Serialization.Internal
{
    public static class NonRandomHashCode
    {
        public static int Ordinal(string s)
        {
            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;

            var chars = s.AsSpan();
            int index = 0;
            int length = s.Length;

            while (length > 2)
            {
                length -= 4;
                hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ CharPair(chars, index);
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ CharPair(chars, index + 2);
                index += 4;
            }

            if (length > 0)
            {
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ CharPair(chars, index);
            }

            return (int)(hash1 + (hash2 * 1566083941));
        }

        /// <summary>
        /// Two chars as one little-endian uint.  The hash was first computed through a pointer that read the string's null
        /// terminator as the second char of a final pair, and serialized version ids depend on its exact values.
        /// </summary>
        private static uint CharPair(ReadOnlySpan<char> chars, int index)
        {
            return chars[index] | (index + 1 < chars.Length ? (uint)chars[index + 1] << 16 : 0u);
        }
    }
}
