using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;

namespace Apex.Serialization
{
    internal static class WriteMethods<T, TStream, TSettingGen>
        where TStream : struct, IBinaryStream
    {
        public delegate void WriteSealed(T obj, ref TStream stream, Binary<TStream, TSettingGen> binary);

        public static WriteSealed? Method;
    }

    internal static class ReadMethods<T, TStream, TSettingGen>
        where TStream : struct, IBinaryStream
    {
        public delegate T ReadSealed(ref TStream stream, Binary<TStream, TSettingGen> binary);

        public static ReadSealed? Method;
    }

    internal sealed partial class Binary<TStream, TSettingGen> : ISerializer, IBinary where TStream : struct, IBinaryStream
    {
        public delegate void WriteObject(object obj, ref TStream stream, Binary<TStream, TSettingGen> binary);
        public delegate object ReadObject(ref TStream stream, Binary<TStream, TSettingGen> binary);

        public ImmutableSettings Settings { get; }
        internal TStream _stream;

        internal List<object> LoadedObjectRefs => _loadedObjectRefs;

        private readonly DictionarySlim<object, int> _savedObjectLookup;
        private readonly List<object> _loadedObjectRefs;

        private readonly List<object> _internedObjects = new List<object>();

        internal List<Type> LoadedTypeRefs => _loadedTypeRefs;

        private readonly DictionarySlim<Type, int> _savedTypeLookup = new DictionarySlim<Type, int>();
        private readonly List<Type> _loadedTypeRefs = new List<Type>();

        private readonly DictionarySlim<Type, WriteObject> VirtualWriteMethods = new DictionarySlim<Type, WriteObject>();
        private readonly DictionarySlim<Type, ReadObject> VirtualReadMethods = new DictionarySlim<Type, ReadObject>();

        private Type? _lastWriteType;
        private WriteObject? _lastWriteMethod;

        private Type? _lastReadType;
        private ReadObject? _lastReadMethod;

        private Type? _lastRefType;
        private int _lastRefIndex;

        private readonly TypeLookup<Type> _knownTypes = new TypeLookup<Type>();

        private readonly List<ValueTuple<Action<object, object>, object>> _deserializationHooks;

        private DictionarySlim<MethodInfo, Type[]> _methodParametersCache = new DictionarySlim<MethodInfo, Type[]>();
        private DictionarySlim<MethodInfo, Type[]> _methodGenericsCache = new DictionarySlim<MethodInfo, Type[]>();

        private readonly DictionarySlim<DelegateID, Delegate> _delegateCache = new DictionarySlim<DelegateID, Delegate>();
        private static readonly Func<object, object> _clone = CreateCloneFunc();
        private static readonly Action<Delegate, object> _setTarget = CreateSetTargetAction();

        private Type[] _parameterTypeBuffer = new Type[256];
        private Type[][] _genericTypeBuffers = new Type[][] { new Type[1], new Type[2], new Type[3], new Type[4] };

        private readonly IBinaryWriter _binaryWriter;
        private readonly IBinaryReader _binaryReader;

        private object _customContext;

#pragma warning disable CS8618 // Non-nullable field is uninitialized.
        internal Binary(ImmutableSettings settings, TStream stream)
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
        {
            Settings = settings;
            _binaryWriter = new BinaryWriter<TStream, TSettingGen>(this);
            _binaryReader = new BinaryReader<TStream, TSettingGen>(this);
            _stream = stream;
            if (Settings.SerializationMode == Mode.Graph)
            {
                _savedObjectLookup = new DictionarySlim<object, int>(16);
                _loadedObjectRefs = new List<object>(16);
            }

            if (Settings.SupportSerializationHooks)
            {
                _deserializationHooks = new List<ValueTuple<Action<object, object>, object>>();
            }
        }

        public void Write<T>(T value, Stream outputStream)
        {
            _lastRefType = null;
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
                    a.Item1(a.Item2, _customContext);
                }
                _deserializationHooks.Clear();
            }

            return result;
        }

        public void Precompile(Type type)
        {
            if(type.IsGenericTypeDefinition)
            {
                throw new ArgumentException("Cannot compile for a generic type definition", nameof(type));
            }

            lock (VirtualReadMethods)
            {
                ref var readMethod = ref VirtualReadMethods.GetOrAddValueRef(type);
                if (readMethod == null)
                {
                    readMethod = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadObject>(type, Settings, true);
                }
            }

            lock (VirtualWriteMethods)
            {
                ref var writeMethod = ref VirtualWriteMethods.GetOrAddValueRef(type);
                if (writeMethod == null)
                {
                    writeMethod = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteObject>(type, Settings, true);
                }
            }

            if(type.IsSealed || type.IsValueType)
            {
                typeof(Binary<TStream, TSettingGen>).GetMethods().Single(x => x.IsGenericMethod && x.Name == "Precompile")
                    .MakeGenericMethod(type).Invoke(this, Array.Empty<object>());
            }
        }

        public void Precompile<T>()
        {
            ref var readMethod = ref ReadMethods<T, TStream, TSettingGen>.Method;
            if (readMethod == null)
            {
                readMethod = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateReadMethod<ReadMethods<T, TStream, TSettingGen>.ReadSealed>(typeof(T), Settings, false);
            }
            ref var writeMethod = ref WriteMethods<T, TStream, TSettingGen>.Method;
            if (writeMethod == null)
            {
                writeMethod = DynamicCode<TStream, Binary<TStream, TSettingGen>>.GenerateWriteMethod<WriteMethods<T, TStream, TSettingGen>.WriteSealed>(typeof(T), Settings, false);
            }
        }

        public void Intern(object o)
        {
            if (Settings.SerializationMode != Mode.Graph)
            {
                throw new InvalidOperationException("Object interning is only supported for Graph serialization");
            }

            _internedObjects.Add(o);

            _savedObjectLookup.GetOrAddValueRef(o) = _savedObjectLookup.Count;
            _loadedObjectRefs.Add(o);
        }

        public void SetCustomHookContext<T>(T context)
            where T : class
        {
            _customContext = context;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
