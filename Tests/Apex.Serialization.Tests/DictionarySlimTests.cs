using System.Linq;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class DictionarySlimTests
    {
        /// <summary>
        /// Every key lands in one of four buckets, so the chains hold several entries each.
        /// </summary>
        private sealed class Key
        {
            private readonly int _hash;

            public Key(int hash) => _hash = hash;

            public override int GetHashCode() => _hash;
        }

        // Truncating walks the removed entries when they are fewer than a 32nd of the buckets and rebuilds the
        // chains otherwise, so the capacities put each count on either side of that.
        [Theory]
        [InlineData(4096, 3, 40)]
        [InlineData(4096, 1, 1)]
        [InlineData(4096, 0, 40)]
        [InlineData(16, 3, 40)]
        [InlineData(16, 3, 400)]
        [InlineData(16, 0, 400)]
        [InlineData(16, 40, 0)]
        public void TruncatingLeavesTheFirstEntriesAndTakesNewOnes(int capacity, int kept, int removed)
        {
            var keys = Enumerable.Range(0, kept + removed).Select(i => new Key(i % 4)).ToArray();
            var dictionary = new DictionarySlim<Key, int>(capacity);
            for (var i = 0; i < keys.Length; ++i)
            {
                dictionary.GetOrAddValueRef(keys[i]) = i + 1;
            }

            for (var pass = 0; pass < 2; ++pass)
            {
                dictionary.TruncateTo(kept);

                dictionary.Count.Should().Be(kept);
                for (var i = 0; i < keys.Length; ++i)
                {
                    dictionary.TryGetValue(keys[i], out var value).Should().Be(i < kept);
                    value.Should().Be(i < kept ? i + 1 : 0);
                }

                for (var i = kept; i < keys.Length; ++i)
                {
                    ref var value = ref dictionary.GetOrAddValueRef(keys[i]);
                    value.Should().Be(0);
                    value = i + 1;
                }

                dictionary.Count.Should().Be(keys.Length);
                for (var i = 0; i < keys.Length; ++i)
                {
                    dictionary.TryGetValue(keys[i], out var value).Should().BeTrue();
                    value.Should().Be(i + 1);
                }
            }
        }
    }
}
