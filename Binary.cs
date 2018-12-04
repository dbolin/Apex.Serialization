using System;
using System.Collections.Generic;
using System.IO;
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

    public sealed partial class Binary : ISerializer, IDisposable
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

        private DictionarySlim<MethodInfo, Type[]> _methodParametersCache = new DictionarySlim<MethodInfo, Type[]>();
        private DictionarySlim<MethodInfo, Type[]> _methodGenericsCache = new DictionarySlim<MethodInfo, Type[]>();

        private readonly DictionarySlim<DelegateID, Delegate> _delegateCache = new DictionarySlim<DelegateID, Delegate>();
        private readonly Func<object, object> _clone = CreateCloneFunc();
        private readonly Action<Delegate, object> _setTarget = CreateSetTargetAction();

        private Type[] _parameterTypeBuffer = new Type[256];
        private Type[][] _genericTypeBuffers = new Type[][] { new Type[1], new Type[2], new Type[3], new Type[4] };

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

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
