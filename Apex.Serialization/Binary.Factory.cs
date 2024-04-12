using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization
{
    public static class Binary
    {
        internal class CustomSerializerDelegate : IEquatable<CustomSerializerDelegate>
        {
            public readonly Delegate? Action;
            public readonly MethodInfo? MethodInfo;
            public readonly Type? CustomContextType;

            public CustomSerializerDelegate(Delegate action, Type? customContextType)
            {
                Action = action;
                CustomContextType = customContextType;
            }

            public CustomSerializerDelegate(MethodInfo methodInfo, Type? customContextType)
            {
                MethodInfo = methodInfo;
                CustomContextType = customContextType;
            }

            public override bool Equals(object? obj)
            {
                return Equals(obj as CustomSerializerDelegate);
            }

            public bool Equals(CustomSerializerDelegate? other)
            {
                return other != null &&
                       EqualityComparer<Delegate?>.Default.Equals(Action, other.Action) &&
                       EqualityComparer<MethodInfo?>.Default.Equals(MethodInfo, other.MethodInfo) &&
                       EqualityComparer<Type?>.Default.Equals(CustomContextType, other.CustomContextType);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Action, CustomContextType);
            }
        }

        public static IBinary Create(Settings settings)
        {
            var immutableSettings = settings.ToImmutable();
            var generatedType = immutableSettings.GetGeneratedType();
            var concreteBinaryType = typeof(Binary<,>).MakeGenericType(typeof(BufferedStream), generatedType);

            return (IBinary) (Activator.CreateInstance(
                concreteBinaryType,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null,
                new object[] { immutableSettings, BufferedStream.Create() },
                null
                ) ?? throw new InvalidOperationException("Failed to create an instance of the Binary class"));
        }

        internal static ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Sets the Byte array pool that binary serializes will use.
        /// </summary>
        /// <param name="arrayPool"></param>
        public static void SetByteArrayPool(ArrayPool<byte> arrayPool)
        {
            ByteArrayPool = arrayPool;
        }
    }
}
