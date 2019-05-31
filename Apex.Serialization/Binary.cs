using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;
using BinaryReader = Apex.Serialization.Extensions.BinaryReader;
using BinaryWriter = Apex.Serialization.Extensions.BinaryWriter;
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
        internal static bool Instantiated;

        public ImmutableSettings Settings { get; } = Serialization.Settings.Default;
        private readonly int _settingsIndex;
        internal readonly BufferedStream _stream;

        List<object> ISerializer.LoadedObjectRefs => _loadedObjectRefs;

        private readonly DictionarySlim<object, int> _savedObjectLookup;
        private readonly List<object> _loadedObjectRefs;

        private readonly List<object> _internedObjects = new List<object>();

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

        private readonly IBinaryWriter _binaryWriter;
        private readonly IBinaryReader _binaryReader;

        public Binary()
        {
            Instantiated = true;
            _binaryWriter = new BinaryWriter(this);
            _binaryReader = new BinaryReader(this);
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
            Instantiated = true;
            _binaryWriter = new BinaryWriter(this);
            _binaryReader = new BinaryReader(this);
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

            WriteObjectEntry(value);

            _stream.Flush();

            if (Settings.SerializationMode == Mode.Graph)
            {
                _savedObjectLookup.Clear();
                if (_internedObjects.Count > 0)
                {
                    foreach (var o in _internedObjects)
                    {
                        _savedObjectLookup.GetOrAddValueRef(o) = _savedObjectLookup.Count;
                    }
                }
            }
            _savedTypeLookup.Clear();
        }

        public T Read<T>(Stream inputStream)
        {
            _stream.ReadFrom(inputStream);

            var result = ReadObjectEntry<T>();

            if (Settings.SerializationMode == Mode.Graph)
            {
                _loadedObjectRefs.Clear();
                if (_internedObjects.Count > 0)
                {
                    _loadedObjectRefs.AddRange(_internedObjects);
                }
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

            return result;
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

        public void Intern(object o)
        {
            if(Settings.SerializationMode != Mode.Graph)
            {
                throw new InvalidOperationException("Object interning is only supported for Graph serialization");
            }

            _internedObjects.Add(o);

            _savedObjectLookup.GetOrAddValueRef(o) = _savedObjectLookup.Count;
            _loadedObjectRefs.Add(o);
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
