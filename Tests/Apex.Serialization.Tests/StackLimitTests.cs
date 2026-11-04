using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Threading;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// The stack check skips the runtime's call only inside the reading thread's own range of stack addresses, so each
    /// thread has to get the bounds of its own stack.  Only native 64-bit Windows reads the bounds; elsewhere every thread's
    /// range is empty, which is all this can check there.
    /// </summary>
    public class StackLimitTests
    {
        [Fact]
        public void EachThreadGetsTheBoundsOfItsOwnStack()
        {
            var small = OnThread(1024 * 1024);
            var large = OnThread(16 * 1024 * 1024);

            if (OperatingSystem.IsWindows() && Environment.Is64BitProcess)
            {
                (small.Address - small.Bounds.Start).Should().BeLessThan(small.Bounds.Span);
                (large.Address - large.Bounds.Start).Should().BeLessThan(large.Bounds.Span);
                large.Bounds.Span.Should().BeGreaterThan(small.Bounds.Span);
            }
            else
            {
                small.Bounds.Span.Should().Be(0);
                large.Bounds.Span.Should().Be(0);
            }
        }

        private static (StackLimit.Bounds Bounds, nuint Address) OnThread(int stackBytes)
        {
            (StackLimit.Bounds, nuint) seen = default;
            var thread = new Thread(() =>
            {
                byte local = 0;
                seen = (StackLimit.ForCurrentThread(), StackLimit.AddressOf(ref local));
            }, stackBytes);
            thread.Start();
            thread.Join();
            return seen;
        }
    }
}
