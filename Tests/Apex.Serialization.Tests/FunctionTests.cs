using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class FunctionTests : AbstractSerializerTestBase
    {
        public class TestClass
        {
            private static int StaticValue = 4;
            private int Value = 3;

            public int Method()
            {
                return Value;
            }

            public static int StaticMethod()
            {
                return StaticValue;
            }

            public static int StaticMethod(int param)
            {
                return StaticValue;
            }
        }

        public class Test
        {
            public Func<int>? A;
            public Func<int>? B;
            public Func<int>? C;
            public Func<int>? D;
        }

        public class Test2
        {
            public object? A;
            public object? B;
            public object? C;
            public object? D;
        }

        public class Test3<T>
        {
            public static T F1(T value)
            {
                return default!;
            }

            public T F2(T value)
            {
                return default!;
            }

            public T2 F3<T2>(T value)
                where T2 : struct
            {
                return default;
            }

            public void F4<T4>()
            {
            }
        }

        public class Animal
        {
            public virtual string Speak() => "animal";
        }

        public sealed class Dog : Animal
        {
            public override string Speak() => "dog";
        }

        public class OpenCall
        {
            public Func<Animal, string>? Speak;
        }

        public delegate void SelfEnding(SelfEnding self, IAsyncResult result);

        public class OpenEnd
        {
            public SelfEnding? End;
        }

        public sealed class Step
        {
            public string Name = "";

            public void Run(List<string> log) => log.Add(Name);
        }

        public class Steps
        {
            public Action<List<string>>? Run;
        }

        [Fact]
        public void Functions()
        {
            var testClass = new TestClass();
            var closureInt = 6;
            var x = new Test
            {
                A = testClass.Method,
                B = TestClass.StaticMethod,
                C = () => 5,
                D = () => closureInt
            };

            var y = RoundTrip(x, (a,b) => true);

            y.A().Should().Be(3);
            y.B().Should().Be(4);
            y.C().Should().Be(5);
            y.D().Should().Be(6);
        }

        [Fact]
        public void FunctionsInObjectFields()
        {
            var testClass = new TestClass();
            var closureInt = 6;
            var x = new Test2
            {
                A = (Func<int>)testClass.Method,
                B = (Func<int>)TestClass.StaticMethod,
                C = (Func<int>)(() => 5),
                D = (Func<int>)(() => closureInt)
            };

            var y = RoundTrip(x, (a, b) => true);

            ((Func<int>)y.A!)().Should().Be(3);
            ((Func<int>)y.B!)().Should().Be(4);
            ((Func<int>)y.C!)().Should().Be(5);
            ((Func<int>)y.D!)().Should().Be(6);
        }

        [Fact]
        public void GenericFunctions()
        {
            var t = new Test3<Test3<int>>();
            var x = new Test2
            {
                A = (Func<Dictionary<string, Test3<int>>, Dictionary<string, Test3<int>>>)
                    Test3<Dictionary<string, Test3<int>>>.F1,
                B = (Func<Test3<int>, Test3<int>>) t.F2,
                C = (Func<Test3<int>, bool>) t.F3<bool>,
                D = (Action) t.F4<decimal>
            };

            RoundTrip(x, (a, b) => true);
        }

        [Fact]
        public void AnOpenDelegateOverAVirtualMethodRoundTripsAsAFreshlyCreatedOne()
        {
            var x = new OpenCall { Speak = OpenSpeak() };

            RoundTrip(x, (_, read) =>
            {
                read.Speak(new Dog()).Should().Be("dog");
                read.Speak.Equals(OpenSpeak()).Should().BeTrue();
                OpenSpeak().Equals(read.Speak).Should().BeTrue();
                read.Speak.GetHashCode().Should().Be(OpenSpeak().GetHashCode());
            });
        }

        [Fact]
        public void AnOpenDelegateOverItsOwnDelegateTypesMethodRoundTrips()
        {
            var x = new OpenEnd { End = OpenEndInvoke() };

            RoundTrip(x, (_, read) =>
            {
                read.End.Equals(OpenEndInvoke()).Should().BeTrue();
                read.End.GetHashCode().Should().Be(OpenEndInvoke().GetHashCode());
            });
        }

        [Fact]
        public void AMulticastDelegateLeftByRemovingEntriesRoundTripsWithItsRemainingEntriesInOrder()
        {
            var b = StepNamed("b");
            var d = StepNamed("d");
            var five = StepNamed("a") + b + StepNamed("c") + d + StepNamed("e");
            var x = new Steps { Run = five - d - b };

            RoundTrip(x, (_, read) => Ran(read).Should().Equal("a", "c", "e"));
        }

        [Fact]
        public void AMulticastDelegateSharingItsListWithALongerOneRoundTripsWithOnlyItsOwnEntries()
        {
            var three = StepNamed("a") + StepNamed("b") + StepNamed("c");
            // The runtime appends into the list the two delegates then share, past the end of the shorter one's entries.
            var four = new Steps { Run = three + StepNamed("d") };
            Ran(four).Should().Equal("a", "b", "c", "d");
            var x = new Steps { Run = three };

            RoundTrip(x, (_, read) => Ran(read).Should().Equal("a", "b", "c"));
        }

        private static Func<Animal, string> OpenSpeak() => typeof(Animal).GetMethod(nameof(Animal.Speak))!.CreateDelegate<Func<Animal, string>>();

        private static SelfEnding OpenEndInvoke() => typeof(SelfEnding).GetMethod(nameof(SelfEnding.EndInvoke))!.CreateDelegate<SelfEnding>();

        private static Action<List<string>> StepNamed(string name) => new Step { Name = name }.Run;

        private static List<string> Ran(Steps steps)
        {
            var log = new List<string>();
            steps.Run(log);
            return log;
        }
    }
}
