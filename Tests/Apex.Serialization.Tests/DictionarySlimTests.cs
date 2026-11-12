using System;
using System.Linq;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class DictionarySlimTests
    {
        private const int DistinctHashCodes = 4;

        private sealed class Key
        {
            private readonly int _hash;
            private readonly bool _equalsEveryKey;

            public Key(int hash, bool equalsEveryKey = false)
            {
                _hash = hash;
                _equalsEveryKey = equalsEveryKey;
            }

            public override int GetHashCode() => _hash;

            public override bool Equals(object? obj) => _equalsEveryKey || ReferenceEquals(this, obj);
        }

        private static int SharedByManyKeys(int index) => index % DistinctHashCodes;

        private static int MovingToAnotherBucketAsTheTableGrows(int index) => unchecked((index + 1) * (int)2654435761);

        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 40)]
        [InlineData(0, 400)]
        [InlineData(1, 1)]
        [InlineData(3, 40)]
        [InlineData(3, 400)]
        [InlineData(40, 0)]
        [InlineData(63, 2)]
        [InlineData(250, 7)]
        public void TruncatingLeavesTheFirstEntriesAndTakesNewOnes(int kept, int removed)
        {
            foreach (var hashCodeOf in new Func<int, int>[] { SharedByManyKeys, MovingToAnotherBucketAsTheTableGrows })
            {
                var keys = Enumerable.Range(0, kept + removed).Select(i => new Key(hashCodeOf(i))).ToArray();
                var allValues = Enumerable.Range(1, keys.Length).ToArray();
                var keptValues = allValues.Select(value => value <= kept ? value : 0).ToArray();

                for (var capacity = 2; capacity <= 8192; capacity *= 2)
                {
                    var dictionary = new DictionarySlim<Key, int>(capacity);
                    Add(dictionary, keys, from: 0, to: keys.Length);

                    for (var pass = 0; pass < 2; ++pass)
                    {
                        var where = $"hash codes {hashCodeOf.Method.Name}, capacity {capacity}, pass {pass}";
                        dictionary.TruncateTo(kept);

                        dictionary.Count.Should().Be(kept, where);
                        ValuesOf(dictionary, keys).Should().Equal(keptValues, where);

                        ValuesHeldBeforeAdding(dictionary, keys, from: kept).Should().Equal(new int[removed], where);

                        dictionary.Count.Should().Be(keys.Length, where);
                        ValuesOf(dictionary, keys).Should().Equal(allValues, where);
                    }
                }
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 40)]
        [InlineData(3, 400)]
        [InlineData(7, 2)]
        [InlineData(15, 2)]
        [InlineData(31, 2)]
        [InlineData(63, 2)]
        [InlineData(127, 2)]
        [InlineData(250, 7)]
        public void ATruncatedTableFindsTheKeyATableOfTheKeptKeysFinds(int kept, int removed)
        {
            var keys = Enumerable.Range(0, kept + removed).Select(i => new Key(SharedByManyKeys(i))).ToArray();
            var equalToEveryKey = Enumerable.Range(0, DistinctHashCodes).Select(hash => new Key(hash, equalsEveryKey: true)).ToArray();

            for (var capacity = 2; capacity <= 8192; capacity *= 2)
            {
                var dictionary = new DictionarySlim<Key, int>(capacity);
                Add(dictionary, keys, from: 0, to: keys.Length);

                for (var pass = 0; pass < 2; ++pass)
                {
                    dictionary.TruncateTo(kept);

                    var ofTheKeptKeys = new DictionarySlim<Key, int>(dictionary.Capacity);
                    Add(ofTheKeptKeys, keys, from: 0, to: kept);
                    ValuesOf(dictionary, equalToEveryKey).Should().Equal(ValuesOf(ofTheKeptKeys, equalToEveryKey), "capacity {0}, pass {1}", capacity, pass);

                    Add(dictionary, keys, from: kept, to: keys.Length);
                }
            }
        }

        private static void Add(DictionarySlim<Key, int> dictionary, Key[] keys, int from, int to)
        {
            for (var i = from; i < to; ++i)
            {
                dictionary.GetOrAddValueRef(keys[i]) = i + 1;
            }
        }

        private static int[] ValuesHeldBeforeAdding(DictionarySlim<Key, int> dictionary, Key[] keys, int from)
        {
            var before = new int[keys.Length - from];
            for (var i = from; i < keys.Length; ++i)
            {
                ref var value = ref dictionary.GetOrAddValueRef(keys[i]);
                before[i - from] = value;
                value = i + 1;
            }

            return before;
        }

        private static int[] ValuesOf(DictionarySlim<Key, int> dictionary, Key[] keys)
        {
            return keys.Select(key => dictionary.TryGetValue(key, out var value) ? value : 0).ToArray();
        }
    }
}
