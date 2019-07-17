using Apex.Serialization.Extensions;
using System;
using System.Collections.Generic;
using System.Reflection;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization
{
    public static class Binary
    {
        internal static bool Instantiated;
        internal class CustomSerializerDelegate
        {
            public Delegate Action;
            public MethodInfo InvokeMethodInfo;
            public Type CustomContextType;
        }

        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>();
        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>();


        public static IBinary Create()
        {
            Instantiated = true;
            return new Binary<BufferedStream>(BufferedStream.Create());
        }

        public static IBinary Create(Settings settings)
        {
            Instantiated = true;
            return new Binary<BufferedStream>(settings, BufferedStream.Create());
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// This cannot be done after an instance of the Binary class has been create.
        /// This method is not thread-safe.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public static void RegisterCustomSerializer<T>(Action<T, IBinaryWriter> writeMethod, Action<T, IBinaryReader> readMethod)
        {
            Check();
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate
            {
                Action = writeMethod,
                InvokeMethodInfo = typeof(Action<T, IBinaryWriter>).GetMethod("Invoke")
            });
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate
            {
                Action = readMethod,
                InvokeMethodInfo = typeof(Action<T, IBinaryReader>).GetMethod("Invoke")
            });
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// This cannot be done after an instance of the Binary class has been create.
        /// This method is not thread-safe.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <typeparam name="TContext">Type of custom serialization context.  Will be null if the current context is not set or cannot be cast to this type.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public static void RegisterCustomSerializer<T, TContext>(Action<T, IBinaryWriter, TContext> writeMethod, Action<T, IBinaryReader, TContext> readMethod)
            where TContext : class
        {
            Check();
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate
            {
                Action = writeMethod,
                InvokeMethodInfo = typeof(Action<T, IBinaryWriter, TContext>).GetMethod("Invoke"),
                CustomContextType = typeof(TContext)
            });
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate
            {
                Action = readMethod,
                InvokeMethodInfo = typeof(Action<T, IBinaryReader, TContext>).GetMethod("Invoke"),
                CustomContextType = typeof(TContext)
            });
        }

        private static void Check()
        {
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot register custom serializers after an instance of a Binary serializer has been created");
            }
        }
    }
}
