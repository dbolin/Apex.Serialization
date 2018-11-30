﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Apex.Serialization.Internal;
using Apex.Serialization.Internal.Reflection;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization
{
    internal static class WriteMethods<T>
    {
        public static Action<T, BufferedStream, Binary>[] Methods = new Action<T, BufferedStream, Binary>[ImmutableSettings.MaxSettingsIndex + 1];
    }

    internal static class ReadMethods<T>
    {
        public static Func<BufferedStream, Binary, T>[] Methods = new Func<BufferedStream, Binary, T>[ImmutableSettings.MaxSettingsIndex + 1];
    }

    public sealed class Binary : ISerializer, IDisposable
    {
        public ImmutableSettings Settings { get; } = Serialization.Settings.Default;
        private readonly int _settingsIndex;
        private readonly BufferedStream _stream;

        List<object> ISerializer.LoadedObjectRefs => _loadedObjectRefs;

        private readonly DictionarySlim<object, int> _savedObjectLookup;
        private readonly List<object> _loadedObjectRefs;

        List<Type> ISerializer.LoadedTypeRefs => _loadedTypeRefs;

        private readonly DictionarySlim<Type, int> _savedTypeLookup = new DictionarySlim<Type, int>();
        private readonly List<Type> _loadedTypeRefs = new List<Type>();

        private readonly DictionarySlim<Type, Action<object, BufferedStream, Binary>> VirtualWriteMethods = new DictionarySlim<Type, Action<object, BufferedStream, Binary>>();
        private readonly DictionarySlim<Type, Func<BufferedStream, Binary, object>> VirtualReadMethods = new DictionarySlim<Type, Func<BufferedStream, Binary, object>>();

        private Type _lastWriteType;
        private Action<object, BufferedStream, Binary> _lastWriteMethod;

        private Type _lastReadType;
        private Func<BufferedStream, Binary, object> _lastReadMethod;

        private readonly TypeLookup<Type> _knownTypes = new TypeLookup<Type>();

        private readonly List<ValueTuple<Action<object>,object>> _deserializationHooks;

        public Binary()
        {
            _settingsIndex = Settings.SettingsIndex;
            _stream = new BufferedStream();
            if (Settings.SerializationMode == Mode.Graph)
            {
                _savedObjectLookup = new DictionarySlim<object, int>(16);
                _loadedObjectRefs = new List<object>(16);
            }

            if (Settings.SupportSerializationHooks)
            {
                _deserializationHooks = new List<ValueTuple<Action<object>, object>>();
            }
        }

        public Binary(Settings settings)
        {
            Settings = settings;
            _settingsIndex = Settings.SettingsIndex;
            _stream = new BufferedStream();
            if (Settings.SerializationMode == Mode.Graph)
            {
                _savedObjectLookup = new DictionarySlim<object, int>(16);
                _loadedObjectRefs = new List<object>(16);
            }

            if (Settings.SupportSerializationHooks)
            {
                _deserializationHooks = new List<ValueTuple<Action<object>, object>>();
            }
        }

        public void Write<T>(T value, Stream outputStream)
        {
            _stream.WriteTo(outputStream);

            if (StaticTypeInfo<T>.IsSealed)
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

            _stream.Flush();

            if (Settings.SerializationMode == Mode.Graph)
            {
                _savedObjectLookup.Clear();
            }
            _savedTypeLookup.Clear();
        }

        public T Read<T>(Stream inputStream)
        {
            _stream.ReadFrom(inputStream);

            object result;
            if (StaticTypeInfo<T>.IsSealed)
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

            if (Settings.SerializationMode == Mode.Graph)
            {
                _loadedObjectRefs.Clear();
            }
            _loadedTypeRefs.Clear();

            if (Settings.SupportSerializationHooks)
            {
                foreach (var a in _deserializationHooks)
                {
                    a.Item1(a.Item2);
                }
                _deserializationHooks.Clear();
            }

            return (T)result;
        }

        public void Precompile(Type type)
        {
            ref var readMethod = ref VirtualReadMethods.GetOrAddValueRef(type);
            if (readMethod == null)
            {
                readMethod = (Func<BufferedStream, Binary, object>)DynamicCode<BufferedStream, Binary>.GenerateReadMethod(type, Settings, true);
            }
            ref var writeMethod = ref VirtualWriteMethods.GetOrAddValueRef(type);
            if (writeMethod == null)
            {
                writeMethod = (Action<object, BufferedStream, Binary>)DynamicCode<BufferedStream, Binary>.GenerateWriteMethod(type, Settings, true);
            }
        }

        public void Precompile<T>()
        {
            var readMethod = ReadMethods<T>.Methods[_settingsIndex];
            if (readMethod == null)
            {
                readMethod = (Func<BufferedStream, Binary, T>)DynamicCode<BufferedStream, Binary>.GenerateReadMethod(typeof(T), Settings, false);
                ReadMethods<T>.Methods[_settingsIndex] = readMethod;
            }
            var writeMethod = WriteMethods<T>.Methods[_settingsIndex];
            if (writeMethod == null)
            {
                writeMethod = (Action<T, BufferedStream, Binary>)DynamicCode<BufferedStream, Binary>.GenerateWriteMethod(typeof(T), Settings, false);
                WriteMethods<T>.Methods[_settingsIndex] = writeMethod;
            }
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
                return _lastReadMethod(_stream, this);
            }

            ref var method = ref VirtualReadMethods.GetOrAddValueRef(type);

            if (method == null)
            {
                method = (Func<BufferedStream, Binary, object>)DynamicCode<BufferedStream, Binary>.GenerateReadMethod(type, Settings, true);
            }

            _lastReadType = type;
            _lastReadMethod = method;

            return method(_stream, this);
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
            var method = ReadMethods<T>.Methods[_settingsIndex];
            if (method == null)
            {
                method = (Func<BufferedStream, Binary, T>)DynamicCode<BufferedStream, Binary>.GenerateReadMethod(typeof(T), Settings, false);
                ReadMethods<T>.Methods[_settingsIndex] = method;
            }

            return method(_stream, this);
        }

        internal T ReadSealedInternal<T>()
        {
            if (ReadObjectRefHeader(out T result))
            {
                return result;
            }

            var method = ReadMethods<T>.Methods[_settingsIndex];
            if (method == null)
            {
                method = (Func<BufferedStream, Binary, T>)DynamicCode<BufferedStream, Binary>.GenerateReadMethod(typeof(T), Settings, false);
                ReadMethods<T>.Methods[_settingsIndex] = method;
            }

            return method(_stream, this);
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
                _lastWriteMethod(value, _stream, this);
                return;
            }

            ref var method = ref VirtualWriteMethods.GetOrAddValueRef(type);

            if (method == null)
            {
                method = (Action<object, BufferedStream, Binary>) DynamicCode<BufferedStream, Binary>.GenerateWriteMethod(type, Settings, true);
            }

            _lastWriteType = type;
            _lastWriteMethod = method;

            method(value, _stream, this);
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

        private DictionarySlim<MethodInfo, Type[]> _methodParametersCache = new DictionarySlim<MethodInfo, Type[]>();
        private DictionarySlim<MethodInfo, Type[]> _methodGenericsCache = new DictionarySlim<MethodInfo, Type[]>();

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

            if(invocationCount == (IntPtr)0)
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

        private readonly DictionarySlim<DelegateID, Delegate> _delegateCache = new DictionarySlim<DelegateID, Delegate>();

        private readonly Func<object, object> _clone = CreateCloneFunc();

        private static Func<object, object> CreateCloneFunc()
        {
            var p = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, object>>(
                Expression.Call(p, typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                , p
            ).Compile();
        }

        private readonly Action<Delegate, object> _setTarget = CreateSetTargetAction();

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

        private static Type[] _parameterTypeBuffer = new Type[256];
        private static Type[][] _genericTypeBuffers = new Type[][] {new Type[1], new Type[2], new Type[3], new Type[4]};

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
            var method = WriteMethods<T>.Methods[_settingsIndex];
            if (method == null)
            {
                CheckTypes(value);

                method = (Action<T, BufferedStream, Binary>)DynamicCode<BufferedStream, Binary>.GenerateWriteMethod(value.GetType(), Settings, false);
                WriteMethods<T>.Methods[_settingsIndex] = method;
            }

            method(value, _stream, this);
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

            var method = WriteMethods<T>.Methods[_settingsIndex];
            if (method == null)
            {
                CheckTypes(value);

                method = (Action<T, BufferedStream, Binary>)DynamicCode<BufferedStream, Binary>.GenerateWriteMethod(value.GetType(), Settings, false);
                WriteMethods<T>.Methods[_settingsIndex] = method;
            }

            method(value, _stream, this);
        }

        [Conditional("DEV")]
        private void CheckTypes<T>(T value)
        {
            if (typeof(T) != value.GetType())
            {
                throw new InvalidOperationException("Actual type found while attempting to write a sealed type does not match");
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        void ISerializer.QueueAfterDeserializationHook(Action<object> method, object instance)
        {
            _deserializationHooks.Add((method,instance));
        }
    }
}
