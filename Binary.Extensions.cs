using System;
using System.Collections.Generic;
using System.Reflection;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;

namespace Apex.Serialization
{
    public partial class Binary
    {
        internal class CustomSerializerDelegate
        {
            public Delegate Action;
            public MethodInfo InvokeMethodInfo;
        }

        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>();
        internal static Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>();

        IBinaryWriter ISerializer.BinaryWriter => _binaryWriter;
        IBinaryReader ISerializer.BinaryReader => _binaryReader;
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

        private static void Check()
        {
            if (Instantiated)
            {
                throw new InvalidOperationException("Cannot register custom serializers after an instance of the Binary class has been created");
            }
        }
    }
}
