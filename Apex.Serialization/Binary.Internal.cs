using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;
using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
// using System.Linq.Expressions;
using FastExpressionCompiler.LightExpression;
using System.Reflection;

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

        internal T ReadObjectEntry<T>()
        {
            object? result;
            if (StaticTypeInfo<T>.IsSealedOrHasNoDescendents)
            {
                if (StaticTypeInfo<T>.IsValueType)
                {
                    result = ReadValueInternal<T>(Settings.UseSerializedVersionId);
                }
                else
                {
                    result = ReadSealedInternal<T>(Settings.UseSerializedVersionId);
                }
            }
            else
            {
                result = ReadInternal();
            }

            return (T)result!;
        }

        internal object ReadInternal()
        {
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

            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadObject>(type, Settings, true, false);
            }

            _lastReadType = type;
            _lastReadMethod = method;

            return method(ref _stream, this);
        }

        private unsafe Type ReadTypeRef(bool readSerializedVersionId)
        {
            _stream.ReserveSize(4);
            var knownTypeIndex = _stream.Read<int>();
            Type type;
            if (knownTypeIndex != -1)
            {
                type = _loadedTypeRefs[knownTypeIndex - 1];
            }
            else
            {
                var typeId = _stream.ReadTypeId(out var typeLen1, out var typeLen2);
                type = _knownTypes.Find(typeId, typeLen1 + typeLen2);
                if (type == null)
                {
                    type = _stream.RestoreTypeFromId(ref typeId, typeLen1, typeLen2);
                    _knownTypes.Add(typeId, typeLen1 + typeLen2, type);
                }

                if (readSerializedVersionId)
                {
                    _stream.ReserveSize(4);
                    var expectedId = GetSerializedVersionUniqueId(type);
                    var id = _stream.Read<int>();
                    if(id != expectedId)
                    {
                        throw new InvalidOperationException($"SerializedVersionUniqueId does not match for Type {type.FullName}");
                    }
                }

                _loadedTypeRefs.Add(type);
            }

            return type;
        }

        internal T ReadValueInternal<T>(bool useSerializedVersionId)
        {
            if (useSerializedVersionId)
            {
                var expectedId = GetSerializedVersionUniqueId<T>();
                _stream.ReserveSize(4);
                var id = _stream.Read<int>();
                if (id != expectedId)
                {
                    throw new InvalidOperationException($"SerializedVersionUniqueId does not match for Type {typeof(T).FullName}");
                }
            }

            ref var method = ref ReadMethods<T, TStream, TSettingGen>.Method;
            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadMethods<T, TStream, TSettingGen>.ReadSealed>(typeof(T), Settings, false, false);
            }

            return method(ref _stream, this);
        }

        internal T ReadSealedInternal<T>(bool useSerializedVersionId)
        {
            if (ReadObjectRefHeader(useSerializedVersionId, out T result))
            {
                return result;
            }

            ref var method = ref ReadMethods<T, TStream, TSettingGen>.Method;
            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadMethods<T, TStream, TSettingGen>.ReadSealed>(typeof(T), Settings, false, false);
            }

            return method(ref _stream, this);
        }

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
                var expectedId = GetSerializedVersionUniqueId<T>();
                var id = _stream.Read<int>();
                if(id != expectedId)
                {
                    throw new InvalidOperationException($"SerializedVersionUniqueId does not match for Type {typeof(T).FullName}");
                }
            }

            if (Settings.SerializationMode == Mode.Graph)
            {
                var refNo = _stream.Read<int>();
                if (refNo != -1)
                {
                    {
                        result = (T)_loadedObjectRefs[refNo - 1];
                        return true;
                    }
                }

            }
            return false;
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
                    _stream.ReserveSize(4);
                    var id = GetSerializedVersionUniqueId(value);
                    _stream.Write(id);
                }
                _lastRefIndex = index;
                return true;
            }

            _stream.Write(index);
            _lastRefIndex = index;
            return false;
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
            var delegateType = value.GetType();
            _stream.ReserveSize(4);
            WriteTypeRef(delegateType, false);
            _stream.ReserveSize(4);
            WriteTypeRef(value.Method.DeclaringType!, false);
            _stream.Write(value.Method.Name);
            _stream.ReserveSize(2);
            var parameters = GetMethodParameterTypes(value.Method);
            var generics = GetMethodGenericTypes(value.Method);
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

        private static Func<object, object> CreateCloneFunc()
        {
            var p = Expression.Parameter(typeof(object));
            var f = Expression.Lambda<Func<object, object>>(
                Expression.Call(p, typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                , p
            ).CompileFast(true);
            return f;
        }

        private static Action<Delegate, object> CreateSetTargetAction()
        {
            var p = Expression.Parameter(typeof(Delegate));
            var t = Expression.Parameter(typeof(object));
            var f = Expression.Lambda<Action<Delegate, object>>(
                Expression.Assign(
                    Expression.MakeMemberAccess(p,
                        typeof(Delegate).GetField("_target", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.NonPublic)), t)
                , p, t
            ).CompileFast(true);
            return f;
        }

        internal Delegate ReadFunction()
        {
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

            _stream.ReserveSize(1);
            bool hasTarget = _stream.Read<bool>();
            Delegate result;
            var methods = TypeMethods.GetMethods(declaringType);
            MethodInfo? delegateMethod = null;
            for (int i = 0; i < methods.Count; ++i)
            {
                var m = methods[i];
                if (m.MethodInfo.Name != methodName)
                {
                    continue;
                }

                if (m.ParameterTypes.Length != parameterCount)
                {
                    continue;
                }

                if (m.GenericArguments.Length != genericCount)
                {
                    continue;
                }

                for (int j = 0; j < parameterCount; ++j)
                {
                    if (m.ParameterTypes[j] != _parameterTypeBuffer[j])
                    {
                        goto next;
                    }
                }

                delegateMethod = m.MethodInfo;
                if (m.GenericArguments.Length > 0)
                {
                    delegateMethod = delegateMethod.MakeGenericMethod(genericTypeBuffer!);
                }
                break;
                next:;
            }

            if (hasTarget)
            {
                var target = ReadInternal();
                ref var cachedDelegate = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod!));
                if (cachedDelegate == null)
                {
                    cachedDelegate = Delegate.CreateDelegate(delegateType, null, delegateMethod!);
                }

                result = (Delegate)_clone(cachedDelegate);
                _setTarget(result, target);
            }
            else
            {
                ref var cachedDelegate = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod!));
                if (cachedDelegate == null)
                {
                    cachedDelegate = Delegate.CreateDelegate(delegateType, delegateMethod!);
                }

                result = cachedDelegate;
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

            var list = new Delegate[invocationCount];
            for (int i = 0; i < invocationCount; ++i)
            {
                list[i] = ReadFunction();
            }

            return Delegate.Combine(list)!;
        }

        internal void WriteValueInternal<T>(T value, bool useSerializedVersionId)
        {
            if (useSerializedVersionId)
            {
                _stream.ReserveSize(4);
                var id = GetSerializedVersionUniqueId<T>();
                _stream.Write(id);
            }
            ref var method = ref WriteMethods<T, TStream, TSettingGen>.Method;
            if (method == null)
            {
                CheckTypes(value);

                method = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteMethods<T, TStream, TSettingGen>.WriteSealed>(value!.GetType(), Settings, false, false);
            }

            method(value, ref _stream, this);
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

        internal void QueueAfterDeserializationHook(Action<object, object> method, object instance)
        {
            _deserializationHooks.Add((method, instance));
        }

        unsafe internal void WriteValuesArray1<T>(T[] array, int elementSize)
            where T : unmanaged
        {
            fixed (void* ptr = array)
            {
                _stream.WriteBytes(ptr, (uint)(array.Length * elementSize));
            }
        }

        unsafe internal T[] ReadValuesArray1<T>(int elementSize, int length)
            where T : unmanaged
        {
            if(length == 0)
            {
                return Array.Empty<T>();
            }

            var array = new T[length];

            fixed(void* ptr = array)
            {
                _stream.ReadBytes(ptr, (uint)(length * elementSize));
            }

            return array;
        }

        unsafe internal void WriteValuesArray2<T>(T[,] array, int elementSize)
            where T : unmanaged
        {
            var length1 = array.GetLength(0);
            var length2 = array.GetLength(1);

            fixed (void* ptr = array)
            {
                _stream.WriteBytes(ptr, (uint)(length1 * length2 * elementSize));
            }
        }

        unsafe internal T[,] ReadValuesArray2<T>(int elementSize, int length1, int length2)
            where T : unmanaged
        {
            var array = new T[length1, length2];

            if (length1 == 0 && length2 == 0)
            {
                return array;
            }

            fixed (void* ptr = array)
            {
                _stream.ReadBytes(ptr, (uint)(length1 * length2 * elementSize));
            }

            return array;
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
