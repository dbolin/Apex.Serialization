using FluentAssertions;
using System;
using System.IO;
using Apex.Serialization.Internal;
using System.Diagnostics;
using Apex.Serialization.Internal.Reflection;
using System.Reflection;
using System.Linq;

namespace Apex.Serialization.Tests
{
    public abstract class AbstractSerializerTestBase
    {
        private MemoryStream _stream = new MemoryStream();
        internal Action<ISerializer> _setupSerializer;
        internal Action<ISerializer> _setupSerializerGraph;
        internal Action<Settings> _modifySettings;

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        protected AbstractSerializerTestBase()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
        }

        private ISerializer[] ConstructSerializers(Func<Settings, bool>? filter)
        {
            var settings = new[]
            {
                new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true },
                new Settings { SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true },
                new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = false },
                new Settings { SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = false },
                new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true, InliningMaxDepth = 0 },
                new Settings { SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = true, InliningMaxDepth = 0 },
                new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = false, InliningMaxDepth = 0 },
                new Settings { SerializationMode = Mode.Graph, AllowFunctionSerialization = true, SupportSerializationHooks = true, UseSerializedVersionId = false, InliningMaxDepth = 0 }
            };

            foreach(var s in settings)
            {
                _modifySettings?.Invoke(s);
            }

            var innerDefs = GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var def in innerDefs)
            {
                foreach(var setting in settings)
                {
                    setting.MarkSerializable(def);
                }
            }

            var additionalTypesMethod = GetType().GetMethod("SerializableTypes");
            if (additionalTypesMethod != null)
            {
                var types = (Type[]?)additionalTypesMethod.Invoke(null, null);
                if (types != null)
                {
                    foreach (var type in types)
                    {
                        foreach(var setting in settings)
                        {
                            setting.MarkSerializable(type);
                        }
                    }
                }
            }
            
            var treeSerializers = settings
                .Where(x => x.SerializationMode == Mode.Tree)
                .Where(x => filter?.Invoke(x) ?? true)
                .Select(x => (ISerializer)Binary.Create(x))
                .Select(x => { _setupSerializer?.Invoke(x); return x; });
            var graphSerializers = settings
                .Where(x => x.SerializationMode == Mode.Graph)
                .Where(x => filter?.Invoke(x) ?? true)
                .Select(x => (ISerializer)Binary.Create(x))
                .Select(x => { _setupSerializerGraph?.Invoke(x); return x; });

            return treeSerializers.Concat(graphSerializers).ToArray();
        }

        private void DisposeSerializers(ISerializer[] serializers)
        {
            foreach(var s in serializers)
            {
                if (s is IDisposable d)
                {
                    d.Dispose();
                }
            }
        }

        protected T RoundTrip<T>(T obj, Func<Settings, bool>? filter = null)
        {
            var loaded = RunTest(obj, (r, o) => r.Should().BeEquivalentTo(o, c => c.AllowingInfiniteRecursion()), filter);
            RunTest(new[] { obj, obj }, (r, o) => r.Should().BeEquivalentTo(o, c => c.AllowingInfiniteRecursion()), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);
            RunTest2(obj, (r, o) => r.Should().BeEquivalentTo(o, c => c.AllowingInfiniteRecursion()), filter);
            RunTest2(new[] { obj, obj }, (r, o) => r.Should().BeEquivalentTo(o, c => c.AllowingInfiniteRecursion()), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);

            return loaded;
        }

        private T RunTest<T>(T obj, Action<T, T> assertion, Func<Settings, bool>? filter)
        {
            var serializers = ConstructSerializers(filter);
            T loaded = default!;
            foreach (var s in serializers)
            {
                _stream.Seek(0, SeekOrigin.Begin);
                s.Write(obj, _stream);
                _stream.Seek(0, SeekOrigin.Begin);
                loaded = s.Read<T>(_stream);
                assertion(loaded, obj);
            }
            DisposeSerializers(serializers);

            return loaded;
        }

        private void RunTest2<T>(T obj, Action<T, T> assertion, Func<Settings, bool>? filter)
        {
            var serializers = ConstructSerializers(filter);
            for(int i=0;i<serializers.Length;++i)
            {
                var s = serializers[i];
                _stream.Seek(0, SeekOrigin.Begin);
                s.Write(obj, _stream);

                DisposeSerializers(serializers);
                serializers = ConstructSerializers(filter);
                s = serializers[i];

                _stream.Seek(0, SeekOrigin.Begin);
                var loaded = s.Read<T>(_stream);
                assertion(loaded, obj);
            }

            DisposeSerializers(serializers);
        }

        protected T RoundTrip<T>(T obj, Func<T, T, bool> check, Func<Settings, bool>? filter = null)
        {
            var loaded = RunTest(obj, (r, o) => check(o, r).Should().BeTrue(), filter);
            RunTest(new[] { obj, obj }, (r, o) => check(o[1], r[1]).Should().BeTrue(), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);
            RunTest2(obj, (r, o) => check(o, r).Should().BeTrue(), filter);
            RunTest2(new[] { obj, obj }, (r, o) => check(o[1], r[1]).Should().BeTrue(), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);

            return loaded;
        }

        protected T RoundTrip<T>(T obj, Action<T, T> check, Func<Settings, bool>? filter = null)
        {
            var loaded = RunTest(obj, (r, o) => check(o, r), filter);
            RunTest(new[] { obj, obj }, (r, o) => check(o[1], r[1]), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);
            RunTest2(obj, (r, o) => check(o, r), filter);
            RunTest2(new[] { obj, obj }, (r, o) => check(o[1], r[1]), s => filter?.Invoke(s) ?? true && s.SerializationMode == Mode.Graph);

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