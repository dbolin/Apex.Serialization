using Apex.Serialization.Extensions;
using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using static Apex.Serialization.Binary;

namespace Apex.Serialization
{
    public enum Mode
    {
        Tree = 0,
        Graph = 1
    }

    public sealed class Settings
    {
        public Mode SerializationMode { get; set; }

        public bool AllowFunctionSerialization { get; set; }

        public bool SupportSerializationHooks { get; set; }

        public int InliningMaxDepth { get; set; } = 10;

        public bool UseSerializedVersionId { get; set; } = true;

        public bool FlattenClassHierarchy { get; set; } = true;

        internal bool ForceReflectionToSetReadonlyFields { get; set; }

        private readonly Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>();
        private readonly Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>();
        private readonly Dictionary<Type, CustomSerializerDelegate> CustomActionInstantiators = new Dictionary<Type, CustomSerializerDelegate>();

        private readonly HashSet<Type> WhitelistedTypes = new HashSet<Type>();
        private readonly List<Func<Type, bool>> WhitelistFuncs = new List<Func<Type, bool>>();

        /// <summary>
        /// Registers a custom serializer action.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        /// <param name="readMethod">Method to be called when a type matching T is to be deserialized.</param>
        public Settings RegisterCustomSerializer<T>(Action<T, IBinaryWriter> writeMethod, Action<T, IBinaryReader> readMethod)
        {
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate(
                writeMethod,
                null
                ));
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate(
                readMethod,
                null));
            return this;
        }

        /// <summary>
        /// Registers a custom serializer action that applies to an open generic type.
        /// </summary>
        /// <param name="type">The open generic type to be directed to the custom serialization functions.</param>
        /// <param name="serializerClass">The type of the class which declares the serialization functions. The methods must static.</param>
        /// <param name="writeMethod">The name of the generic method to be used to write the objects.</param>
        /// <param name="readMethod">The name of the generic method to be used to read the objects.</param>
        /// <param name="customContextType">The type of the custom context, if any.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public Settings RegisterCustomSerializer(Type type, Type serializerClass, string writeMethod, string readMethod, Type? customContextType = null)
        {
            var expectedLength = customContextType != null ? 3 : 2;

            var writeMethodInfo = serializerClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SingleOrDefault(x => x.Name == writeMethod
                && x.IsGenericMethodDefinition && x.GetParameters().Length == expectedLength
                && (expectedLength == 2 || x.GetParameters()[2].ParameterType == customContextType)
                && x.GetParameters()[1].ParameterType == typeof(IBinaryWriter)
                && x.GetParameters()[0].ParameterType == type.MakeGenericType(x.GetGenericArguments()));

            if (writeMethodInfo == null)
            {
                throw new ArgumentException($"Unable to find method '{writeMethod}' on type '{serializerClass.Name}' with appropriate parameter types");
            }

            var readMethodInfo = serializerClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SingleOrDefault(x => x.Name == readMethod
                && x.IsGenericMethodDefinition && x.GetParameters().Length == expectedLength
                && (expectedLength == 2 || x.GetParameters()[2].ParameterType == customContextType)
                && x.GetParameters()[1].ParameterType == typeof(IBinaryReader)
                && x.GetParameters()[0].ParameterType == type.MakeGenericType(x.GetGenericArguments()));

            if (readMethodInfo == null)
            {
                throw new ArgumentException($"Unable to find method '{readMethod}' on type '{serializerClass.Name}' with appropriate parameter types");
            }

            CustomActionSerializers.Add(type, new CustomSerializerDelegate(writeMethodInfo, customContextType));
            CustomActionDeserializers.Add(type, new CustomSerializerDelegate(readMethodInfo, customContextType));
            return this;
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <typeparam name="TContext">Type of custom serialization context.  Will be null if the current context is not set or cannot be cast to this type.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        /// <param name="readMethod">Method to be called when a type matching T is to be deserialized.</param>
        public Settings RegisterCustomSerializer<T, TContext>(Action<T, IBinaryWriter, TContext> writeMethod, Action<T, IBinaryReader, TContext> readMethod)
            where TContext : class
        {
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate(
                writeMethod,
                typeof(TContext)
                ));
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate(
                readMethod,
                typeof(TContext)
                ));
            return this;
        }

        /// <summary>
        /// Registers a custom function to instantiate objects prior to deserialization. Reading a non-value type within this function in graph mode will throw an InvalidOperationException as this could result in corrupting the object graph.
        /// </summary>
        /// <typeparam name="T">Type to which the custom instantiation will apply.  Does not support primitives.</typeparam>
        /// <param name="instantiationFunc">Method to be called when a type matching T is to be instantiated.</param>
        /// <returns></returns>
        public Settings RegisterCustomInstantiator<T>(Func<IBinaryReader, T> instantiationFunc)
        {
            CustomActionInstantiators.Add(typeof(T), new CustomSerializerDelegate(instantiationFunc, null));
            return this;
        }

        /// <summary>
        /// Registers a custom function to instantiate objects prior to deserialization. Reading a non-value type within this function in graph mode will throw an InvalidOperationException as this could result in corrupting the object graph.
        /// </summary>
        /// <param name="type">The open generic type to be directed to the custom instantiation function.</param>
        /// <param name="serializerClass">The type of the class which declares the instantiation function. The methods must static.</param>
        /// <param name="methodName">The name of the generic method to be used to instantiate the objects.</param>
        /// <param name="customContextType">The type of the custom context, if any.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public Settings RegisterCustomInstantiator(Type type, Type serializerClass, string methodName, Type? customContextType = null)
        {
            var methodInfo = serializerClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SingleOrDefault(x => x.Name == methodName
                && x.IsGenericMethodDefinition && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType == typeof(IBinaryReader)
                && x.ReturnType == type.MakeGenericType(x.GetGenericArguments()));

            if (methodInfo == null)
            {
                throw new ArgumentException($"Unable to find method '{methodName}' on type '{serializerClass.Name}' with appropriate parameter types");
            }

            CustomActionInstantiators.Add(type, new CustomSerializerDelegate(methodInfo, customContextType));
            return this;
        }

        /// <summary>
        /// Marks a type as able to be serialized.
        /// </summary>
        /// <param name="type">The type to mark as serializable</param>
        public Settings MarkSerializable(Type type)
        {
            WhitelistedTypes.Add(type);
            if (!FlattenClassHierarchy && type.BaseType != null)
            {
                MarkSerializable(type.BaseType);
            }
            return this;
        }

        /// <summary>
        /// Marks types as serializable according to a predicate.
        /// </summary>
        /// <param name="type">The predicate function to determine whether a type can be serialized</param>
        public Settings MarkSerializable(Func<Type, bool> isTypeSerializable)
        {
            WhitelistFuncs.Add(isTypeSerializable);
            return this;
        }

        private static readonly Dictionary<ImmutableSettings, ImmutableSettings> _constructedSettings
            = new Dictionary<ImmutableSettings, ImmutableSettings>(new ImmutableSettingsDeduplicator());
        private static readonly object _constructedSettingsLock = new object();

        internal ImmutableSettings ToImmutable()
        {
            var result = new ImmutableSettings(
                SerializationMode,
                AllowFunctionSerialization,
                SupportSerializationHooks,
                InliningMaxDepth,
                UseSerializedVersionId,
                FlattenClassHierarchy,
                ForceReflectionToSetReadonlyFields,
                CustomActionSerializers,
                CustomActionDeserializers,
                CustomActionInstantiators,
                WhitelistedTypes,
                WhitelistFuncs);
            lock (_constructedSettingsLock)
            {
                if(_constructedSettings.TryGetValue(result, out var previousConstructed))
                {
                    return previousConstructed;
                }
                _constructedSettings.Add(result, result);
            }

            return result;
        }
    }

    internal sealed class ImmutableSettings : IEquatable<ImmutableSettings>
    {
        public Mode SerializationMode { get; }
        public bool AllowFunctionSerialization { get; }
        public bool SupportSerializationHooks { get; }
        public bool UseSerializedVersionId { get; }
        public bool FlattenClassHierarchy { get; }
        public Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers { get; }
        public Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers { get; }
        public Dictionary<Type, CustomSerializerDelegate> CustomActionInstantiators { get; }
        public HashSet<Type> WhitelistedTypes { get; }
        public List<Func<Type, bool>> WhitelistFuncs { get; }
        public bool UseConstructors { get; } = true;
        public int InliningMaxDepth { get; }

        internal bool ForceReflectionToSetReadonlyFields { get; }

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

        internal bool IsTypeSerializable(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
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

            if (type.IsArray)
            {
                return true;
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                return true;
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                return true;
            }

            if (type.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) != null)
            {
                return true;
            }

            if (TypeFields.IsPrimitive(type))
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

        private bool IsSpecialCoreType(Type type)
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

            if ((type.Namespace == "System.Collections.Generic"
                || type.Namespace == "System.Collections.Immutable")
                && type.BaseType != null
                && IsTypeSerializable(type.BaseType))
            {
                return true;
            }

            if (type == typeof(SerializationInfo))
            {
                return true;
            }

            return false;
        }


        private readonly int _hashCode;

        internal ImmutableSettings(Mode serializationMode,
            bool allowFunctionSerialization,
            bool supportSerializationHooks,
            int inliningMaxDepth,
            bool useSerializedVersionId,
            bool flattenClassHierarchy,
            bool forceReflectionToSetReadonlyFields,
            Dictionary<Type, CustomSerializerDelegate> customActionSerializers,
            Dictionary<Type, CustomSerializerDelegate> customActionDeserializers,
            Dictionary<Type, CustomSerializerDelegate> customActionInstatiators,
            HashSet<Type> whitelistedTypes,
            List<Func<Type, bool>> whitelistFuncs)
        {
            SerializationMode = serializationMode;
            AllowFunctionSerialization = allowFunctionSerialization;
            SupportSerializationHooks = supportSerializationHooks;
            UseSerializedVersionId = useSerializedVersionId;
            FlattenClassHierarchy = flattenClassHierarchy;
            ForceReflectionToSetReadonlyFields = forceReflectionToSetReadonlyFields;
            InliningMaxDepth = inliningMaxDepth;

            CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>(customActionSerializers);
            CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>(customActionDeserializers);
            CustomActionInstantiators = new Dictionary<Type, CustomSerializerDelegate>(customActionInstatiators);
            WhitelistedTypes = new HashSet<Type>(whitelistedTypes);
            WhitelistFuncs = new List<Func<Type, bool>>(whitelistFuncs);

            _hashCode = HashCode.Combine(
                HashCode.Combine(SerializationMode,
                    AllowFunctionSerialization,
                    SupportSerializationHooks,
                    UseConstructors,
                    InliningMaxDepth,
                    UseSerializedVersionId,
                    FlattenClassHierarchy,
                    ForceReflectionToSetReadonlyFields),
                HashCode.Combine(CustomActionSerializers.Select(x => HashCode.Combine(x.Key, x.Value.GetHashCode())).Aggregate(0, (a,b) => HashCode.Combine(a,b))),
                HashCode.Combine(CustomActionDeserializers.Select(x => HashCode.Combine(x.Key, x.Value.GetHashCode())).Aggregate(0, (a, b) => HashCode.Combine(a, b))),
                HashCode.Combine(WhitelistedTypes.Select(x => x.GetHashCode()).Aggregate(0, (a, b) => HashCode.Combine(a, b))),
                HashCode.Combine(WhitelistFuncs.Select(x => x.GetHashCode()).Aggregate(0, (a, b) => HashCode.Combine(a, b)))
                );
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ImmutableSettings);
        }

        public bool Equals([AllowNull] ImmutableSettings other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        internal static readonly Dictionary<ImmutableSettings, Type> _generatedTypes = new Dictionary<ImmutableSettings, Type>();
        private static readonly object _generatedTypesLock = new object();

        internal Type GetGeneratedType()
        {
            lock(_generatedTypesLock)
            {
                if(!_generatedTypes.TryGetValue(this, out var result))
                {
                    result = CreateGeneratedType();
                    _generatedTypes.Add(this, result);
                }
                return result;
            }
        }

        private Type CreateGeneratedType()
        {
            var assemblyName = new AssemblyName("Apex.Serialization.Anonymous.Settings");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("Main");
            var tb = moduleBuilder.DefineType(
                $"Settings_{_hashCode}_{Guid.NewGuid()}",
                TypeAttributes.NotPublic
                | TypeAttributes.Class
                | TypeAttributes.Sealed
                );
            return tb.CreateType() ?? throw new InvalidOperationException("Failed to dynamically generate settings type");
        }
    }
}
