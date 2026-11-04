using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// A stack address above which <see cref="RuntimeHelpers.EnsureSufficientExecutionStack"/> can't throw, so a read
    /// above it skips that call into the runtime and a read below it makes the call, which then decides.  CoreCLR
    /// refuses once less than 128 KB of the thread's stack is left on 64-bit (measured on .NET 10 x64: within 128 bytes
    /// of that from the caller's frame).  The limit sits 256 KB above the stack's low end, so the runtime still decides
    /// every read that gets near its margin, and would with a margin up to twice as large.  Where the stack's bounds
    /// aren't read (anything but 64-bit Windows) the limit is the top of the address space, and every read makes the call.
    /// </summary>
    internal static class StackLimit
    {
        internal static readonly nuint Unknown = nuint.MaxValue;

        private const nuint Margin = 256 * 1024;

        [ThreadStatic]
        private static nuint t_limit;

        internal static nuint ForCurrentThread()
        {
            var limit = t_limit;
            return limit != 0 ? limit : Compute();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static nuint AddressOf(ref byte local) => (nuint)Unsafe.ByteOffset(ref Unsafe.NullRef<byte>(), ref local);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void CheckWithRuntime() => RuntimeHelpers.EnsureSufficientExecutionStack();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static nuint Compute()
        {
            var limit = Unknown;
            if (OperatingSystem.IsWindows() && Environment.Is64BitProcess)
            {
                GetCurrentThreadStackLimits(out var low, out var high);
                if (high - low > Margin)
                {
                    limit = low + Margin;
                }
            }

            t_limit = limit;
            return limit;
        }

        [DllImport("kernel32.dll")]
        private static extern void GetCurrentThreadStackLimits(out nuint lowLimit, out nuint highLimit);
    }
}
