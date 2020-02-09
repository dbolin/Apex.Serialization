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
        public static Settings Default { get; set; } = new Settings { UseSerializedVersionId = true };

        public Mode SerializationMode { get; set; }

        public bool AllowFunctionSerialization { get; set; }

        public bool SupportSerializationHooks { get; set; }

        public bool DisableInlining { get; set; }

        public bool UseSerializedVersionId { get; set; }

        private readonly Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>();
        private readonly Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>();

        private readonly HashSet<Type> WhitelistedTypes = new HashSet<Type>();
        private readonly List<Func<Type, bool>> WhitelistFuncs = new List<Func<Type, bool>>();

        /// <summary>
        /// Registers a custom serializer action.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public Settings RegisterCustomSerializer<T>(Action<T, IBinaryWriter> writeMethod, Action<T, IBinaryReader> readMethod)
        {
            CustomActionSerializers.Add(typeof(T), new CustomSerializerDelegate(
                writeMethod,
                typeof(Action<T, IBinaryWriter>).GetMethod("Invoke")!,
                null
                ));
            CustomActionDeserializers.Add(typeof(T), new CustomSerializerDelegate(
                readMethod,
                typeof(Action<T, IBinaryReader>).GetMethod("Invoke")!,
                null));
            return this;
        }

        /// <summary>
        /// Registers a custom serializer action.
        /// </summary>
        /// <typeparam name="T">Type to which the custom serialization will apply.  Does not support primitives.</typeparam>
        /// <typeparam name="TContext">Type of custom serialization context.  Will be null if the current context is not set or cannot be cast to this type.</typeparam>
        /// <param name="writeMethod">Method to be called when a type matching T is to be serialized.</param>
        public Settings RegisterCustomSerializer<T, TContext>(Action<T, IBinaryWriter, TContext> writeMethod, Action<T, IBinaryReader, TContext> readMethod)
            where TContext : class
        {
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
            return this;
        }

        /// <summary>
        /// Marks a type as able to be serialized.
        /// </summary>
        /// <param name="type">The type to mark as serializable</param>
        public Settings MarkSerializable(Type type)
        {
            WhitelistedTypes.Add(type);
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

        internal static readonly Dictionary<ImmutableSettings, ImmutableSettings> _constructedSettings
            = new Dictionary<ImmutableSettings, ImmutableSettings>(new ImmutableSettingsDeduplicator());
        private static readonly object _constructedSettingsLock = new object();

        internal ImmutableSettings ToImmutable()
        {
            var result = new ImmutableSettings(
                SerializationMode,
                AllowFunctionSerialization,
                SupportSerializationHooks,
                DisableInlining,
                UseSerializedVersionId,
                CustomActionSerializers,
                CustomActionDeserializers,
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
        public Dictionary<Type, CustomSerializerDelegate> CustomActionSerializers { get; }
        public Dictionary<Type, CustomSerializerDelegate> CustomActionDeserializers { get; }
        public HashSet<Type> WhitelistedTypes { get; }
        public List<Func<Type, bool>> WhitelistFuncs { get; }
        public bool UseConstructors { get; } = true;
        public bool EnableInlining { get; }

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
            bool disableInlining,
            bool useSerializedVersionId,
            Dictionary<Type, CustomSerializerDelegate> customActionSerializers,
            Dictionary<Type, CustomSerializerDelegate> customActionDeserializers,
            HashSet<Type> whitelistedTypes,
            List<Func<Type, bool>> whitelistFuncs)
        {
            SerializationMode = serializationMode;
            AllowFunctionSerialization = allowFunctionSerialization;
            SupportSerializationHooks = supportSerializationHooks;
            UseSerializedVersionId = useSerializedVersionId;
            EnableInlining = !disableInlining;

            CustomActionSerializers = new Dictionary<Type, CustomSerializerDelegate>(customActionSerializers);
            CustomActionDeserializers = new Dictionary<Type, CustomSerializerDelegate>(customActionDeserializers);
            WhitelistedTypes = new HashSet<Type>(whitelistedTypes);
            WhitelistFuncs = new List<Func<Type, bool>>(whitelistFuncs);

            _hashCode = HashCode.Combine(
                HashCode.Combine(SerializationMode,
                    AllowFunctionSerialization,
                    SupportSerializationHooks,
                    UseConstructors,
                    EnableInlining,
                    UseSerializedVersionId),
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
