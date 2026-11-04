using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// A range of stack addresses in which <see cref="RuntimeHelpers.EnsureSufficientExecutionStack"/> can't throw, so a
    /// read inside it skips that call into the runtime and a read outside it, including one on another thread, makes the
    /// call, which then decides.  CoreCLR refuses once less than 128 KB of the thread's stack is left on 64-bit (measured
    /// on .NET 10 x64: within 128 bytes of that from the caller's frame).  The range would still hold with a margin up to
    /// twice as large.
    /// </summary>
    internal static class StackLimit
    {
        private const nuint Margin = 256 * 1024;

        // The stack bounds Wine reports haven't been measured against the runtime's margin.
        internal static readonly bool UnderWine = IsWine();

        [ThreadStatic]
        private static Bounds t_bounds;

        /// <summary>
        /// A start of 0 means not yet read: an empty range starts at 1.
        /// </summary>
        internal readonly struct Bounds
        {
            internal Bounds(nuint start, nuint span)
            {
                Start = start;
                Span = span;
            }

            internal readonly nuint Start;
            internal readonly nuint Span;
        }

        // Without this the JIT doesn't inline it into Read (seen in .NET 10's x64 code), which costs a call per read.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Bounds ForCurrentThread()
        {
            var bounds = t_bounds;
            return bounds.Start != 0 ? bounds : Compute();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Ensure(nuint start, nuint span)
        {
            byte probe = 0;
            if (AddressOf(ref probe) - start >= span)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static nuint AddressOf(ref byte local) => (nuint)Unsafe.ByteOffset(ref Unsafe.NullRef<byte>(), ref local);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Bounds Compute()
        {
            var bounds = new Bounds(1, 0);
            if (OperatingSystem.IsWindows() && Environment.Is64BitProcess && !UnderWine)
            {
                try
                {
                    GetCurrentThreadStackLimits(out var low, out var high);
                    var start = low + Margin;
                    bounds = new Bounds(start, high > start ? high - start : 0);
                }
                // A Wine that hides its exports (wine-staging's HideWineExports) or a kernel32 without this export.
                catch (EntryPointNotFoundException)
                {
                }
                catch (DllNotFoundException)
                {
                }
            }

            t_bounds = bounds;
            return bounds;
        }

        private static bool IsWine() =>
            OperatingSystem.IsWindows() && NativeLibrary.TryLoad("ntdll.dll", out var ntdll) && NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);

        [DllImport("kernel32.dll")]
        private static extern void GetCurrentThreadStackLimits(out nuint lowLimit, out nuint highLimit);
    }
}
