# Apex.Serialization

A high performance contract-less binary serializer

Suitable for realtime workloads where the serialized data will not persist for long, as most assembly changes will render the data format incompatible with older versions.

### Limitations

As the serialization is contract-less, the binary format produced depends on precise characteristics of the types serialized. Most changes to types, such as adding or removing fields, renaming types, or changing relationships between types will break compatibility with previously serialized data.

For performance reasons, the serializer and deserializer make extensive use of pointers and raw memory access.  This will usually cause attempting to deserialize incompatible data to immediately crash the application instead of throwing an exception.

NEVER deserialize data from an untrusted source.

Some types aren't supported:
- Enumerators
- Objects that use randomized hashing or other runtime specific data to determine their behavior
- Structs with explicit layout that have reference fields

Requires code generation capabilities, most likely only operates under full trust

### Usage

Serialization
```csharp
var obj = ClassToSerializer();
var binarySerializer = new Binary();
binarySerializer.Write(obj, outputStream);
```

Deserialization
```csharp
var obj = binarySerializer.Read<SerializedClassType>(inputStream)
```

Always reuse serializer instances when possible, as the instance caches a lot of data to improve performance when repeatedly serializing or deserializing objects.

#### Tips for best performance

Use sealed type declarations when possible - this allows the serializer to skip writing any type information
Create empty constructors for classes that will be serialized/deserialized a lot
Use different serializer instances for different workloads (e.g. one for serializing a few objects at a time and one for large graphs)
