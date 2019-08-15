using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class EventTests :  AbstractSerializerTestBase
    {
        public class Test
        {
            public delegate void EventHandler(int v);

            public event EventHandler? OnEvent;

            public void RaiseEvent()
            {
                OnEvent?.Invoke(1);
            }

            public int Value;
        }


        [Fact]
        public void Events()
        {
            var x = new Test();

            x.OnEvent += i => x.Value++;
            x.OnEvent += i => x.Value *= 2;

            x.RaiseEvent();
            x.Value.Should().Be(2);

            // closure target has a reference to the event
            x = RoundTripGraphOnly(x);

            x.RaiseEvent();
            x.Value.Should().Be(6);
        }
    }
}
