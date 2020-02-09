using FluentAssertions;
using System;
using System.IO;
using Apex.Serialization.Internal;
using System.Diagnostics;
using Apex.Serialization.Internal.Reflection;
using System.Reflection;

namespace Apex.Serialization.Tests
{
    public abstract class AbstractSerializerTestBase : IDisposable
    {
        private ISerializer _serializer;
        private ISerializer _serializerGraph;
        private MemoryStream _stream = new MemoryStream();
        internal Action<ISerializer> _setupSerializer;
        internal Action<ISerializer> _setupSerializerGraph;

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        protected AbstractSerializerTestBase()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
        }

        private void ConstructSerializers()
        {
            Dispose();
            var treeSettings = new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true };
            var graphSettings = new Settings { SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true };

            var innerDefs = GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var def in innerDefs)
            {
                treeSettings.MarkSerializable(def);
                graphSettings.MarkSerializable(def);
            }

            var additionalTypesMethod = GetType().GetMethod("SerializableTypes");
            if (additionalTypesMethod != null)
            {
                var types = (Type[]?)additionalTypesMethod.Invoke(null, null);
                if (types != null)
                {
                    foreach (var type in types)
                    {
                        treeSettings.MarkSerializable(type);
                        graphSettings.MarkSerializable(type);
                    }
                }
            }
            _serializer = (ISerializer)Binary.Create(treeSettings);
            _setupSerializer?.Invoke(_serializer);
            _serializerGraph = (ISerializer)Binary.Create(graphSettings);
            _setupSerializerGraph?.Invoke(_serializerGraph);
        }

        public void Dispose()
        {
            DisposeSerializers();
        }

        private void DisposeSerializers()
        {
            if(_serializer is IDisposable d)
            {
                d.Dispose();
            }

            if(_serializerGraph is IDisposable d2)
            {
                d2.Dispose();
            }
        }

        protected T RoundTrip<T>(T obj)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);
            ConstructSerializers();

            loaded.Should().BeEquivalentTo(obj);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            loaded.Should().BeEquivalentTo(obj);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            loaded2.Should().BeEquivalentTo(obj2);

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Func<T, T, bool> check)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded).Should().Be(true);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded).Should().BeTrue();

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            check(obj2[1], loaded2[1]).Should().BeTrue();

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Action<T, T> check)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            check(obj2[1], loaded2[1]);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            loaded.Should().BeEquivalentTo(obj);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            loaded2.Should().BeEquivalentTo(obj2);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Func<T, T, bool> check)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded).Should().BeTrue();

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            check(obj2[1], loaded2[1]).Should().BeTrue();

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Action<T, T> check)
        {
            ConstructSerializers();
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);
            ConstructSerializers();

            check(obj, loaded);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);
            ConstructSerializers();

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);
            ConstructSerializers();

            check(obj2[1], loaded2[1]);

            return loaded;
        }

        protected void TypeShouldUseEmptyConstructor(Type t)
        {
#if DEBUG
            Cil.TypeUsesEmptyConstructor(t).Should().BeTrue();
#endif
        }

        protected void TypeShouldUseFullConstructor(Type t)
        {
#if DEBUG
            Cil.TypeUsesFullConstructor(t).Should().BeTrue();
#endif
        }

        protected void TypeShouldNotUseConstructor(Type t)
        {
#if DEBUG
            Cil.TypeUsesEmptyConstructor(t).Should().BeFalse();
            Cil.TypeUsesFullConstructor(t).Should().BeFalse();
#endif
        }
    }
}