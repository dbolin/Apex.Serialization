using Apex.Serialization.Extensions;
using Apex.Serialization.Internal.Reflection;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization
{
    public static class Binary
    {
#if !DEBUG
        internal static bool Instantiated;
#endif
        internal class CustomSerializerDelegate
        {
            public readonly Delegate Action;
            public readonly MethodInfo InvokeMethodInfo;
            public readonly Type? CustomContextType;

            public CustomSerializerDelegate(Delegate action, MethodInfo invokeMethodInfo, Type? customContextType)
            {
                Action = action;
                InvokeMethodInfo = invokeMethodInfo;
                CustomContextType = customContextType;
            }
        }

        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>();
        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>();

        internal static HashSet<Type> WhitelistedTypes = new HashSet<Type>();
        internal static List<Func<Type, bool>> WhitelistFuncs = new List<Func<Type, bool>>();

        public static IBinary Create()
        {
#if !DEBUG
            Instantiated = true;
#endif
            return new Binary<BufferedStream>(BufferedStream.Create());
        }

        public static IBinary Create(Settings settings)
        {
#if !DEBUG
            Instantiated = true;
#endif
            return new Binary<BufferedStream>(settings, BufferedStream.Create());
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// This cannot be done after an instance of the Binary class has been created.
        /// This method is not thread-safe.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public static void RegisterCustomSerializer<T>(Action<T, IBinaryWriter> writeMethod, Action<T, IBinaryReader> readMethod)
        {
            CheckInstantiantedCustomSerializer();
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate(
                writeMethod,
                typeof(Action<T, IBinaryWriter>).GetMethod("Invoke")!,
                null
                ));
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate(
                readMethod,
                typeof(Action<T, IBinaryReader>).GetMethod("Invoke")!,
                null));
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// This cannot be done after an instance of the Binary class has been created.
        /// This method is not thread-safe.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <typeparam name="TContext">Type of custom serialization context.  Will be null if the current context is not set or cannot be cast to this type.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public static void RegisterCustomSerializer<T, TContext>(Action<T, IBinaryWriter, TContext> writeMethod, Action<T, IBinaryReader, TContext> readMethod)
            where TContext : class
        {
            CheckInstantiantedCustomSerializer();
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate(
                writeMethod,
                typeof(Action<T, IBinaryWriter, TContext>).GetMethod("Invoke")!,
                typeof(TContext)
                ));
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate(
                readMethod,
                typeof(Action<T, IBinaryReader, TContext>).GetMethod("Invoke")!,
                typeof(TContext)
                ));
        }

        internal static ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Sets the Byte array pool that binary serializes will use.
        /// Cannot be set after an instance of the Binary class has been created.
        /// This method is not thread-safe.
        /// </summary>
        /// <param name="arrayPool"></param>
        public static void SetByteArrayPool(ArrayPool<byte> arrayPool)
        {
            CheckInstantiantedArrayPool();
            ByteArrayPool = arrayPool;
        }

        /// <summary>
        /// Marks a type as able to be serialized.
        /// Cannot be done after an instance of the Binary class has been created.
        /// This method is not thread-safe.
        /// </summary>
        /// <param name="type">The type to mark as serializable</param>
        public static void MarkSerializable(Type type)
        {
#if !DEBUG
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot mark types as serializable after an instance of a Binary serializer has been created");
            }
#endif
            WhitelistedTypes.Add(type);
        }

        /// <summary>
        /// Marks types as serializable according to a predicate.
        /// Cannot be done after an instance of the Binary class has been created.
        /// This method is not thread-safe.
        /// </summary>
        /// <param name="type">The predicate function to determine whether a type can be serialized</param>
        public static void MarkSerializable(Func<Type, bool> isTypeSerializable)
        {
#if !DEBUG
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot mark types as serializable after an instance of a Binary serializer has been created");
            }
#endif
            WhitelistFuncs.Add(isTypeSerializable);
        }

        // For internal testing
        internal static void ClearSerializableMarks()
        {
            WhitelistedTypes.Clear();
            WhitelistFuncs.Clear();
        }

        private static HashSet<Type> _autoWhitelistedTypes = new HashSet<Type>
        {
            typeof(string),
            typeof(object),
            typeof(KeyValuePair<,>),
            typeof(Tuple<>),
            typeof(Tuple<,>),
            typeof(Tuple<,,>),
            typeof(Tuple<,,,>),
            typeof(Tuple<,,,,>),
            typeof(Tuple<,,,,,>),
            typeof(Tuple<,,,,,,>),
            typeof(Tuple<,,,,,,,>),
            typeof(ValueTuple<>),
            typeof(ValueTuple<,>),
            typeof(ValueTuple<,,>),
            typeof(ValueTuple<,,,>),
            typeof(ValueTuple<,,,,>),
            typeof(ValueTuple<,,,,,>),
            typeof(ValueTuple<,,,,,,>),
            typeof(ValueTuple<,,,,,,,>),
        };

        internal static bool IsTypeSerializable(Type type)
        {
            if(type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                type = type.GetGenericTypeDefinition();
                if (_autoWhitelistedTypes.Contains(type))
                {
                    return true;
                }
            }

            if (type == typeof(FieldInfoModifier.TestReadonly))
            {
                return true;
            }

            if(type.IsArray)
            {
                return true;
            }

            if(typeof(Delegate).IsAssignableFrom(type))
            {
                return true;
            }

            if(typeof(Type).IsAssignableFrom(type))
            {
                return true;
            }

            if(type.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) != null)
            {
                return true;
            }

            if(TypeFields.IsPrimitive(type))
            {
                return true;
            }

            if (IsSpecialCoreType(type))
            {
                return true;
            }

            if (_autoWhitelistedTypes.Contains(type))
            {
                return true;
            }

            var declaringTypeIsSerializeable = type.DeclaringType != null && IsTypeSerializable(type.DeclaringType);

            return declaringTypeIsSerializeable
                || WhitelistedTypes.Contains(type) 
                || WhitelistFuncs.Any(x => x(type));
        }

        private static bool IsSpecialCoreType(Type type)
        {
            if (
                (
                    type.Assembly == typeof(List<>).Assembly
                    || type.Assembly == typeof(Queue<>).Assembly
                    || type.Assembly == typeof(ImmutableList<>).Assembly
                )
                && 
                (type.Namespace == "System.Collections.Generic"
                || type.Namespace == "System.Collections.Immutable")
                && (!type.IsPublic))
            {
                return true;
            }

            if (type.BaseType != null
                && type.BaseType.IsGenericType
                && type.BaseType.GetGenericTypeDefinition() == typeof(EqualityComparer<>)
                && typeof(EqualityComparer<>).Assembly == type.Assembly)
            {
                return true;
            }

            if (type.BaseType != null
                && type.BaseType.IsGenericType
                && type.BaseType.GetGenericTypeDefinition() == typeof(Comparer<>)
                && typeof(Comparer<>).Assembly == type.Assembly)
            {
                return true;
            }

            if(type.BaseType != null && IsTypeSerializable(type.BaseType))
            {
                return true;
            }

            if (type == typeof(SerializationInfo))
            {
                return true;
            }

            return false;
        }

        private static void CheckInstantiantedCustomSerializer()
        {
#if !DEBUG
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot register custom serializers after an instance of a Binary serializer has been created");
            }
#endif
        }

        private static void CheckInstantiantedArrayPool()
        {
#if !DEBUG
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot set array pool after an instance of a Binary serializer has been created");
            }
#endif
        }
    }
}
