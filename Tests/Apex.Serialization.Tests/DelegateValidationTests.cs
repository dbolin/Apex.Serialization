using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Xunit;

namespace Apex.Serialization.Tests
{
    public static class DelegateValidationCounterExtensions
    {
        public static int Doubled(this DelegateValidationTests.Counter counter) => counter.Count * 2;
    }

    /// <summary>
    /// A delegate is read as the type declaring its method, the method's name and parameter types, and its target, all of
    /// which come from the stream, so each is checked before the delegate is built from them.  Settings that limit the
    /// assemblies a method may come from also refuse to write a delegate they wouldn't read.
    /// </summary>
    public class DelegateValidationTests
    {
        public delegate int RefFunc(ref int x);

        public delegate int InFunc(in int x);

        public delegate int NativeFunc();

        public sealed class Counter
        {
            public int Count;

            public int Add(int x) => Count += x;

            public int AddInto(ref int x) => x += Count;

            public int AddFrom(in int x) => Count + x;

            public int Peek() => Count;

            public void Alpha() => Count += 1;

            public void Bravo() => Count += 2;
        }

        public sealed class Decoy
        {
            public int Value;
        }

        /// <summary>
        /// Writes <see cref="First"/> before <see cref="Second"/>, so the payload names a decoy's type before the
        /// delegate's target refers to its own.
        /// </summary>
        public sealed class Holder<TDelegate> where TDelegate : Delegate
        {
            public object? First;
            public TDelegate? Second;
        }

        public struct Point
        {
            public int X;

            public int GetX() => X;
        }

        public sealed class Widget
        {
            public int Value;
        }

        public sealed class Box<T>
        {
            public T? Value;
        }

        public sealed class Scale
        {
            public int Factor;

            public int Take(int x) => x * Factor;

            public long Take(long x) => x * Factor * 2;

            public int Hold(object x) => Factor;

            public int Keep(string x) => Factor;

            public int Mark(string x) => Factor;
        }

        public static class Factory
        {
            public static Type TypeOf<T>() => typeof(T);

            public static Type StructTypeOf<T>() where T : struct => typeof(T);

            public static T Echo<T>(T value) => value;

            public static Func<T, T> LocalEcho<T>()
            {
                return Same;

                static T Same(T value) => value;
            }
        }

        private static readonly Assembly TestAssembly = typeof(DelegateValidationTests).Assembly;

        private static IBinary Serializer(params Assembly[] allowedAssemblies)
        {
            var settings = new Settings { AllowFunctionSerialization = true, UseSerializedVersionId = false }.MarkSerializable(_ => true);
            if (allowedAssemblies.Length > 0)
            {
                settings.AllowDelegateMethodsFrom(allowedAssemblies);
            }

            return Binary.Create(settings);
        }

