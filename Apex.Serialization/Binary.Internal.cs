using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;
using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Apex.Serialization
{
    internal sealed partial class Binary<TStream>
        where TStream : struct, IBinaryStream
    {
        internal void WriteObjectEntry<T>(T value)
        {
            if (StaticTypeInfo<T>.IsSealedOrHasNoDescendents)
            {
                if (StaticTypeInfo<T>.IsValueType)
                {
                    WriteValueInternal(value);
                }
                else
                {
                    WriteSealedInternal(value);
                }
            }
            else
            {
                WriteInternal(value);
            }
        }

        internal T ReadObjectEntry<T>()
        {
            object result;
            if (StaticTypeInfo<T>.IsSealedOrHasNoDescendents)
            {
                if (StaticTypeInfo<T>.IsValueType)
                {
                    result = ReadValueInternal<T>();
                }
                else
                {
                    result = ReadSealedInternal<T>();
                }
            }
            else
            {
                result = ReadInternal();
            }

            return (T)result;
        }

        internal object ReadInternal()
        {
            if (ReadObjectRefHeader<object>(out var result))
            {
                return result;
            }

            var type = ReadTypeRefInternal();

            if (_lastReadType == type)
            {
                return _lastReadMethod(ref _stream, this);
            }

            ref var method = ref VirtualReadMethods.GetOrAddValueRef(type);

            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream>>.GenerateReadMethod<ReadObject>(type, Settings, true);
            }

            _lastReadType = type;
            _lastReadMethod = method;

            return method(ref _stream, this);
        }

        Type ISerializer.ReadTypeRef()
        {
            return ReadTypeRefInternal();
        }

        private unsafe Type ReadTypeRefInternal()
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

                _loadedTypeRefs.Add(type);
            }

            return type;
        }

        internal T ReadValueInternal<T>()
        {
            var method = ReadMethods<T, TStream>.Methods[_settingsIndex];
            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream>>.GenerateReadMethod<ReadMethods<T, TStream>.ReadSealed>(typeof(T), Settings, false);
                ReadMethods<T, TStream>.Methods[_settingsIndex] = method;
            }

            return method(ref _stream, this);
        }

        internal T ReadSealedInternal<T>()
        {
            if (ReadObjectRefHeader(out T result))
            {
                return result;
            }

            var method = ReadMethods<T, TStream>.Methods[_settingsIndex];
            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream>>.GenerateReadMethod<ReadMethods<T, TStream>.ReadSealed>(typeof(T), Settings, false);
                ReadMethods<T, TStream>.Methods[_settingsIndex] = method;
            }

            return method(ref _stream, this);
        }

        private bool ReadObjectRefHeader<T>(out T result)
        {
            result = default;
            _stream.ReserveSize(5);
            var isNull = _stream.Read<byte>() == 0;
            if (isNull)
            {
                {
                    return true;
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

        internal bool ReadNullByteInternal()
        {
            _stream.ReserveSize(1);
            return _stream.Read<byte>() == 0;
        }

        bool ISerializer.ReadNullByte()
        {
            return ReadNullByteInternal();
        }

        bool ISerializer.WriteObjectRef(object value)
        {
            ref var index = ref _savedObjectLookup.GetOrAddValueRef(value);
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

        void ISerializer.WriteTypeRef(Type value)
        {
            WriteTypeRefInternal(value);
        }

        internal void WriteTypeRefInternal(Type value)
        {
            if(_lastRefType == value)
            {
                _stream.Write(_lastRefIndex);
                return;
            }

            _lastRefType = value;

            ref var index = ref _savedTypeLookup.GetOrAddValueRef(value);
            if (index == 0)
            {
                index = _savedTypeLookup.Count;
                _stream.Write(-1);
                _stream.WriteTypeId(value);
            }
            else
            {
                _stream.Write(index);
            }

            _lastRefIndex = index;
        }

        internal void WriteInternal(object value)
        {
            if (WriteNullByteInternal(value))
            {
                return;
            }

            var type = value.GetType();

            if (_lastWriteType == type)
            {
                _lastWriteMethod(value, ref _stream, this);
                return;
            }

            ref var method = ref VirtualWriteMethods.GetOrAddValueRef(type);

            if (method == null)
            {
                method = DynamicCode<TStream, Binary<TStream>>.GenerateWriteMethod<WriteObject>(type, Settings, true);
            }

            _lastWriteType = type;
            _lastWriteMethod = method;

            method(value, ref _stream, this);
        }

        internal bool WriteNullByteInternal(object value)
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

        bool ISerializer.WriteNullByte(object value)
        {
            return WriteNullByteInternal(value);
        }

        bool ISerializer.WriteNullableByte<T>(T? value)
        {
            _stream.ReserveSize(1);
            if (value.HasValue)
            {
                _stream.Write((byte)1);
            }
            else
            {
                _stream.Write((byte)0);
                return true;
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

        void ISerializer.WriteFunction(Delegate value)
        {
            WriteFunctionInternal(value);
        }

        internal void WriteFunctionInternal(Delegate value)
        {
            var delegateType = value.GetType();
            _stream.ReserveSize(4);
            WriteTypeRefInternal(delegateType);
            _stream.ReserveSize(4);
            WriteTypeRefInternal(value.Method.DeclaringType);
            _stream.Write(value.Method.Name);
            _stream.ReserveSize(2);
            var parameters = GetMethodParameterTypes(value.Method);
            var generics = GetMethodGenericTypes(value.Method);
            _stream.Write((byte)parameters.Length);
            _stream.Write((byte)generics.Length);
            for (int i = 0; i < parameters.Length; ++i)
            {
                _stream.ReserveSize(4);
                WriteTypeRefInternal(parameters[i]);
            }
            for (int i = 0; i < generics.Length; ++i)
            {
                _stream.ReserveSize(4);
                WriteTypeRefInternal(generics[i]);
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
                WriteFunctionInternal((Delegate)invocationList[i]);
            }
        }

        Delegate ISerializer.ReadFunction()
        {
            return ReadFunctionInternal();
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

            public override bool Equals(object obj)
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
            return Expression.Lambda<Func<object, object>>(
                Expression.Call(p, typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
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
                        typeof(Delegate).GetField("_target", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.NonPublic)), t)
                , p, t
            ).Compile();
        }

        internal Delegate ReadFunctionInternal()
        {
            var delegateType = ReadTypeRefInternal();
            var declaringType = ReadTypeRefInternal();
            var methodName = _stream.Read();
            _stream.ReserveSize(2);
            var parameterCount = _stream.Read<byte>();
            var genericCount = _stream.Read<byte>();
            for (int i = 0; i < parameterCount; ++i)
            {
                _parameterTypeBuffer[i] = ReadTypeRefInternal();
            }

            var genericTypeBuffer = genericCount <= 4
                ? (genericCount == 0 ? null : _genericTypeBuffers[genericCount - 1])
                : new Type[genericCount];

            for (int i = 0; i < genericCount; ++i)
            {
                genericTypeBuffer[i] = ReadTypeRefInternal();
            }

            _stream.ReserveSize(1);
            bool hasTarget = _stream.Read<bool>();
            Delegate result;
            var methods = TypeMethods.GetMethods(declaringType);
            MethodInfo delegateMethod = null;
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
                    delegateMethod = delegateMethod.MakeGenericMethod(genericTypeBuffer);
                }
                break;
                next:;
            }

            if (hasTarget)
            {
                var target = ReadInternal();
                ref var cachedDelegate = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod));
                if (cachedDelegate == null)
                {
                    cachedDelegate = Delegate.CreateDelegate(delegateType, null, delegateMethod);
                }

                result = (Delegate)_clone(cachedDelegate);
                _setTarget(result, target);
            }
            else
            {
                ref var cachedDelegate = ref _delegateCache.GetOrAddValueRef(new DelegateID(delegateType, declaringType, delegateMethod));
                if (cachedDelegate == null)
                {
                    cachedDelegate = Delegate.CreateDelegate(delegateType, delegateMethod);
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
                list[i] = ReadFunctionInternal();
            }

            return Delegate.Combine(list);
        }

        internal void WriteValueInternal<T>(T value)
        {
            var method = WriteMethods<T, TStream>.Methods[_settingsIndex];
            if (method == null)
            {
                CheckTypes(value);

                method = DynamicCode<TStream, Binary<TStream>>.GenerateWriteMethod<WriteMethods<T, TStream>.WriteSealed>(value.GetType(), Settings, false);
                WriteMethods<T, TStream>.Methods[_settingsIndex] = method;
            }

            method(value, ref _stream, this);
        }

        internal void WriteSealedInternal<T>(T value)
        {
            _stream.ReserveSize(1);
            if (ReferenceEquals(value, null))
            {
                _stream.Write((byte)0);
                return;
            }
            else
            {
                _stream.Write((byte)1);
            }

            var method = WriteMethods<T, TStream>.Methods[_settingsIndex];
            if (method == null)
            {
                CheckTypes(value);

                method = DynamicCode<TStream, Binary<TStream>>.GenerateWriteMethod<WriteMethods<T, TStream>.WriteSealed>(value.GetType(), Settings, false);
                WriteMethods<T, TStream>.Methods[_settingsIndex] = method;
            }

            method(value, ref _stream, this);
        }

        [Conditional("DEV")]
        private void CheckTypes<T>(T value)
        {
            if (typeof(T) != value.GetType())
            {
                throw new InvalidOperationException("Actual type found while attempting to write a sealed type does not match");
            }
        }

        void ISerializer.QueueAfterDeserializationHook(Action<object, object> method, object instance)
        {
            _deserializationHooks.Add((method, instance));
        }

        unsafe void ISerializer.WriteValuesArray(object array, int length, int elementSize)
        {
            var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject().ToPointer();
                _stream.ReserveSize(4);
                _stream.Write(length);
                _stream.WriteBytes(ptr, (uint)(length * elementSize));
            }
            finally
            {
                handle.Free();
            }
        }

        unsafe void ISerializer.ReadIntoValuesArray(object array, int elementSize)
        {
            _stream.ReserveSize(4);
            var length = _stream.Read<int>();
            var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject().ToPointer();
                _stream.ReadBytes(ptr, (uint)(length * elementSize));
            }
            finally
            {
                handle.Free();
            }
        }

        IBinaryWriter ISerializer.BinaryWriter => _binaryWriter;
        IBinaryReader ISerializer.BinaryReader => _binaryReader;

        T ISerializer.GetCustomContext<T>()
        {
            return _customContext as T;
        }
    }
}
