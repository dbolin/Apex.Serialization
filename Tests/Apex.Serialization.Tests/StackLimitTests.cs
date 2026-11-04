using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Threading;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// The stack check skips the runtime's call only inside the reading thread's own range of stack addresses, so each
    /// thread has to get the bounds of its own stack.
    /// </summary>
    public class StackLimitTests
    {
        [Fact]
        public void EachThreadGetsTheBoundsOfItsOwnStack()
        {
            using var bothRead = new Barrier(2);
            var readSmall = StartThread(1024 * 1024, bothRead);
            var readLarge = StartThread(16 * 1024 * 1024, bothRead);
            var small = readSmall();
            var large = readLarge();

            if (OperatingSystem.IsWindows() && Environment.Is64BitProcess && !StackLimit.UnderWine)
            {
                Inside(small.Address, small.Bounds).Should().BeTrue();
                Inside(large.Address, large.Bounds).Should().BeTrue();
                Inside(small.Address, large.Bounds).Should().BeFalse();
                Inside(large.Address, small.Bounds).Should().BeFalse();
                large.Bounds.Span.Should().BeGreaterThan(small.Bounds.Span);
            }
            else
            {
                small.Bounds.Span.Should().Be(0);
                large.Bounds.Span.Should().Be(0);
            }
        }

        private static bool Inside(nuint address, StackLimit.Bounds bounds) => address - bounds.Start < bounds.Span;

        private static Func<(StackLimit.Bounds Bounds, nuint Address)> StartThread(int stackBytes, Barrier bothRead)
        {
            (StackLimit.Bounds, nuint) seen = default;
            var thread = new Thread(() =>
            {
                byte local = 0;
                seen = (StackLimit.ForCurrentThread(), StackLimit.AddressOf(ref local));
                // Both stacks stay allocated until both are read, so neither thread can get addresses the other used.
                bothRead.SignalAndWait();
            }, stackBytes);
            thread.Start();
            return () =>
            {
                thread.Join();
                return seen;
            };
        }
    }
}
