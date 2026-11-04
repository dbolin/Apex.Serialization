using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Apex.Serialization.Internal
{
    internal static class ThrowHelper
    {
        // The name comes from the stream and can be a megabyte long.
        private const int MaxNameInMessage = 512;

        [DoesNotReturn]
        internal static void ThrowEndOfStream()
        {
            throw new EndOfStreamException("The stream ended before the serialized data did");
        }

        [DoesNotReturn]
        internal static void ThrowInvalidLength(string what, long length)
        {
            throw new InvalidDataException($"The serialized data holds an invalid {what}: {length}");
        }

        [DoesNotReturn]
        internal static void ThrowInvalidReference(string what, int index)
        {
            throw new InvalidDataException($"The serialized data refers to {what} {index}, which hasn't been read");
        }

        [DoesNotReturn]
        internal static Delegate ThrowInvalidDelegate(string reason, Exception? cause = null)
        {
            throw new InvalidDataException($"The serialized data holds a delegate that can't be read: {reason}", cause);
        }

        [DoesNotReturn]
        internal static void ThrowDelegateToDynamicMethod(MethodInfo method)
        {
            throw new NotSupportedException($"Can't write a delegate to the dynamic method {method.Name}: it has no declaring type for the reader to find it in");
        }

        [DoesNotReturn]
        internal static void ThrowImpossibleObjectType(Type type)
        {
            throw new InvalidDataException($"The serialized data holds an object of type {type.FullName}, which isn't a type an object can be read as");
        }

        [DoesNotReturn]
        internal static void ThrowDelegateMethodNotAllowed(MethodInfo method)
        {
            var declaringType = method.DeclaringType!;
            var typeArguments = method.IsGenericMethod
                ? $" over {string.Join(", ", method.GetGenericArguments().Select(x => $"{x.FullName ?? x.Name} ({x.Assembly.GetName().Name})"))}"
                : "";
            throw new InvalidOperationException($"Can't write a delegate to {declaringType.FullName}.{method.Name}{typeArguments}, declared in {declaringType.Assembly.GetName().Name}: "
                + "the settings don't allow delegate methods from that assembly, or generic methods over types from outside the allowed ones");
        }

        [DoesNotReturn]
        internal static void ThrowTooLargeToCheck(long elementCount)
        {
            throw new NotSupportedException($"An array of {elementCount} elements is too large to check against a stream that can't seek");
        }

        /// <summary>
        /// The exception the span constructor these checks replace throws for a position outside the buffer:
        /// <c>MalformedInputTests</c> counts one as a read that got past every check on the data, so a different type would
        /// hide that from it.
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowOutsideBuffer()
        {
            throw new ArgumentOutOfRangeException(null, "A read or write went past the end of the stream buffer");
        }

        [DoesNotReturn]
        internal static void ThrowReservationTooLarge(int size)
        {
            throw new InvalidOperationException($"A single value of {size} bytes doesn't fit the {BufferedStream.MaxSize}-byte stream buffer");
        }

        [DoesNotReturn]
        internal static Type ThrowUnresolvableType(string name)
        {
            throw new InvalidOperationException($"Unable to resolve type: {(name.Length > MaxNameInMessage ? name.Substring(0, MaxNameInMessage) + "..." : name)}");
        }
    }
}
