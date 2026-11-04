using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;
using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Serialization
{
    internal sealed partial class Binary<TStream, TSettingGen>
        where TStream : struct, IBinaryStream
    {
        internal void WriteObjectEntry<T>(T value)
        {
            if (StaticTypeInfo<T>.IsSealedOrHasNoDescendents)
            {
                if (StaticTypeInfo<T>.IsValueType)
                {
                    WriteValueInternal(value!, Settings.UseSerializedVersionId);
                }
                else
                {
                    WriteSealedInternal(value, Settings.UseSerializedVersionId);
                }
            }
            else
            {
                WriteInternal(value);
            }
        }

        /// <summary>
        /// A custom reader's nested reads can alternate between types, which a cache of the last type read would miss
        /// every time, so this one caches nothing.
        /// </summary>
        internal T ReadObjectEntry<T>()
        {
            if (typeof(T).IsValueType)
            {
                return ReadValueInternal<T>(Settings.UseSerializedVersionId);
            }

            return StaticTypeInfo<T>.IsSealedOrHasNoDescendents ? ReadSealedInternal<T>(Settings.UseSerializedVersionId) : (T)ReadInternal();
        }

        /// <summary>
        /// For a reference type the root read is shared generic code, where each static of a generic class over
        /// <typeparamref name="T"/> costs a helper call, so what it needs is cached on the instance, keyed by the type
        /// of what is cached.
        /// </summary>
        internal T ReadRoot<T>()
        {
            if (typeof(T).IsValueType)
            {
                return ReadValueInternal<T>(Settings.UseSerializedVersionId);
            }

            while (true)
            {
                var root = _rootRead;
                if (root is ReadMethods<T, TStream, TSettingGen>.ReadSealed method)
                {
                    if (ReadSealedHeader(Settings.UseSerializedVersionId, out T result))
                    {
                        return result;
                    }

                    return method(ref _stream, this);
                }

                if (root is PolymorphicRoot<T>)
                {
                    return (T)ReadInternal();
                }

                if (root is SealedRoot<T>)
                {
                    var result = ReadSealedInternal<T>(Settings.UseSerializedVersionId);
                    _rootRead = ReadMethods<T, TStream, TSettingGen>.Method ?? root;
                    return result;
                }

                _rootRead = StaticTypeInfo<T>.IsSealedOrHasNoDescendents
                    ? ReadMethods<T, TStream, TSettingGen>.Method ?? (object)SealedRoot<T>.Instance
                    : PolymorphicRoot<T>.Instance;
            }
        }

        private object? _rootRead;

        private sealed class PolymorphicRoot<T>
        {
            internal static readonly PolymorphicRoot<T> Instance = new PolymorphicRoot<T>();
        }

        /// <summary>
        /// Its read is generated after the header, so a null root of a type that can't be read still reads as null.
        /// </summary>
        private sealed class SealedRoot<T>
        {
            internal static readonly SealedRoot<T> Instance = new SealedRoot<T>();
        }

        /// <summary>
        /// Every object read starts here, so data nested deeper than the stack can hold ends in
        /// <see cref="InsufficientExecutionStackException"/> rather than a stack overflow.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureSufficientStack() => StackLimit.Ensure(_stackStart, _stackSpan);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ReadSealedHeader<T>(bool useSerializedVersionId, out T result)
        {
            EnsureSufficientStack();
            return ReadObjectRefHeader(useSerializedVersionId, out result);
        }

        internal object ReadInternal()
        {
            EnsureSufficientStack();
            if (ReadObjectRefHeader<object>(false, out var result))
            {
                return result;
            }

            var type = ReadTypeRef(Settings.UseSerializedVersionId);

            if (_lastReadType == type)
            {
                return _lastReadMethod!(ref _stream, this);
            }

            ref var method = ref VirtualReadMethods.GetOrAddValueRef(type);
            method ??= GenerateObjectRead(type);

            _lastReadType = type;
            _lastReadMethod = method;

            return method(ref _stream, this);
        }

        /// <summary>
        /// Out of line, as is <see cref="GenerateSealedRead{T}"/>: inlined into the reads that nest, the one-time
        /// generation call widens every level's frame, which cuts how deep data can nest on a given stack.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadObject GenerateObjectRead(Type type)
        {
            if (!CanBeAnObjectsType(type))
            {
                ThrowHelper.ThrowImpossibleObjectType(type);
            }

            return DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadObject>(type, Settings, true, false);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadMethods<T, TStream, TSettingGen>.ReadSealed GenerateSealedRead<T>()
        {
            return ReadMethods<T, TStream, TSettingGen>.Method =
                DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadMethods<T, TStream, TSettingGen>.ReadSealed>(typeof(T), Settings, false, false);
        }

        /// <summary>
        /// Generating code throws <see cref="ArgumentException"/> for a byref, pointer, byref-like or open generic type or
        /// void, none of which an object can be, and for an array of pointers, which the writer can't write either, so a
        /// stored name that resolves to one is rejected as invalid data first.
        /// </summary>
        private static bool CanBeAnObjectsType(Type type)
        {
            var innermost = type;
            while (innermost.IsArray)
            {
                innermost = innermost.GetElementType()!;
            }

            return !innermost.IsByRef && !innermost.IsPointer && !innermost.IsByRefLike && !type.ContainsGenericParameters && type != typeof(void);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Type ReadTypeRef(bool readSerializedVersionId)
        {
            _stream.ReserveSize(4);
            _stream.ThrowIfStreamEndsWithin(4);
            var knownTypeIndex = _stream.Read<int>();
            if (knownTypeIndex != -1)
            {
                return _loadedTypeRefs.GetReferenced(knownTypeIndex, "type");
            }

            return ReadNewTypeRef(readSerializedVersionId);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Type ReadNewTypeRef(bool readSerializedVersionId)
        {
            var typeId = _stream.ReadTypeId();
            var type = _knownTypes.Find(typeId);
            if (type == null)
            {
                type = _stream.RestoreTypeFromId(typeId, out var isCanonical);
                if (isCanonical)
                {
                    _knownTypes.Add(typeId, type);
                }
            }

            if (readSerializedVersionId)
            {
                CheckSerializedVersionUniqueId(type);
            }

            _loadedTypeRefs.Add(type);
            return type;
        }

        public void CheckSerializedVersionUniqueId(Type type)
        {
            _stream.ReserveSize(4);
            _stream.ThrowIfStreamEndsWithin(4);
            var expectedId = GetSerializedVersionUniqueId(type);
            var id = _stream.Read<int>();
            if (id != expectedId)
            {
                ThrowHelper.ThrowVersionIdMismatch(type);
            }
        }

        internal T ReadValueInternal<T>(bool useSerializedVersionId)
        {
            EnsureSufficientStack();
            if (useSerializedVersionId)
            {
                CheckSerializedVersionUniqueId<T>();
            }

            var method = ReadMethods<T, TStream, TSettingGen>.Method ?? GenerateSealedRead<T>();
            return method(ref _stream, this);
        }

        private void CheckSerializedVersionUniqueId<T>()
        {
            var expectedId = GetSerializedVersionUniqueId<T>();
            _stream.ReserveSize(4);
            _stream.ThrowIfStreamEndsWithin(4);
            var id = _stream.Read<int>();
            if (id != expectedId)
            {
                ThrowHelper.ThrowVersionIdMismatch(typeof(T));
            }
        }

        /// <summary>
        /// Generated code calls this for each nested object of a type it doesn't inline, and inlined there the statics of
        /// <typeparamref name="T"/> resolve when the JIT compiles it rather than through a helper call per object.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T ReadSealedInternal<T>(bool useSerializedVersionId)
        {
            if (ReadSealedHeader(useSerializedVersionId, out T result))
            {
                return result;
            }

            var method = ReadMethods<T, TStream, TSettingGen>.Method ?? GenerateSealedRead<T>();
            return method(ref _stream, this);
        }

        private void DisallowReadingObjectReference()
        {
            _allowReadingObjectReference = false;
        }

        private void AllowReadingObjectReference()
        {
            _allowReadingObjectReference = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ReadObjectRefHeader<T>(bool checkSerializedVersionId, out T result)
        {
            result = default!;
            _stream.ReserveSize(9);
            var isNull = _stream.Read<byte>() == 0;
            if (isNull)
            {
                return true;
            }

            if (checkSerializedVersionId)
            {
                _stream.ThrowIfStreamEndsWithin(4);
                var expectedId = GetSerializedVersionUniqueId<T>();
                var id = _stream.Read<int>();
                if (id != expectedId)
                {
                    ThrowHelper.ThrowVersionIdMismatch(typeof(T));
                }
            }

            if (Settings.SerializationMode == Mode.Graph)
            {
                CheckReadingObjectReference();

                var refNo = _stream.Read<int>();
                if (refNo != -1)
                {
                    result = (T)GetSavedReference(refNo);
                    return true;
                }

            }
            return false;
        }

        internal object GetSavedReference(int refIndex) => _loadedObjectRefs.GetReferenced(refIndex, "object");

        private void CheckReadingObjectReference()
        {
            if (!_allowReadingObjectReference)
            {
                ThrowHelper.ThrowObjectReferenceDuringInstantiation();
            }
        }

        internal bool WriteObjectRef(object value)
        {
            ref var index = ref _savedObjectLookup!.GetOrAddValueRef(value);
            if (index == 0)
            {
                index = _savedObjectLookup.Count;
                _stream.Write(-1);
                return false;
            }
            else
            {
                _stream.Write(index);
                return true;
            }
        }

        internal bool WriteTypeRef(Type value, bool writeSerializedVersionId)
        {
            if(_lastRefType == value)
            {
                _stream.Write(_lastRefIndex);
                return false;
            }

            _lastRefType = value;

            ref var index = ref _savedTypeLookup.GetOrAddValueRef(value);
            if (index == 0)
            {
                index = _savedTypeLookup.Count;
                _stream.Write(-1);
                _stream.WriteTypeId(value);
                if (writeSerializedVersionId)
                {
                    WriteSerializedVersionUniqueId(value);
                }
                _lastRefIndex = index;
                return true;
            }

            _stream.Write(index);
            _lastRefIndex = index;
            return false;
        }

        private void WriteSerializedVersionUniqueId(Type value)
        {
            _stream.ReserveSize(4);
            var id = GetSerializedVersionUniqueId(value);
            _stream.Write(id);
        }

        private int GetSerializedVersionUniqueId(Type t)
        {
            ref var result = ref VirtualVersionUniqueIds.GetOrAddValueRef(t);
            if (result == 0)
            {
                if (!DynamicCodeMethods._virtualWriteMethods.TryGetValue(
                    new TypeKey { IncludesTypeInfo = true, Type = t, Settings = Settings },
                    out var gd))
                {
                    Precompile(t);
                    result = GetSerializedVersionUniqueId(t);
                }
                else
                {
                    result = gd.SerializedVersionUniqueId;
                }
            }

            return result;
        }

        private int GetSerializedVersionUniqueId<T>()
        {
            var result = WriteMethods<T, TStream, TSettingGen>.VersionUniqueId;
            if (result == 0)
            {
                Precompile<T>();
                return GetSerializedVersionUniqueId<T>();
            }

            return result;
        }

        internal void WriteInternal(object? value)
        {
            if (WriteNullByte(value))
            {
                return;
            }

            var type = value!.GetType();

            if (_lastWriteType == type)
            {
                _lastWriteMethod!(value, ref _stream, this);
                return;
            }

            ref var method = ref VirtualWriteMethods.GetOrAddValueRef(type);

            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteObject>(type, Settings, true, false);
            }

            _lastWriteType = type;
            _lastWriteMethod = method;

            method(value, ref _stream, this);
        }

        internal bool WriteNullByte(object? value)
        {
            _stream.ReserveSize(1);
            if (ReferenceEquals(value, null))
            {
                _stream.Write((byte)0);
                return true;
            }
            else
            {
                _stream.Write((byte)1);
            }

            return false;
        }

        internal Type[] GetMethodParameterTypes(MethodInfo method)
        {
            ref var parameterTypes = ref _methodParametersCache.GetOrAddValueRef(method);
            if (parameterTypes == null)
            {
                var parameters = method.GetParameters();
                parameterTypes = new Type[parameters.Length];
                for (int i = 0; i < parameters.Length; ++i)
                {
                    parameterTypes[i] = parameters[i].ParameterType;
                }
            }

            return parameterTypes;
        }

        internal Type[] GetMethodGenericTypes(MethodInfo method)
        {
            ref var genericTypes = ref _methodGenericsCache.GetOrAddValueRef(method);
            if (genericTypes == null)
            {
                genericTypes = method.GetGenericArguments();
            }

            return genericTypes;
        }

        internal void WriteFunction(Delegate value)
        {
            var declaringType = value.Method.DeclaringType;
            if (declaringType == null)
            {
                ThrowHelper.ThrowDelegateToDynamicMethod(value.Method);
            }

            var generics = GetMethodGenericTypes(value.Method);
            if (!Settings.AllowsDelegateMethod(declaringType, generics))
            {
                ThrowHelper.ThrowDelegateMethodNotAllowed(value.Method);
            }

            var delegateType = value.GetType();
            _stream.ReserveSize(4);
            WriteTypeRef(delegateType, false);
            _stream.ReserveSize(4);
            WriteTypeRef(declaringType, false);
            _stream.Write(value.Method.Name);
            _stream.ReserveSize(2);
            var parameters = GetMethodParameterTypes(value.Method);
            _stream.Write((byte)parameters.Length);
            _stream.Write((byte)generics.Length);
            for (int i = 0; i < parameters.Length; ++i)
            {
                _stream.ReserveSize(4);
                WriteTypeRef(parameters[i], false);
            }
            for (int i = 0; i < generics.Length; ++i)
            {
                _stream.ReserveSize(4);
                WriteTypeRef(generics[i], false);
            }
            _stream.ReserveSize(1);
            if (value.Target == null)
            {
                _stream.Write(false);
            }
            else
            {
                _stream.Write(true);
                WriteInternal(value.Target);
            }

            _stream.ReserveSize(5);
            if (!(value is MulticastDelegate md))
            {
                _stream.Write(false);
                return;
            }
            _stream.Write(true);

            var invocationCount = TypeFields.getInvocationCount(md);
            if (invocationCount == (IntPtr)(-1))
            {
                throw new NotSupportedException("Serializing delegates to unmanaged functions is not supported");
            }

            _stream.Write((int)invocationCount);

            if (invocationCount == (IntPtr)0)
            {
                return;
            }

            var invocationList = TypeFields.getInvocationList(md) as object[];
            if (invocationList == null)
            {
                throw new InvalidOperationException("Unexpected null invocation list on delegate");
            }

            for (int i = 0; i < (int)invocationCount; ++i)
            {
                WriteFunction((Delegate)invocationList[i]);
            }
        }

        internal struct DelegateID : IEquatable<DelegateID>
        {
            public DelegateID(Type delegateType, Type declaringType, MethodInfo delegateMethod)
            {
                DelegateType = delegateType;
                DeclaringType = declaringType;
                DelegateMethod = delegateMethod;
            }

            public Type DelegateType;
            public Type DeclaringType;
            public MethodInfo DelegateMethod;

            public override bool Equals(object? obj)
            {
                return obj is DelegateID id && Equals(id);
            }

            public bool Equals(DelegateID other)
            {
                return EqualityComparer<Type>.Default.Equals(DelegateType, other.DelegateType) &&
                       EqualityComparer<Type>.Default.Equals(DeclaringType, other.DeclaringType) &&
                       EqualityComparer<MethodInfo>.Default.Equals(DelegateMethod, other.DelegateMethod);
            }

            public override int GetHashCode()
            {
                var hashCode = 1750649675;
                hashCode = hashCode * -1521134295 + EqualityComparer<Type>.Default.GetHashCode(DelegateType);
                hashCode = hashCode * -1521134295 + EqualityComparer<Type>.Default.GetHashCode(DeclaringType);
                hashCode = hashCode * -1521134295 + EqualityComparer<MethodInfo>.Default.GetHashCode(DelegateMethod);
                return hashCode;
            }

            public static bool operator ==(DelegateID iD1, DelegateID iD2)
            {
                return iD1.Equals(iD2);
            }

            public static bool operator !=(DelegateID iD1, DelegateID iD2)
            {
                return !(iD1 == iD2);
            }
        }

        internal readonly record struct CachedDelegate(Delegate? Template, Type? TargetType);

        private static Func<object, object> CreateCloneFunc()
        {
            var p = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, object>>(
                Expression.Call(p, typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!)
                , p
            ).Compile();
        }

        private static Action<Delegate, object> CreateSetTargetAction()
        {
            var p = Expression.Parameter(typeof(Delegate));
            var t = Expression.Parameter(typeof(object));
            return Expression.Lambda<Action<Delegate, object>>(
                Expression.Assign(
                    Expression.MakeMemberAccess(p,
                        typeof(Delegate).GetField("_target", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.NonPublic)!), t)
                , p, t
            ).Compile();
        }

        internal Delegate ReadFunction()
        {
            EnsureSufficientStack();
            var delegateType = ReadTypeRef(false);
            var declaringType = ReadTypeRef(false);
            var methodName = _stream.Read();
            _stream.ReserveSize(2);
            var parameterCount = _stream.Read<byte>();
            var genericCount = _stream.Read<byte>();
            for (int i = 0; i < parameterCount; ++i)
            {
                _parameterTypeBuffer[i] = ReadTypeRef(false);
            }

            var genericTypeBuffer = genericCount <= 4
                ? (genericCount == 0 ? null : _genericTypeBuffers[genericCount - 1])
                : new Type[genericCount];

            for (int i = 0; i < genericCount; ++i)
            {
                genericTypeBuffer![i] = ReadTypeRef(false);
            }

            if (!Settings.AllowsDelegateMethod(declaringType, genericTypeBuffer))
            {
                ThrowHelper.ThrowInvalidDelegate($"{declaringType.FullName}.{methodName} is declared in, or instantiated over, an assembly the settings don't allow delegate methods from");
            }

            _stream.ReserveSize(1);
            bool hasTarget = _stream.Read<bool>();
            Delegate result;
            var delegateMethod = FindDelegateMethod(declaringType, methodName, parameterCount, genericTypeBuffer);
            if (delegateMethod == null)
            {
                ThrowHelper.ThrowInvalidDelegate($"no method {declaringType.FullName}.{methodName} matches its parameter types");
            }

            if (hasTarget)
            {
                var target = ReadInternal();
                ref var cached = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod));
                if (cached.Template == null)
                {
                    cached = new CachedDelegate(CreateTemplate(delegateType, delegateMethod, closedOverTarget: true), ClosedTargetType(delegateType, delegateMethod));
                }

                // The clone shares the template's method pointer, which only works on the type the method was bound for.
                if (cached.TargetType?.IsInstanceOfType(target) != true)
                {
                    ThrowHelper.ThrowInvalidDelegate($"its target, a {target?.GetType().FullName ?? "null"}, can't be bound to {declaringType.FullName}.{methodName}");
                }

                result = (Delegate)_clone(cached.Template!);
                _setTarget(result, target);
            }
            else
            {
                ref var cached = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod));
                if (cached.Template == null)
                {
                    cached = new CachedDelegate(CreateTemplate(delegateType, delegateMethod, closedOverTarget: false), null);
                }

                result = cached.Template!;
            }

            _stream.ReserveSize(5);
            var isMulticast = _stream.Read<bool>();
            if (!isMulticast)
            {
                return result;
            }

            var invocationCount = _stream.Read<int>();
            if (invocationCount == 0)
            {
                return result;
            }

            CheckArrayLength(invocationCount, 1);
            var list = new Delegate[invocationCount];
            for (int i = 0; i < invocationCount; ++i)
            {
                list[i] = ReadFunction();
            }

            try
            {
                return Delegate.Combine(list)!;
            }
            catch (ArgumentException e)
            {
                return ThrowHelper.ThrowInvalidDelegate("its invocation list mixes delegate types", e);
            }
        }

        private static Delegate CreateTemplate(Type delegateType, MethodInfo method, bool closedOverTarget)
        {
            try
            {
                return closedOverTarget ? Delegate.CreateDelegate(delegateType, null, method) : Delegate.CreateDelegate(delegateType, method);
            }
            catch (ArgumentException e)
            {
                return ThrowHelper.ThrowInvalidDelegate($"{method.DeclaringType?.FullName}.{method.Name} can't be bound to a {delegateType.FullName}", e);
            }
        }

        /// <summary>
        /// The writer stores a generic method's parameter types with its type arguments substituted, so a generic method
        /// is compared once it's instantiated over the stored arguments.  A method those arguments break the constraints
        /// of can't be the one written.
        /// </summary>
        private MethodInfo? FindDelegateMethod(Type declaringType, string? methodName, int parameterCount, Type[]? genericArguments)
        {
            var genericCount = genericArguments?.Length ?? 0;
            var methods = TypeMethods.GetMethods(declaringType);
            for (int i = 0; i < methods.Count; ++i)
            {
                var m = methods[i];
                if (m.MethodInfo.Name != methodName
                    || m.ParameterTypes.Length != parameterCount
                    || m.GenericArguments.Length != genericCount)
                {
                    continue;
                }

                var method = genericCount > 0 ? TryMakeGenericMethod(m.MethodInfo, genericArguments!) : m.MethodInfo;
                if (method != null && HasStoredParameterTypes(genericCount > 0 ? GetMethodParameterTypes(method) : m.ParameterTypes))
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo? TryMakeGenericMethod(MethodInfo definition, Type[] arguments)
        {
            try
            {
                return definition.MakeGenericMethod(arguments);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private bool HasStoredParameterTypes(Type[] parameterTypes)
        {
            for (int j = 0; j < parameterTypes.Length; ++j)
            {
                if (parameterTypes[j] != _parameterTypeBuffer[j])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The type a delegate's target has to be for <paramref name="method"/> to run on it, or null if
        /// <see cref="Delegate.CreateDelegate(Type, object, MethodInfo)"/> doesn't close a delegate of
        /// <paramref name="delegateType"/> over a target for it (an open delegate keeps itself in the target field).
        /// </summary>
        private static Type? ClosedTargetType(Type delegateType, MethodInfo method)
        {
            var delegateParameterCount = delegateType.GetMethod("Invoke")!.GetParameters().Length;
            var parameters = method.GetParameters();
            if (method.IsStatic)
            {
                return parameters.Length == delegateParameterCount + 1 && !parameters[0].ParameterType.IsValueType ? parameters[0].ParameterType : null;
            }

            return parameters.Length == delegateParameterCount ? method.DeclaringType : null;
        }

        internal void WriteValueInternal<T>(T value, bool useSerializedVersionId)
        {
            if (useSerializedVersionId)
            {
                WriteSerializedVersionUniqueId<T>();
            }
            ref var method = ref WriteMethods<T, TStream, TSettingGen>.Method;
            if (method == null)
            {
                CheckTypes(value);

                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteMethods<T, TStream, TSettingGen>.WriteSealed>(value!.GetType(), Settings, false, false);
            }

            method(value, ref _stream, this);
        }

        private void WriteSerializedVersionUniqueId<T>()
        {
            _stream.ReserveSize(4);
            var id = GetSerializedVersionUniqueId<T>();
            _stream.Write(id);
        }

        internal void WriteSealedInternal<T>(T value, bool useSerializedVersionId)
        {
            _stream.ReserveSize(5);
            if (ReferenceEquals(value, null))
            {
                _stream.Write((byte)0);
                return;
            }
            else
            {
                _stream.Write((byte)1);
            }

            if(useSerializedVersionId)
            {
                var id = GetSerializedVersionUniqueId<T>();
                _stream.Write(id);
            }
            ref var method = ref WriteMethods<T, TStream, TSettingGen>.Method;
            if (method == null)
            {
                CheckTypes(value!);

                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteMethods<T, TStream, TSettingGen>.WriteSealed>(value!.GetType(), Settings, false, false);
            }

            method(value, ref _stream, this);
        }

        [Conditional("DEV")]
        private void CheckTypes<T>(T value)
        {
            if (typeof(T) != value!.GetType())
            {
                throw new InvalidOperationException("Actual type found while attempting to write a sealed type does not match");
            }
        }

        /// <summary>
        /// Resolves the substitute for a boundary occurrence.  <paramref name="concreteType"/> is the
        /// type the generated code will cast the result to, which the setter cannot validate up front
        /// because the concrete types in a payload are not known until it is read.
        /// </summary>
        internal object GetBoundarySubstitute(Type markedType, Type concreteType)
        {
            if (!_boundarySubstitutes.TryGetValue(markedType, out var substitute))
            {
                throw new InvalidOperationException($"No boundary substitute has been registered for Type {markedType.FullName}. Call IBinary.SetBoundarySubstitute before reading a payload containing this type.");
            }

            if (!concreteType.IsInstanceOfType(substitute))
            {
                throw new InvalidOperationException(
                    $"The boundary substitute registered for Type {markedType.FullName} is of type {substitute.GetType().FullName}, which cannot be assigned to {concreteType.FullName} as found in the payload."
                    + " A single substitute must be assignable to every concrete boundary type the payload contains, so a payload holding sibling subclasses of one marked type cannot be read.");
            }

            return substitute;
        }

        internal void QueueAfterDeserializationHook(Action<object, object> method, object instance)
        {
            _deserializationHooks.Add((method, instance));
        }

        internal void CheckArrayLength(int length, int minimumBytesPerElement)
        {
            CheckLength(length);
            CheckArrayElementCount(length, minimumBytesPerElement);
        }

        internal void CheckArrayElementCount(long elementCount, int minimumBytesPerElement)
        {
            if (minimumBytesPerElement == 0)
            {
                return;
            }

            var byteCount = SaturatingMultiply(elementCount, minimumBytesPerElement);
            if (_stream.TryEnsureAvailable(byteCount))
            {
                return;
            }

            if (byteCount > Array.MaxLength)
            {
                ThrowHelper.ThrowTooLargeToCheck(elementCount);
            }

            _stream.ReadAhead((int)byteCount);
        }

        /// <summary>
        /// The runtime won't create a multi-dimensional array once the product of its dimensions so far, taken in order,
        /// passes <see cref="uint.MaxValue"/>, even when a later dimension is 0 (<c>AllocateArrayEx</c> in coreclr's
        /// <c>gchelpers.cpp</c> throws <see cref="OutOfMemoryException"/>), so the running product is checked here.
        /// </summary>
        internal static long MultiplyArrayLength(long elementCount, int length)
        {
            CheckLength(length);
            var product = SaturatingMultiply(elementCount, length);
            if (product > uint.MaxValue)
            {
                ThrowHelper.ThrowInvalidLength("product of array dimensions", product);
            }

            return product;
        }

        /// <summary>
        /// Multiplies non-negative counts, saturating at <see cref="long.MaxValue"/>: the product is only compared with
        /// what the stream can supply, which an overflow would wrap below.
        /// </summary>
        private static long SaturatingMultiply(long a, long b)
        {
            return b == 0 || a <= long.MaxValue / b ? a * b : long.MaxValue;
        }

        private static void CheckLength(int length)
        {
            if ((uint)length > Array.MaxLength)
            {
                ThrowHelper.ThrowInvalidLength("array length", length);
            }
        }

        // A span holds at most int.MaxValue bytes, so a larger blittable array is copied a chunk at a time.
        private const int BlittableChunkBytes = 1 << 30;

        // elementSize is always Unsafe.SizeOf<T>(), but the generated write code that passes it is hashed into
        // SerializedVersionUniqueId, so dropping the parameter changes the id every payload carries for these arrays.
        internal void WriteValuesArray1<T>(T[] array, int elementSize)
            where T : unmanaged
        {
            WriteBlittable(ref MemoryMarshal.GetArrayDataReference(array), array.Length);
        }

        internal T[] ReadValuesArray1<T>(int length)
            where T : unmanaged
        {
            if (length == 0)
            {
                return Array.Empty<T>();
            }

            CheckLength(length);
            if (!_stream.TryEnsureAvailable((long)length * Unsafe.SizeOf<T>()))
            {
                return ReadBlittableAsSupplied<T>(length);
            }

            var array = new T[length];
            ReadBlittable(ref MemoryMarshal.GetArrayDataReference(array), length);
            return array;
        }

        internal void WriteValuesArray2<T>(T[,] array, int elementSize)
            where T : unmanaged
        {
            WriteBlittable(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(array)), array.LongLength);
        }

        internal T[,] ReadValuesArray2<T>(int length1, int length2)
            where T : unmanaged
        {
            var count = MultiplyArrayLength(MultiplyArrayLength(1, length1), length2);
            if (count == 0)
            {
                return new T[length1, length2];
            }

            if (!_stream.TryEnsureAvailable(SaturatingMultiply(count, Unsafe.SizeOf<T>())))
            {
                if (count > Array.MaxLength)
                {
                    ThrowHelper.ThrowTooLargeToCheck(count);
                }

                var values = ReadBlittableAsSupplied<T>((int)count);
                var copy = new T[length1, length2];
                values.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(copy)), values.Length));
                return copy;
            }

            var array = new T[length1, length2];
            ReadBlittable(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(array)), count);
            return array;
        }

        private void WriteBlittable<T>(ref T first, long count)
            where T : unmanaged
        {
            var chunk = Math.Max(1, BlittableChunkBytes / Unsafe.SizeOf<T>());
            for (long done = 0; done < count; done += chunk)
            {
                var values = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref first, (nint)done), (int)Math.Min(chunk, count - done));
                _stream.WriteValues<T>(MemoryMarshal.AsBytes(values));
            }
        }

        private void ReadBlittable<T>(ref T first, long count)
            where T : unmanaged
        {
            var chunk = Math.Max(1, BlittableChunkBytes / Unsafe.SizeOf<T>());
            for (long done = 0; done < count; done += chunk)
            {
                var values = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref first, (nint)done), (int)Math.Min(chunk, count - done));
                _stream.ReadBytes(MemoryMarshal.AsBytes(values));
            }
        }

        private const int UnverifiedArrayStartBytes = 1024 * 1024;

        /// <summary>
        /// Reads an array the stream can't vouch for up front, doubling it from 1 MiB only as the values arrive, so what a
        /// corrupt length allocates stays proportional to what the stream actually holds.
        /// </summary>
        private T[] ReadBlittableAsSupplied<T>(int length)
            where T : unmanaged
        {
            var values = new T[Math.Min(length, Math.Max(1, UnverifiedArrayStartBytes / Unsafe.SizeOf<T>()))];
            var read = 0;
            while (true)
            {
                ReadBlittable(ref values[read], values.Length - read);
                read = values.Length;
                if (read == length)
                {
                    return values;
                }

                Array.Resize(ref values, (int)Math.Min(length, 2L * read));
            }
        }

        internal void WriteRawBytes<T>(ref T value, int offset, int length)
            where T : struct
        {
            _stream.WriteValues<byte>(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>()).Slice(offset, length));
        }

        internal void ReadRawBytes<T>(ref T value, int offset, int length)
            where T : struct
        {
            _stream.ReadBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>()).Slice(offset, length));
        }

        internal IBinaryWriter BinaryWriter => _binaryWriter;
        internal IBinaryReader BinaryReader => _binaryReader;

        internal T GetCustomContext<T>()
            where T : class
        {
            return (_customContext as T)!;
        }
    }
}
