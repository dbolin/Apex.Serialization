using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apex.Serialization.Extensions;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class CustomSerializationTests
    {
        public class Test
        {
            public int Value;

            public static void Serialize(Test t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
            }

            public static void Deserialize(Test t, IBinaryReader reader)
            {
                t.Value = reader.Read<int>();
            }
        }

        [Fact]
        public void SimpleTest()
        {
            Binary.Instantiated = false;
            Binary.RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);

            var binary = new Binary(new Settings {SupportSerializationHooks = true});
            var m = new MemoryStream();

            var x = new Test {Value = 10};

            binary.Write(x, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<Test>(m);

            y.Value.Should().Be(x.Value - 1);
        }

        [Fact]
        public void Precompile()
        {
            var binary = new Binary(new Settings { SupportSerializationHooks = true });

            binary.Precompile(typeof(Settings));
            binary.Precompile<Settings>();
            binary.Precompile<CustomSerializationTests>();
        }
    }
}
