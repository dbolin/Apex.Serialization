using FluentAssertions;
using System;
using System.IO;
using Apex.Serialization.Internal;
using System.Diagnostics;
using Apex.Serialization.Internal.Reflection;

namespace Apex.Serialization.Tests
{
    public abstract class AbstractSerializerTestBase
    {
        internal ISerializer _serializer;
        internal ISerializer _serializerGraph;
        internal MemoryStream _stream = new MemoryStream();

        protected AbstractSerializerTestBase()
        {
            _serializer = (ISerializer)Binary.Create(new Settings {AllowFunctionSerialization = true, SupportSerializationHooks = true});
            _serializerGraph = (ISerializer)Binary.Create(new Settings {SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true});
        }

        protected T RoundTrip<T>(T obj)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            loaded2.Should().BeEquivalentTo(obj2);

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Func<T, T, bool> check)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);

            check(obj, loaded).Should().Be(true);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded).Should().BeTrue();

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            check(obj2[1], loaded2[1]).Should().BeTrue();

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Action<T, T> check)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializer.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializer.Read<T>(_stream);

            check(obj, loaded);

            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            check(obj2[1], loaded2[1]);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            loaded2.Should().BeEquivalentTo(obj2);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Func<T, T, bool> check)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded).Should().BeTrue();

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            check(obj2[1], loaded2[1]).Should().BeTrue();

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Action<T, T> check)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded);

            var obj2 = new[] { obj, obj };
            _stream.Seek(0, SeekOrigin.Begin);
            _serializerGraph.Write(obj2, _stream);

            _stream.Seek(0, SeekOrigin.Begin);
            var loaded2 = _serializerGraph.Read<T[]>(_stream);

            check(obj2[1], loaded2[1]);

            return loaded;
        }

        [Conditional("DEBUG")]
        protected void TypeShouldUseEmptyConstructor(Type t)
        {
            Cil.TypeUsesEmptyConstructor(t).Should().BeTrue();
        }

        [Conditional("DEBUG")]
        protected void TypeShouldUseFullConstructor(Type t)
        {
            Cil.TypeUsesFullConstructor(t).Should().BeTrue();
        }

        [Conditional("DEBUG")]
        protected void TypeShouldNotUseConstructor(Type t)
        {
            Cil.TypeUsesEmptyConstructor(t).Should().BeFalse();
            Cil.TypeUsesFullConstructor(t).Should().BeFalse();
        }
    }
}