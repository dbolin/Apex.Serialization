using System.Reflection;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal
{
    internal static class InlineArrayElement
    {
        internal static readonly MethodInfo GetMethod = typeof(InlineArrayElement).GetMethod(nameof(Get), BindingFlags.Static | BindingFlags.NonPublic)!;
        internal static readonly MethodInfo SetMethod = typeof(InlineArrayElement).GetMethod(nameof(Set), BindingFlags.Static | BindingFlags.NonPublic)!;

        internal static TElement Get<TArray, TElement>(ref TArray array, int index)
            where TArray : struct
        {
            return Unsafe.Add(ref Unsafe.As<TArray, TElement>(ref array), index);
        }

        internal static void Set<TArray, TElement>(ref TArray array, int index, TElement value)
            where TArray : struct
        {
            Unsafe.Add(ref Unsafe.As<TArray, TElement>(ref array), index) = value;
        }
    }
}
