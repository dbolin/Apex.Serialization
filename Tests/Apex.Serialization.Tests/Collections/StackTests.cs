using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class StackTests : AbstractSerializerTestBase
    {
        [Fact]
        public void StackObject()
        {
            var x = new Stack<object>();
            x.Push(1);
            x.Push("12");
            x.Push(null);
            x.Push(new object());
        }
    }
}
