using FluentAssertions;
using System;
using System.IO;
using Apex.Serialization.Internal;

namespace Apex.Serialization.Tests
{
    public abstract class AbstractSerializerTestBase
    {
        internal ISerializer _serializer;
        internal ISerializer _serializerGraph;
        internal MemoryStream _stream = new MemoryStream();

        protected AbstractSerializerTestBase()
        {
            _serializer = new Binary(new Settings {AllowFunctionSerialization = true, SupportSerializationHooks = true});
            _serializerGraph = new Binary(new Settings {SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true});
        }

        protected T RoundTrip<T>(T obj)
        {
            _serializer.Write(obj, _stream);

            var loaded = _serializer.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            _serializerGraph.Write(obj, _stream);

            loaded = _serializerGraph.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Func<T, T, bool> check)
        {
            _serializer.Write(obj, _stream);

            var loaded = _serializer.Read<T>(_stream);

            check(obj, loaded).Should().Be(true);

            _serializerGraph.Write(obj, _stream);

            loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded).Should().Be(true);

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Action<T, T> check)
        {
            _serializer.Write(obj, _stream);

            var loaded = _serializer.Read<T>(_stream);

            check(obj, loaded);

            _serializerGraph.Write(obj, _stream);

            loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj)
        {
            _serializerGraph.Write(obj, _stream);

            var loaded = _serializerGraph.Read<T>(_stream);

            loaded.Should().BeEquivalentTo(obj);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Func<T, T, bool> check)
        {
            _serializerGraph.Write(obj, _stream);

            var loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded).Should().Be(true);

            return loaded;
        }

        protected T RoundTripGraphOnly<T>(T obj, Action<T, T> check)
        {
            _serializerGraph.Write(obj, _stream);

            var loaded = _serializerGraph.Read<T>(_stream);

            check(obj, loaded);

            return loaded;
        }
    }
}