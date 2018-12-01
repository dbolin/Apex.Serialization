using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apex.Serialization.Tests.Collections.Objects
{
    public struct RandomHashcode : IEquatable<RandomHashcode>, IComparable<RandomHashcode>
    {
        public static int HashCodeRandomizer = 0x12341234;

        public int Value;

        public override bool Equals(object obj) => obj is RandomHashcode && Equals((RandomHashcode)obj);
        public bool Equals(RandomHashcode other) => Value == other.Value;

        public override int GetHashCode()
        {
            return HashCodeRandomizer + Value.GetHashCode();
        }

        public static bool operator ==(RandomHashcode hashcode1, RandomHashcode hashcode2) => hashcode1.Equals(hashcode2);
        public static bool operator !=(RandomHashcode hashcode1, RandomHashcode hashcode2) => !(hashcode1 == hashcode2);

        private static Random _random = new Random();

        internal static void NewRandomizer()
        {
            var old = HashCodeRandomizer;

            while (HashCodeRandomizer == old)
            {
                HashCodeRandomizer = _random.Next();
            }
        }

        public int CompareTo(RandomHashcode other) => Value.CompareTo(other.Value);
    }
}
