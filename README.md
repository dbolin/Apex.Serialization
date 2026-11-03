# Apex.Serialization

A high performance contract-less binary serializer capable of handling data trees or object graphs.

Suitable for realtime workloads where the serialized data will not persist for long, as most assembly changes will render the data format incompatible with older versions. Performance is optimized for throughput at the expense of initialization time.

### Status

[![Build Status](https://numenfall.visualstudio.com/Libraries/_apis/build/status/dbolin.Apex.Serialization?branchName=master)](https://numenfall.visualstudio.com/Libraries/_build/latest?definitionId=11&branchName=master) <!-- [![Tests](https://img.shields.io/azure-devops/tests/numenfall/Libraries/11.svg?compact_message)](https://numenfall.visualstudio.com/Libraries/_build/latest?definitionId=11&branchName=master) -->
[![Code Coverage](https://img.shields.io/azure-devops/coverage/numenfall/Libraries/11/master.svg)](https://numenfall.visualstudio.com/Libraries/_build/latest?definitionId=11&branchName=master)

[Nuget Package](https://www.nuget.org/packages/Apex.Serialization/)

Using the latest package version is recommended, but 1.3.4 is the latest version that supportes netstandard or .NET framework targets.

### Use Cases

#### Good
- Caching of data that can be reproduced if lost or unable to be deserialized
- Distributed processing where all workers are using the same runtime/application versions and are hosted on the same type of hardware

#### Bad
- Any type of long term storage, such as save files or data archiving, since changes to schema, runtime, library versions, etc. could make deserialization no longer possible

### Limitations

As the serialization is contract-less, the binary format produced depends on precise characteristics of the types serialized. Most changes to types, such as adding or removing fields, renaming types, or changing relationships between types will break compatibility with previously serialized data.  Serializing and deserializing between different chip architectures and .NET runtimes is not supported.

The reader works on managed buffers with bounds checks, so data that is malformed or was written by an incompatible version makes `Read` throw rather than read or write outside them.  The exceptions it raises include `EndOfStreamException` when the data ends early, `InvalidDataException` for a length, reference, delegate or object type that can't be valid, `InvalidOperationException` for a type it can't resolve, that isn't marked serializable or (with `UseSerializedVersionId`) that has a different version id, `NotSupportedException` for a type it can't read (one holding pointers or handles, or a delegate when functions aren't allowed) or an array too large to check against a stream that can't seek, and `InsufficientExecutionStackException` when the data nests deeper than the thread's stack allows.  A stored type name resolves among the loaded assemblies, and loads an assembly only when one with that name is in the runtime's trusted platform assemblies list (the framework and the application's own dependencies); a type the named assembly forwards elsewhere is bound the way the runtime binds that assembly's references.  A length is checked against what the stream can still supply before anything is allocated for it, so what a corrupt length allocates stays proportional to the size of the data (except for an array of a value type that has a custom read action or reads no bytes, whose length isn't checked).

Reading checks the stack at each level of nesting, but writing doesn't.  Data nested deeper than the reading thread's stack allows writes but then fails to read: measured on .NET 10 x64, a Release build reads between about 1,800 and 19,000 levels on a 1 MiB stack, depending on what nests.  Nesting deeper than the writing thread's stack allows overflows it, which ends the process, so keep nesting well under those depths or read and write on a thread with a larger stack.

Reading data from another process or machine also needs the caller to:
- catch `Exception` around `Read`, since the list above isn't complete (a reference to an object of the wrong type throws `InvalidCastException`, for example) and custom read actions and `[AfterDeserialization]` hooks can throw their own
- call `Settings.AllowDelegateMethodsFrom` when functions are serialized, or the data can name any method whose signature matches the delegate type, and the application calls it later.  It checks the assembly declaring each method and the type arguments of a generic method, but not the type arguments of the generic type a method belongs to.  Writing applies the same check and throws `InvalidOperationException`, so a delegate the reader would refuse fails when it's written
- treat a count that a custom read action reads as a hint, not as a size to allocate

Some types aren't supported:
- Objects that use randomized hashing or other runtime specific data to determine their behavior (including HashSet<>, Dictionary<,> and their immutable counterparts) unless you specifically use a comparer/objects that don't have that randomization
- Objects containing pointers or handles to unmanaged resources
- BlockingCollection\<> and types in System.Collections.Concurrent
- Non-generic standard collections
- Delegates to dynamic methods, such as those `DynamicMethod` and usually `LambdaExpression.Compile` create, which have no declaring type for the reader to find them in (writing one throws `NotSupportedException`)

Requires code generation capabilities

### Migrating to version 2.x

Version 2 adds type whitelisting, which means no types can be serialized unless marked by calling Settings.MarkSerializable(Type | Func<Type, bool>).  To restore the previous behavior for backwards compatibility you can simply pass a function that always returns true.

### Migrating to version 8.x

Version 8 reads through bounds-checked managed buffers, so malformed data throws instead of reading out of bounds, and the exceptions it throws have changed: data that ends early throws `EndOfStreamException`, and an impossible length, a reference to a type or object that hasn't been read, or a delegate that can't be valid throws `InvalidDataException` (Limitations lists the others).  Some delegates are refused on write as well: one to a dynamic method throws `NotSupportedException`, and once `Settings.AllowDelegateMethodsFrom` (new in version 8) is called, one to a method from another assembly, or to a generic method over a type from one, throws `InvalidOperationException`.  The wire format is unchanged, so valid data written by version 7 reads back the same, within the read limits Limitations describes (nesting depth, which assemblies a type name can load).

### Usage

Serialization
```csharp
var obj = ClassToSerialize();
var binarySerializer = Binary.Create(new Settings().MarkSerializable<ClassToSerializeType>());
binarySerializer.Write(obj, outputStream);
```

Deserialization
```csharp
var obj = binarySerializer.Read<SerializedClassType>(inputStream)
```

Class instances are not thread safe, static methods are thread safe unless otherwise noted in their documentation. 

Always reuse serializer instances when possible, as the instance caches a lot of data to improve performance when repeatedly serializing or deserializing objects.  Since the instances are not thread-safe, you should use an object pool or some other method to ensure that only one thread uses an instance at a time.

Fields with the [Nonserialized] attribute will not be serialized or deserialized.

A value type whose fields are all primitive (recursively) is written as one copy of its memory, and so is a one- or two-dimensional array of such values, unless a custom serializer applies to the type or to a value inside it, or one of those fields is [NonSerialized].  The padding bytes in that copy are written as zeros, so two values serialize to equal bytes when every field is equal.  Any other struct is written field by field, so a [NonSerialized] field in it reads back as its default.  An `[InlineArray]` that isn't copied whole is written as its elements, each the way a field of the element type would be.

A struct with a declared size (`StructLayout.Size`) counts as having no padding, because the bytes past its fields may hold data.  Any real padding in such a struct is written as is.  That applies to a fixed buffer (the compiler declares the size on the hidden struct it generates for each buffer) but not to the struct that holds it, whose own padding is still zeroed.  A struct with a declared size that's written field by field (because it holds a custom-serialized value or a [NonSerialized] field, say) is followed by the bytes no field covers, copied as they are.  The runtime ignores the declared size of a sequential struct holding a reference (it keeps the size of an explicit-layout one), so that struct writes only its fields.  An empty struct is written at its declared size when that's more than one byte, and as a zero byte otherwise (the compiler gives it one byte).  A type whose layout the runtime defines, such as `Vector<T>`, is copied whole.

#### Settings

You must pass a Settings object to Binary.Create that lets you choose:
- between tree or graph serialization (graph serialization is required for cases where you have a cyclical reference or need to maintain object identity)
- whether functions should be serialized, and which assemblies their methods may come from when they're read
- whether serialization hooks should be called (any methods with the [AfterDeserialization] attribute will be called after the object graph is completely deserialized.)
- whether to disable inlining (reduces startup time and stack frame sizes at the cost of throughput)
- whether to disable flattening of class hierachies (has an effect similar to disabling inlining)
- whether to emit autogenerated type IDs, which will prevent incompatible data from being deserialized at the cost of some overhead per object
- custom serialization actions
- what types are allowed to be serialized

#### Serialization boundaries

A boundary type marks a place where the object graph stops.  Instances of it are written as a reference and type header with no field payload, so serialization never traverses into them, and every occurrence is resolved on read to an instance you supply.  This lets you serialize a small subgraph that references a large or unserializable object without dragging that object along.

```csharp
var settings = new Settings { SerializationMode = Mode.Graph }
    .MarkSerializable(typeof(Player))
    .MarkBoundary<GameState>();

var serializer = Binary.Create(settings);
serializer.Write(player, outputStream);

// On read, supply the instance that GameState references should resolve to.
serializer.SetBoundarySubstitute(currentGameState);
var loadedPlayer = serializer.Read<Player>(inputStream);   // loadedPlayer.State == currentGameState
```

Because boundary types are never traversed, they need no `MarkSerializable` call and may contain members the serializer could not otherwise handle, such as pointers or handles.

Behavior and limitations:

- **Graph mode only.**  Boundary resolution depends on the reference table that only graph serialization maintains, so `Binary.Create` rejects boundary settings in tree mode.
- **Substitutes last one read.**  They are cleared after every successful `Read`, so register a substitute before each read. 
- **The substitute must match the concrete types in the payload.**  `SetBoundarySubstitute` checks only that the substitute is assignable to the marked type; assignability to each concrete type in the payload is checked during the read, which throws an `InvalidOperationException` naming the types involved if it does not hold.
- **Subclasses are boundaries too, keyed by the marked type.**  Marking a base class makes all of its subclasses boundaries, and the substitute is registered under the marked base.
- **One substitute per marked type, so all occurrences alias it.**  Distinct instances in the written graph all resolve to the single substitute; object identity within the boundary type is not preserved.  A payload holding two *sibling subclasses* of one marked base cannot be satisfied, since no single instance is assignable to both.
- **Do not use a boundary type as a dictionary or set key.**  Since every occurrence resolves to the same instance, keys that were distinct when written collapse to one identity on read, which corrupts the collection's lookup.  This is inherent to aliasing, not a bug that can be fixed by the caller.
- **Marking a boundary changes the payload format.**  The boundary configuration contributes to the autogenerated type IDs, so data written with boundary settings cannot be read with non-boundary settings, or with settings marking different types, while `UseSerializedVersionId` is on.  Callers who mark no boundary are unaffected.

#### Performance

Performance is a feature!  Apex.Serialization is an extremely fast binary serializer.  See [benchmarks](Benchmarks.md) for comparisons with other fast binary serializers.

### Custom serialization/deserialization

You can define custom serialization and deserialization simply by calling
```csharp
Settings.RegisterCustomSerializer<CustomType>(writeAction, readAction)
```

In order for custom serialization to be used, the SupportSerializationHooks property on the Settings used to instantiate the Binary class must also be set to true.

A registration applies to every value of the matching type, including a value type that would otherwise be written as one copy of its memory (a struct holding such a value is then written field by field, and an `[InlineArray]` element by element), and `Nullable<T>` or `Nullable<>` can be registered as well.  It doesn't apply to bool, char, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, decimal, IntPtr, UIntPtr, Guid, enums, strings, delegates or Type objects.

A value type's read Action gets a copy it can't fill in, so register a custom instantiator with `Settings.RegisterCustomInstantiator<CustomType>(readFunc)` to produce the value.  The instantiator runs first and may read object references in graph mode (unless it's read from inside a class's instantiator).  The read Action still runs after it, so between them they must read exactly what the write Action wrote, or the rest of the stream is read out of step.  Without an instantiator the value reads back as its default.

Both the write Action and read Action will be called with an instance of the type being serialized/deserialized and a BinaryWriter/BinaryReader interface which exposes three methods:

```csharp
        void Write(string input);
        void Write<T>(T value) where T : struct;
        void WriteObject<T>(T value);
```

The Actions can optionally take a third parameter for context, which is set on the Binary instance with SetCustomHookContext.

The reader has corresponding methods for reading back the values.  Behavior of the generic Write/Read method when passed a non-primitive is undefined.  If multiple customer serializers match an object, they will all be called in the order in which they were registered.

#### Tips for best performance

- Use sealed type declarations when possible - this allows the serializer to skip writing any type information
- Create empty constructors (or constructors that assign to every field from parameters matching the field types) for classes that will be serialized/deserialized a lot (only helps if there's no inline field initialization as well)
- Use different serializer instances for different workloads (e.g. one for serializing a few objects at a time and one for large graphs), and pool serializer instances
- Don't inherit from standard collections
- Call `Precompile` at startup (e.g. on a background thread) for the types you will serialize — code generation results are cached process-wide per settings, so first writes and reads of warmed types skip their codegen cost
