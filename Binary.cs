using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                WriteSealedInternal(value);
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
                result = ReadSealedInternal<T>();
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

        internal T ReadSealedInternal<T>()
        {
            if (!StaticTypeInfo<T>.IsValueType)
            {
                if (ReadObjectRefHeader(out T result))
                {
                    return result;
                }
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

        private DictionarySlim<MethodInfo, Type[]> _methodParametersCache = new DictionarySlim<MethodInfo, Type[]>();

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
            _stream.ReserveSize(4);
            var parameters = GetMethodParameterTypes(value.Method);
            _stream.Write(parameters.Length);
            for (int i = 0; i < parameters.Length; ++i)
            {
                _stream.ReserveSize(4);
                WriteTypeRefInternal(parameters[i]);
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

        internal Delegate ReadFunctionInternal()
        {
            var delegateType = ReadTypeRefInternal();
            var declaringType = ReadTypeRefInternal();
            var methodName = _stream.Read();
            _stream.ReserveSize(4);
            var parameterCount = _stream.Read<int>();
            var parameterTypeList = new Type[parameterCount];
            for (int i = 0; i < parameterCount; ++i)
            {
                parameterTypeList[i] = ReadTypeRefInternal();
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

                for (int j = 0; j < parameterCount; ++j)
                {
                    if (m.ParameterTypes[j] != parameterTypeList[j])
                    {
                        goto next;
                    }
                }

                delegateMethod = m.MethodInfo;
                break;
                next: ;
            }
            if (hasTarget)
            {
                var target = ReadInternal();
                result = Delegate.CreateDelegate(delegateType, target, delegateMethod);
            }
            else
            {
                result = Delegate.CreateDelegate(delegateType, delegateMethod);
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

        internal void WriteSealedInternal<T>(T value)
        {
            if (!StaticTypeInfo<T>.IsValueType)
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
