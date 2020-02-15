using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal
{
    public static class NonRandomHashCode
    {
        public static unsafe int Ordinal(string s)
        {
            fixed (char* src = s)
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;

                uint* ptr = (uint*)src;
                int length = s.Length;

                while (length > 2)
                {
                    length -= 4;
                    // Where length is 4n-1 (e.g. 3,7,11,15,19) this additionally consumes the null terminator
                    hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ ptr[0];
                    hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[1];
                    ptr += 2;
                }

                if (length > 0)
                {
                    // Where length is 4n-3 (e.g. 1,5,9,13,17) this additionally consumes the null terminator
                    hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[0];
                }

                return (int)(hash1 + (hash2 * 1566083941));
            }
        }

        public static unsafe int OrdinalIgnoreCase(string s)
        {
            fixed (char* src = s)
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;

                uint* ptr = (uint*)src;
                int length = s.Length;

                while (length > 2)
                {
                    uint value1 = ptr[0];
                    uint value2 = ptr[1];

                    if (((value1 | value2) & 0xff80ff80) != 0)
                    {
                        // Non-ascii detected, fallback to non-ascii aware casing
                        return GetRemainingIgnoreCaseHashCode((char*)ptr, length, hash1, hash2);
                    }

                    length -= 4;
                    // Where length is 4n-1 (e.g. 3,7,11,15,19) this additionally consumes the null terminator

                    // Branchless double char (uint) ascii lowercase 
                    uint upper = value1 + 0x_0025_0025u; // Ignore chars > 'Z'
                    uint lower = upper + 0x_001a_001au;  // Ignore chars < 'A'
                    // Keep high bit for range, and shift into case bit, xor to flip case
                    value1 ^= (~(value1 | upper) & lower & 0x0080_0080u) >> 2;

                    upper = value2 + 0x_0025_0025u; // repeat for next uint
                    lower = upper + 0x_001a_001au;
                    value2 ^= (~(value2 | upper) & lower & 0x0080_0080u) >> 2;

                    hash1 = NonRandomizedHashCodeCombine(hash1, value1);
                    hash2 = NonRandomizedHashCodeCombine(hash2, value2);
                    ptr += 2;
                }

                if (length > 0)
                {
                    uint value = ptr[0];
                    if ((value & 0xff80ff80) != 0)
                    {
                        // Non-ascii detected, fallback to non-ascii aware casing
                        return GetRemainingIgnoreCaseHashCode((char*)ptr, length, hash1, hash2);
                    }

                    // Where length is 4n-3 (e.g. 1,5,9,13,17) this additionally consumes the null terminator

                    // Branchless double char (uint) ascii lowercase 
                    uint upper = value + 0x_0025_0025u; // Ignore chars > 'Z'
                    uint lower = upper + 0x_001a_001au; // Ignore chars < 'A'
                    // Keep high bit for range, and shift into case bit, xor to flip case
                    value ^= (~(value | upper) & lower & 0x0080_0080u) >> 2;

                    hash2 = NonRandomizedHashCodeCombine(hash2, value);
                }

                return NonRandomizedHashCodeFinalize(hash1, hash2);
            }
        }

        private static unsafe int GetRemainingIgnoreCaseHashCode(char* str, int remainingLength, uint hash1, uint hash2)
        {
            Debug.Assert(remainingLength > 0);
            Debug.Assert(str[remainingLength] == '\0', "str[remainingLength] == '\\0'");

            char[]? rentedArray = null;
            Span<char> span = remainingLength <= 255 ?
                stackalloc char[255] :
                (rentedArray = ArrayPool<char>.Shared.Rent(remainingLength));

            int charsWritten = new ReadOnlySpan<char>(str, remainingLength).ToLowerInvariant(span);
            span = span.Slice(0, charsWritten);

            fixed (char* src = span)
            {
                str = src;
                int length = span.Length;

                Debug.Assert(((int)str) % 4 == 0, "Array and stackalloc should start at 4 bytes boundary");

                uint* ptr = (uint*)str;

                while (length >= 4)
                {
                    length -= 4;
                    hash1 = NonRandomizedHashCodeCombine(hash1, ptr[0]);
                    hash2 = NonRandomizedHashCodeCombine(hash2, ptr[1]);
                    ptr += 2;
                }

                if (length >= 2)
                {
                    length -= 2;
                    hash2 = NonRandomizedHashCodeCombine(hash2, ptr[0]);
                    ptr += 1;
                }

                if (length == 1)
                {
                    // Need to process any remaining single char individually as there is no null terminator
                    // in lowercased temporary char array that we can consume at the same time.
                    // (Unlike in GetNonRandomizedHashCode which operates directly on the string)
                    hash2 = NonRandomizedHashCodeCombine(hash2, *(char*)ptr);
                }
            }

            // Return the borrowed array if necessary.
            if (rentedArray != null)
            {
                ArrayPool<char>.Shared.Return(rentedArray);
            }

            return NonRandomizedHashCodeFinalize(hash1, hash2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint NonRandomizedHashCodeCombine(uint hash, uint value)
                   => (((hash << 5) | (hash >> 27)) + hash) ^ value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NonRandomizedHashCodeFinalize(uint hash1, uint hash2)
           => (int)(hash1 + (hash2 * 1566083941));
    }
}