        private static byte[] Write<T>(T value)
        {
            using var binary = Serializer();
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static byte[] WriteDelegate(Delegate value) => Write(value);

        [Fact]
        public void ADelegateNamingAMethodTheTypeDoesntDeclareIsRejected()
        {
            var counter = new Counter();
            var payload = Write<Action>(counter.Alpha);
            var nameAt = FirstDifference(payload, Write<Action>(counter.Bravo));
            Encoding.Unicode.GetString(payload, nameAt, 10).Should().Be("Alpha");
            var mutated = payload.ToArray();
            Encoding.Unicode.GetBytes("Gamma").CopyTo(mutated, nameAt);

            ShouldRejectThenReadIntact<Action, InvalidDataException>(Serializer(), payload, mutated);
        }

        [Fact]
        public void ADelegateWhoseTargetIsntItsMethodsDeclaringTypeIsRejected()
        {
            // Decoy, Action and Counter are the first, second and third types the payload names.
            var holder = new Holder<Action> { First = new Decoy(), Second = new Counter { Count = 1 }.Alpha };
            var mutated = WithTargetTypeIndex(holder, h => h.Second = new Counter { Count = 2 }.Alpha, written: 3, replacement: 1);

            ShouldRejectThenReadIntact<Holder<Action>, InvalidDataException>(Serializer(), Write(holder), mutated);
        }

        [Fact]
        public void AStaticDelegateWhoseTargetIsntItsFirstParametersTypeIsRejected()
        {
            // Decoy, Func<int>, DelegateValidationCounterExtensions and Counter are the first four types the payload names.
            var holder = new Holder<Func<int>> { First = new Decoy(), Second = new Counter { Count = 1 }.Doubled };
            var mutated = WithTargetTypeIndex(holder, h => h.Second = new Counter { Count = 2 }.Doubled, written: 4, replacement: 1);

            ShouldRejectThenReadIntact<Holder<Func<int>>, InvalidDataException>(Serializer(), Write(holder), mutated);
        }

        [Fact]
        public void ADelegateToAStructsInstanceMethodRoundTrips()
        {
            var point = new Point { X = 5 };
            using var binary = Serializer();

            var read = binary.Read<Func<int>>(new MemoryStream(Write<Func<int>>(point.GetX)));

            read().Should().Be(5);
            read.Target.Should().Be(point);
        }

        [Fact]
        public void ADelegateToAMethodInAnAllowedAssemblyRoundTrips()
        {
            var counter = new Counter { Count = 3 };
            using var binary = Serializer(TestAssembly);

            var read = binary.Read<Func<int, int>>(new MemoryStream(Write<Func<int, int>>(counter.Add)));

            read(4).Should().Be(7);
            ((Counter)read.Target!).Count.Should().Be(7);
        }

        [Fact]
        public void ADelegateToAMethodOutsideTheAllowedAssembliesIsRejected()
        {
            ShouldRejectThenReadIntact<Delegate, InvalidDataException>(Serializer(TestAssembly),
                WriteDelegate((Func<int, int>)new Counter().Add), WriteDelegate((Func<string, int>)int.Parse));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AMulticastDelegateWithOneEntryOutsideTheAllowedAssembliesIsRejected(bool outsideEntryFirst)
        {
            var counter = new Counter();
            Action outside = Console.WriteLine;
            var multicast = outsideEntryFirst ? outside + counter.Alpha : (Action)counter.Alpha + outside;

            ShouldRejectThenReadIntact<Action, InvalidDataException>(Serializer(TestAssembly), Write((Action)counter.Alpha + counter.Bravo), Write(multicast));
        }

        private static readonly Dictionary<string, Func<Type>> TypeOfs = new Dictionary<string, Func<Type>>
        {
            ["Widget"] = Factory.TypeOf<Widget>,
            ["Widget[]"] = Factory.TypeOf<Widget[]>,
            ["Box<Widget>"] = Factory.TypeOf<Box<Widget>>,
            ["string"] = Factory.TypeOf<string>,
            ["Box<string>"] = Factory.TypeOf<Box<string>>,
            ["List<Widget>"] = Factory.TypeOf<List<Widget>>,
            ["Box<int>[]"] = Factory.TypeOf<Box<int>[]>,
        };

        [Theory]
        [InlineData("Widget", true)]
        [InlineData("Widget[]", true)]
        [InlineData("Box<Widget>", true)]
        [InlineData("string", false)]
        [InlineData("Box<string>", false)]
        [InlineData("List<Widget>", false)]
        [InlineData("Box<int>[]", false)]
        public void AGenericMethodIsAllowedOnlyOverTypesFromTheAllowedAssemblies(string typeArgument, bool allowed)
        {
            var payload = Write(TypeOfs[typeArgument]);

            if (allowed)
            {
                using var binary = Serializer(TestAssembly);
                binary.Read<Func<Type>>(new MemoryStream(payload))().Should().Be(TypeOfs[typeArgument]());
                WriteWith(binary, TypeOfs[typeArgument]).Should().Equal(payload);
            }
            else
            {
                ShouldRejectThenReadIntact<Func<Type>, InvalidDataException>(Serializer(TestAssembly), Write(TypeOfs["Widget"]), payload);
                ShouldRefuseToWriteThenWriteIntact(Serializer(TestAssembly), TypeOfs[typeArgument], TypeOfs["Widget"], "*Factory.TypeOf*");
            }
        }

        [Fact]
        public void ADelegateToAMethodInAnAllowedAssemblyWritesWhatUnrestrictedSettingsWriteAndReadsBack()
        {
            var counter = new Counter { Count = 3 };
            using var restricted = Serializer(TestAssembly);

            var payload = WriteWith<Func<int, int>>(restricted, counter.Add);

            payload.Should().Equal(Write<Func<int, int>>(counter.Add));
            restricted.Read<Func<int, int>>(new MemoryStream(payload))(4).Should().Be(7);
        }

        [Fact]
        public void WritingADelegateToAMethodOutsideTheAllowedAssembliesThrowsNamingItsMethodAndAssembly()
        {
            ShouldRefuseToWriteThenWriteIntact<Delegate>(Serializer(TestAssembly), (Func<string, int>)int.Parse, (Func<int, int>)new Counter().Add,
                $"*System.Int32.Parse*{typeof(int).Assembly.GetName().Name}*");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WritingAMulticastDelegateWithOneEntryOutsideTheAllowedAssembliesThrows(bool outsideEntryFirst)
        {
            var counter = new Counter();
            Action outside = Console.WriteLine;
            var multicast = outsideEntryFirst ? outside + counter.Alpha : (Action)counter.Alpha + outside;

            ShouldRefuseToWriteThenWriteIntact(Serializer(TestAssembly), multicast, (Action)counter.Alpha + counter.Bravo, "*System.Console.WriteLine*");
        }

        [Fact]
        public void WithoutAnAllowedAssemblyListAnyMethodIsAllowed()
        {
            using var binary = Serializer();
            Action outside = Console.WriteLine;

            binary.Read<Func<string, int>>(new MemoryStream(Write<Func<string, int>>(int.Parse)))("12").Should().Be(12);
            binary.Read<Action>(new MemoryStream(Write(outside + new Counter().Alpha))).GetInvocationList().Should().HaveCount(2);
            binary.Read<Func<Type>>(new MemoryStream(Write(TypeOfs["List<Widget>"])))().Should().Be(typeof(List<Widget>));
        }

        [Fact]
        public void SettingsDifferingOnlyInTheirAllowedAssembliesDontShareReaders()
        {
            var payload = Write<Func<string, int>>(int.Parse);
            using var restricted = Serializer(TestAssembly);
            using var widened = Serializer(TestAssembly, typeof(int).Assembly);
            using var unrestricted = Serializer();

            var readRestricted = () => restricted.Read<Func<string, int>>(new MemoryStream(payload));

            readRestricted.Should().Throw<InvalidDataException>();
            widened.Read<Func<string, int>>(new MemoryStream(payload))("3").Should().Be(3);
            unrestricted.Read<Func<string, int>>(new MemoryStream(payload))("4").Should().Be(4);
            readRestricted.Should().Throw<InvalidDataException>("settings that differ only in their allowed assemblies don't share readers");
            WriteWith<Func<string, int>>(widened, int.Parse).Should().Equal(payload);
        }

        [Fact]
        public void AllowingAssembliesInTwoCallsAllowsTheAssembliesOfBoth()
        {
            using var binary = Binary.Create(new Settings { AllowFunctionSerialization = true, UseSerializedVersionId = false }
                .MarkSerializable(_ => true)
                .AllowDelegateMethodsFrom(TestAssembly)
                .AllowDelegateMethodsFrom(typeof(int).Assembly));

            binary.Read<Func<int, int>>(new MemoryStream(Write<Func<int, int>>(new Counter { Count = 3 }.Add)))(4).Should().Be(7);
            binary.Read<Func<string, int>>(new MemoryStream(Write<Func<string, int>>(int.Parse)))("12").Should().Be(12);
        }

        [Fact]
        public void ADelegateToAMethodWithARefParameterRoundTrips()
        {
            var x = 4;

            RoundTrip<RefFunc>(new Counter { Count = 3 }.AddInto)(ref x).Should().Be(7);

            x.Should().Be(7);
        }

        [Fact]
        public void ADelegateToAMethodWithAnInParameterRoundTrips()
        {
            RoundTrip<InFunc>(new Counter { Count = 3 }.AddFrom)(4).Should().Be(7);
        }

        [Fact]
        public void ADelegateToAGenericMethodWhoseParameterIsItsTypeParameterRoundTrips()
        {
            var widget = new Widget();

            RoundTrip<Func<Widget, Widget>>(Factory.Echo)(widget).Should().BeSameAs(widget);
        }

        [Fact]
        public void ADelegateToALocalFunctionOfAGenericMethodRoundTrips()
        {
            var echo = Factory.LocalEcho<Widget>();
            echo.Method.IsGenericMethod.Should().BeTrue("the compiler makes the local function a generic method over the outer method's type parameter");
            var widget = new Widget();

            RoundTrip(echo)(widget).Should().BeSameAs(widget);
        }

        [Fact]
        public void OverloadsReadByOneSerializerEachReadBackAsTheOneWritten()
        {
            var scale = new Scale { Factor = 3 };
            var narrow = Write<Func<int, int>>(scale.Take);
            var wide = Write<Func<long, long>>(scale.Take);
            using var binary = Serializer();

            for (var pass = 0; pass < 2; ++pass)
            {
                binary.Read<Func<int, int>>(new MemoryStream(narrow))(5).Should().Be(15);
                binary.Read<Func<long, long>>(new MemoryStream(wide))(5).Should().Be(30);
            }
        }

        [Fact]
        public void AGenericMethodReadByOneSerializerOverTwoTypeArgumentsReadsBackOverEach()
        {
            var overInt = Write<Func<int, int>>(Factory.Echo);
            var overWidget = Write<Func<Widget, Widget>>(Factory.Echo);
            var echo = typeof(Factory).GetMethod(nameof(Factory.Echo))!;
            using var binary = Serializer();

            for (var pass = 0; pass < 2; ++pass)
            {
                binary.Read<Func<int, int>>(new MemoryStream(overInt)).Method.Should().BeSameAs(echo.MakeGenericMethod(typeof(int)));
                binary.Read<Func<Widget, Widget>>(new MemoryStream(overWidget)).Method.Should().BeSameAs(echo.MakeGenericMethod(typeof(Widget)));
            }
        }

        [Fact]
        public void AMethodAlreadyReadIsStillRejectedUnderParameterTypesItDoesntDeclare()
        {
            var scale = new Scale { Factor = 3 };
            var payload = Write<Func<string, int>>(scale.Keep);
            var nameAt = FirstDifference(payload, Write<Func<string, int>>(scale.Mark));
            Encoding.Unicode.GetString(payload, nameAt, 8).Should().Be("Keep");
            var mutated = payload.ToArray();
            Encoding.Unicode.GetBytes("Hold").CopyTo(mutated, nameAt);
            var binary = Serializer();
            binary.Read<Func<object, int>>(new MemoryStream(Write<Func<object, int>>(scale.Hold)))("").Should().Be(3);

            // Hold(object) binds to a Func<string, int>, so only the method lookup stands between the data and a delegate.
            ShouldRejectThenReadIntact<Func<string, int>, InvalidDataException>(binary, payload, mutated);
        }

        [Fact]
        public void ADelegateClosedOverATargetWhoseMethodDoesntFitItsStoredDelegateTypeIsRejected()
        {
            ShouldRejectWithDelegateType<Func<int, int>>(new Counter().Add, typeof(Action));
        }

        [Fact]
        public void AStaticDelegateWhoseMethodDoesntFitItsStoredDelegateTypeIsRejected()
        {
            ShouldRejectWithDelegateType<Func<string, int>>(int.Parse, typeof(Action<string>));
        }

        [Fact]
        public void ADelegateWhoseStoredDelegateTypeIsntADelegateIsRejected()
        {
            ShouldRejectWithDelegateType<Func<int, int>>(new Counter().Add, typeof(Widget));
        }

        [Fact]
        public void AMulticastDelegateWhoseEntriesHaveDifferentDelegateTypesIsRejected()
        {
            // ThreadStart, Counter and Action are the first, second and third types the payload names.
            var counter = new Counter();
            var holder = new Holder<Action> { First = (ThreadStart)counter.Alpha, Second = (Action)counter.Alpha + counter.Bravo };
            var payload = Write(holder);

            ShouldRejectThenReadIntact<Holder<Action>, InvalidDataException>(Serializer(), payload,
                WithLastEntrysDelegateTypeIndex(payload, nameof(Counter.Bravo), written: 3, replacement: 1));
        }

        [Fact]
        public void ADelegateToAGenericMethodOverATypeArgumentItsConstraintsRuleOutIsRejected()
        {
            var payload = Write<Func<Type>>(Factory.StructTypeOf<int>);

            ShouldRejectThenReadIntact<Func<Type>, InvalidDataException>(Serializer(), payload, WithTypeId(payload, typeof(int), typeof(string)));
        }

        private static Func<int> EmittedSeven()
        {
            var method = new DynamicMethod("Seven", typeof(int), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<int>>();
        }

        private static Func<int> CompiledSeven() => Expression.Lambda<Func<int>>(Expression.Constant(7)).Compile();

        [Theory]
        [InlineData(nameof(EmittedSeven), true)]
        [InlineData(nameof(EmittedSeven), false)]
        [InlineData(nameof(CompiledSeven), true)]
        [InlineData(nameof(CompiledSeven), false)]
        public void WritingADelegateToADynamicMethodIsRefused(string source, bool limitAssemblies)
        {
            var seven = source == nameof(EmittedSeven) ? EmittedSeven() : CompiledSeven();
            seven.Method.DeclaringType.Should().BeNull("the test needs a method with no declaring type");

            ShouldRefuseToWriteThenWriteIntact<Func<int>, NotSupportedException>(limitAssemblies ? Serializer(TestAssembly) : Serializer(),
                seven, new Counter().Peek, "*dynamic method*");
        }

        [UnmanagedCallersOnly]
        private static int NativeSeven() => 7;

        private static unsafe NativeFunc OverAnUnmanagedFunction()
        {
            var native = Marshal.GetDelegateForFunctionPointer<NativeFunc>((IntPtr)(delegate* unmanaged<int>)&NativeSeven);
            native.Method.DeclaringType.Should().Be(typeof(NativeFunc), "the test needs a delegate the runtime made for the function pointer, not one over a managed method");
            return native;
        }

        [Fact]
        public void WritingADelegateOverAnUnmanagedFunctionIsRefused()
        {
            ShouldRefuseToWriteThenWriteIntact<NativeFunc, NotSupportedException>(Serializer(),
                OverAnUnmanagedFunction(), new Counter().Peek, "*unmanaged function*");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WritingAMulticastDelegateWithAnEntryOverAnUnmanagedFunctionIsRefused(bool unmanagedEntryFirst)
        {
            NativeFunc managed = new Counter().Peek;
            var multicast = unmanagedEntryFirst ? OverAnUnmanagedFunction() + managed : managed + OverAnUnmanagedFunction();

            ShouldRefuseToWriteThenWriteIntact<NativeFunc, NotSupportedException>(Serializer(),
                multicast, managed + managed, "*unmanaged function*");
        }

        [Fact]
        public void ADelegateClosedOverNullOnItsOwnTypesInvokeIsntRefusedAsOneOverAnUnmanagedFunction()
        {
            var closedOverNull = (Action)Delegate.CreateDelegate(typeof(Action), null, typeof(Action).GetMethod(nameof(Action.Invoke))!);
            closedOverNull.Target.Should().BeNull();
            closedOverNull.Method.DeclaringType.Should().Be(typeof(Action), "the test needs a delegate that reports what one over an unmanaged function does");

            var write = () => Write(closedOverNull);

            write.Should().NotThrow();
        }

        [Fact]
        public void ADelegateClosedOverAnotherOfItsOwnTypeIsntRefusedAsOneOverAnUnmanagedFunction()
        {
            Func<int> inner = new Counter { Count = 5 }.Peek;
            Func<int> outer = inner.Invoke;
            outer.Target.Should().BeSameAs(inner);
            outer.Method.DeclaringType.Should().Be(typeof(Func<int>));

            RoundTrip(outer)().Should().Be(5);
        }

        private static int Seven() => 7;

        [Theory]
        [InlineData("closed")]
        [InlineData("static")]
        [InlineData("multicast")]
        public void WritingADelegateAllocatesNothing(string kind)
        {
            var counter = new Counter();
            var holder = new Holder<Func<int>>
            {
                Second = kind switch
                {
                    "closed" => counter.Peek,
                    "static" => Seven,
                    "multicast" => (Func<int>)counter.Peek + counter.Peek + Seven,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                },
            };
            using var binary = Serializer();
            using var stream = new MemoryStream();
            binary.Write(holder, stream);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; ++i)
            {
                stream.Position = 0;
                binary.Write(holder, stream);
            }

            (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        }

        private static T RoundTrip<T>(T value) where T : Delegate
        {
            var payload = Write(value);
            using var binary = Serializer();

            var read = binary.Read<T>(new MemoryStream(payload));

            WriteWith(binary, read).Should().Equal(payload, "the delegate read back writes the bytes it was read from");
            return read;
        }

        private static void ShouldRejectWithDelegateType<T>(T value, Type stored) where T : Delegate
        {
            var payload = Write(value);

            ShouldRejectThenReadIntact<T, InvalidDataException>(Serializer(), payload, WithTypeId(payload, typeof(T), stored));
        }

        /// <summary>
        /// Replaces the one place <paramref name="payload"/> names <paramref name="written"/> in full with the name of
        /// <paramref name="replacement"/>.
        /// </summary>
        private static byte[] WithTypeId(byte[] payload, Type written, Type replacement)
        {
            var id = TypeResolverTests.TypeId(written);
            var at = payload.AsSpan().IndexOf(id);
            at.Should().BeGreaterThanOrEqualTo(0);
            payload.AsSpan(at + 1).IndexOf(id).Should().Be(-1, "the payload names the type in full once");

            return payload.Take(at).Concat(TypeResolverTests.TypeId(replacement)).Concat(payload.Skip(at + id.Length)).ToArray();
        }

        /// <summary>
        /// Changes the delegate type index of the last invocation list entry calling <paramref name="methodName"/>, which
        /// the indices of its delegate's and method's types and the length of its method name come before.
        /// </summary>
        private static byte[] WithLastEntrysDelegateTypeIndex(byte[] payload, string methodName, int written, int replacement)
        {
            var typeIndexAt = payload.AsSpan().LastIndexOf(Encoding.Unicode.GetBytes(methodName)) - 3 * sizeof(int);
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(typeIndexAt)).Should().Be(written);

            var mutated = payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(typeIndexAt), replacement);
            return mutated;
        }

        private static byte[] WriteWith<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Requires writing <paramref name="refused"/> to throw, then <paramref name="intact"/> to write what unrestricted
        /// settings write, so the failed write left nothing behind in the serializer.
        /// </summary>
        private static void ShouldRefuseToWriteThenWriteIntact<T>(IBinary binary, T refused, T intact, string messagePattern)
        {
            ShouldRefuseToWriteThenWriteIntact<T, InvalidOperationException>(binary, refused, intact, messagePattern);
        }

        private static void ShouldRefuseToWriteThenWriteIntact<T, TException>(IBinary binary, T refused, T intact, string messagePattern)
            where TException : Exception
        {
            using (binary)
            {
                var write = () => WriteWith(binary, refused);
                write.Should().Throw<TException>().WithMessage(messagePattern);

                WriteWith(binary, intact).Should().Equal(Write(intact));
            }
        }

        /// <summary>
        /// Changes the type index of the delegate's target in <paramref name="holder"/>'s payload, found as the bytes
        /// before the target's field, which <paramref name="recount"/> changes.
        /// </summary>
        private static byte[] WithTargetTypeIndex<TDelegate>(Holder<TDelegate> holder, Action<Holder<TDelegate>> recount, int written, int replacement)
            where TDelegate : Delegate
        {
            var payload = Write(holder);
            recount(holder);
            var countAt = FirstDifference(payload, Write(holder));

            // The delegate's has-target flag, the target's null flag, then the target's type index.
            payload[countAt - 6].Should().Be(1);
            payload[countAt - 5].Should().Be(1);
            var typeIndex = payload.AsSpan(countAt - 4);
            BinaryPrimitives.ReadInt32LittleEndian(typeIndex).Should().Be(written);
            BinaryPrimitives.WriteInt32LittleEndian(typeIndex, replacement);
            return payload;
        }

        private static int FirstDifference(byte[] a, byte[] b)
        {
            a.Length.Should().Be(b.Length);
            var at = a.AsSpan().CommonPrefixLength(b);
            at.Should().BeLessThan(a.Length);
            return at;
        }

        private static void ShouldRejectThenReadIntact<T, TException>(IBinary binary, byte[] intact, byte[] rejected)
            where TException : Exception
        {
            using (binary)
            {
                var read = () => binary.Read<T>(new MemoryStream(rejected));
                read.Should().Throw<TException>();

                var value = binary.Read<T>(new MemoryStream(intact));
                using var rewritten = new MemoryStream();
                binary.Write(value, rewritten);
                rewritten.ToArray().Should().Equal(intact);
            }
        }
    }
}
