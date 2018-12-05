using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal.Reflection;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
        where TStream : IBufferedStream
        where TBinary : ISerializer
    {
        internal static Delegate GenerateWriteMethod(Type type, ImmutableSettings settings, bool shouldWriteTypeInfo)
        {
            var fields = TypeFields.GetFields(type);

            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 12;

            var source = Expression.Parameter(shouldWriteTypeInfo ? typeof(object) : type, "source");
            var stream = Expression.Parameter(typeof(TStream), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");

            var returnTarget = Expression.Label();

            var writeStatements = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var castedSourceType = (ParameterExpression)null;

            if (shouldWriteTypeInfo)
            {
                castedSourceType = Expression.Variable(type);
                localVariables.Add(castedSourceType);
                writeStatements.Add(Expression.Assign(castedSourceType, Expression.Convert(source, type)));
            }

            var actualSource = castedSourceType ?? source;

            writeStatements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSizeNeeded)));

            if (settings.SerializationMode == Mode.Graph)
            {
                if (!type.IsValueType && !typeof(Delegate).IsAssignableFrom(type)
                    && !typeof(Type).IsAssignableFrom(type))
                {
                    writeStatements.Add(Expression.IfThen(
                        Expression.Call(output, SerializerMethods.WriteObjectRefMethod, source),
                        Expression.Return(returnTarget)));
                }
                else if (shouldWriteTypeInfo)
                {
                    writeStatements.Add(Expression.Call(stream,
                        BufferedStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo,
                        Expression.Constant(-1)));
                }
            }

            if (shouldWriteTypeInfo)
            {
                writeStatements.Add(
                    Expression.Call(output, SerializerMethods.WriteTypeRefMethod, Expression.Constant(type))
                );
                writeStatements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSizeNeeded)));
            }

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialWrite(type, output, actualSource, stream, source, fields, settings);

            if (specialExpression != null)
            {
                writeStatements.Add(specialExpression);
            }
            else
            {
                if (type.IsPointer || fields.Any(x => x.FieldType.IsPointer))
                {
                    throw new NotSupportedException("Pointers or types containing pointers are not supported");
                }
                if (typeof(SafeHandle).IsAssignableFrom(type))
                {
                    throw new NotSupportedException("Objects containing handles are not supported");
                }

                writeStatements.AddRange(fields.Select(x => GetWriteFieldExpression(x, actualSource, stream, output, settings)));
            }

            writeStatements.Add(Expression.Label(returnTarget));

            var lambda = Expression.Lambda(Expression.Block(localVariables, writeStatements), $"Write_{type.FullName}", new [] {source, stream, output}).Compile();
            return lambda;
        }

        internal static Expression HandleSpecialWrite(Type type, ParameterExpression output, ParameterExpression actualSource, ParameterExpression stream, ParameterExpression source, List<FieldInfo> fields, ImmutableSettings settings)
        {
            var primitive = HandlePrimitiveWrite(stream, output, type, actualSource);
            if(primitive != null)
            {
                return primitive;
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                return Expression.Call(output, SerializerMethods.WriteTypeRefMethod, actualSource);
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function serialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                return Expression.Call(output, SerializerMethods.WriteFunctionMethod, Expression.Convert(source, typeof(Delegate)));
            }

            var custom = HandleCustomWrite(output, type, actualSource, settings);
            if (custom != null)
            {
                return custom;
            }

            var writeStruct = WriteStructExpression(type, actualSource, stream, fields);
            if (writeStruct != null)
            {
                return writeStruct;
            }

            if(type.IsArray)
            {
                var elementType = type.GetElementType();
                var dimensions = type.GetArrayRank();

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType);

                var lengths = new List<ParameterExpression>();
                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int)));
                }

                var statements = new List<Expression>();

                statements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x, i) =>
                    Expression.Assign(x, Expression.Call(actualSource, "GetLength", Array.Empty<Type>(), Expression.Constant(i)))));
                statements.AddRange(lengths.Select(x =>
                    Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo, x)));

                if (IsBlittable(elementType) && dimensions == 1)
                {
                    statements.Add(WriteArrayOfBlittableValues(output, actualSource, stream, dimensions, elementType, elementSize));
                }
                else
                {
                    statements.Add(WriteArrayGeneral(output, actualSource, stream, dimensions, lengths, elementType, elementSize, settings));
                }
                return Expression.Block(lengths, statements);
            }

            return WriteCollection(type, output, actualSource, stream, source, settings);
        }

        private static MethodInfo _allocMethod = typeof(GCHandle).GetMethod("Alloc", new[] { typeof(object), typeof(GCHandleType) });
        private static MethodInfo _addrMethod = typeof(GCHandle).GetMethod("AddrOfPinnedObject");
        private static MethodInfo _toPointerMethod = typeof(IntPtr).GetMethod("ToPointer");
        private static MethodInfo _freeMethod = typeof(GCHandle).GetMethod("Free");

        private static Expression WriteArrayOfBlittableValues(ParameterExpression output, ParameterExpression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return Expression.Call(output, SerializerMethods.WriteArrayOfValuesMethod,
                Expression.Convert(actualSource, typeof(object)), Expression.Property(actualSource, "Length"),
                Expression.Constant(elementSize));
        }

        private static Expression WriteArrayGeneral(ParameterExpression output, ParameterExpression actualSource,
            ParameterExpression stream, int dimensions, List<ParameterExpression> lengths, Type elementType, int elementSize,
            ImmutableSettings settings)
        {
            var indices = new List<ParameterExpression>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int)));
            }

            var accessExpression = dimensions > 1
                ? (Expression) Expression.ArrayIndex(actualSource, indices)
                : Expression.ArrayIndex(actualSource, indices[0]);
            var innerWrite = WriteValue(stream, output, elementType, accessExpression, settings);
            var loop = (Expression) Expression.Block(
                Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(elementSize)),
                innerWrite);
            for (int i = 0; i < dimensions; ++i)
            {
                var breakLabel = Expression.Label();
                loop =
                    Expression.Block(
                        Expression.Assign(indices[i], Expression.Constant(0)),
                        Expression.Loop(Expression.IfThenElse(
                            Expression.GreaterThanOrEqual(indices[i], lengths[i]),
                            Expression.Break(breakLabel),
                            Expression.Block(loop, Expression.Assign(indices[i], Expression.Increment(indices[i])))
                        ), breakLabel)
                    );
            }

            return Expression.Block(indices, loop);
        }

        private static Expression WriteStructExpression(Type type, Expression source, ParameterExpression stream,
            List<FieldInfo> fields)
        {
            if (type.IsValueType)
            {
                if (type.IsExplicitLayout)
                {
                    if (fields.Any(x => !x.FieldType.IsValueType))
                    {
                        throw new NotSupportedException(
                            "Structs with explicit layout and reference fields are not supported");
                    }
                }

                if (type.IsExplicitLayout ||
                    (fields.Count <= 1 && fields.All(x => x.FieldType.IsValueType))
                )
                {
                    var method = (MethodInfo) typeof(BufferedStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("WriteValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    return Expression.Call(stream, method, source);
                }
            }

            return null;
        }

        internal static Expression WriteCollection(Type type, ParameterExpression output,
            ParameterExpression actualSource, ParameterExpression stream, ParameterExpression source, ImmutableSettings
                settings)
        {
            return WriteDictionary(type, output, actualSource, stream, source, settings)
                ?? WriteList(type, output, actualSource, stream, source, settings);
        }

        internal static Expression GetWriteFieldExpression(FieldInfo fieldInfo, ParameterExpression source,
            ParameterExpression stream, ParameterExpression output, ImmutableSettings settings)
        {
            var declaredType = fieldInfo.FieldType;
            var valueAccessExpression = Expression.MakeMemberAccess(source, fieldInfo);

            return WriteValue(stream, output, declaredType, valueAccessExpression, settings);
        }

        private static Expression WriteValue(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression, ImmutableSettings settings)
        {
            var primitiveExpression = HandlePrimitiveWrite(stream, output, declaredType, valueAccessExpression);
            if(primitiveExpression != null)
            {
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableWrite(stream, output, declaredType, valueAccessExpression);
            if (nullableExpression != null)
            {
                return nullableExpression;
            }

            var customExpression = HandleCustomWrite(output, declaredType, valueAccessExpression, settings);
            if (customExpression != null)
            {
                return customExpression;
            }

            var writeStruct = WriteStructExpression(declaredType, valueAccessExpression, stream, TypeFields.GetFields(declaredType));
            if (writeStruct != null)
            {
                return writeStruct;
            }

            var shouldWriteTypeInfo = !declaredType.IsSealed || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldWriteTypeInfo)
            {
                return Expression.Call(output, "WriteInternal", null, valueAccessExpression);
            }

            if (declaredType.IsValueType)
            {
                return Expression.Call(output, "WriteValueInternal", new[] { declaredType }, valueAccessExpression);
            }
            else
            {
                return Expression.Call(output, "WriteSealedInternal", new[] { declaredType }, valueAccessExpression);
            }
        }

        private static Expression HandleCustomWrite(ParameterExpression output, Type declaredType,
            Expression valueAccessExpression, ImmutableSettings settings)
        {
            if (!settings.SupportSerializationHooks)
            {
                return null;
            }

            foreach (var entry in Binary.CustomActionSerializers)
            {
                if (entry.Key.IsAssignableFrom(declaredType))
                {
                    return Expression.Call(
                        Expression.Convert(
                            Expression.Constant(entry.Value.Action),
                            typeof(Action<,>).MakeGenericType(declaredType, typeof(IBinaryWriter))),
                        entry.Value.InvokeMethodInfo, valueAccessExpression,
                        Expression.Call(output, SerializerMethods.BinaryWriterGetter));
                }
            }

            return null;
        }

        private static Expression HandlePrimitiveWrite(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression)
        {
            if(BufferedStreamMethods<TStream>.primitiveWriteMethods.TryGetValue(declaredType, out var method))
            {
                return Expression.Call(stream, method, valueAccessExpression);
            }

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BufferedStreamMethods<TStream>.WriteStringMethodInfo, valueAccessExpression);
            }

            return null;
        }

        private static Expression HandleNullableWrite(ParameterExpression stream, ParameterExpression output,
            Type declaredType,
            Expression valueAccessExpression)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            return Expression.IfThen(Expression.Not(Expression.Call(output, SerializerMethods.WriteNullableByteMethod.MakeGenericMethod(declaredType.GenericTypeArguments), valueAccessExpression)),
                Expression.Call(output, "WriteValueInternal", declaredType.GenericTypeArguments,Expression.Convert(valueAccessExpression, declaredType.GenericTypeArguments[0])));
        }

        internal static MethodInfo GetUnitializedObjectMethodInfo = typeof(FormatterServices).GetMethod("GetUninitializedObject");
        private static Type[] emptyTypes = new Type[0];

        internal static Delegate GenerateReadMethod(Type type, ImmutableSettings settings, bool isBoxed)
        {
            var fields = TypeFields.GetFields(type);
            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 8;

            var stream = Expression.Parameter(typeof(TStream), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");

            var readStatements = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var result = Expression.Variable(type, "result");
            localVariables.Add(result);

            if(type.IsValueType)
            {
                readStatements.Add(Expression.Assign(result, Expression.Default(type)));
            }

            readStatements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSizeNeeded)));

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialRead(type, output, result, stream, fields, settings, out var created);

            if (specialExpression != null && created)
            {
                readStatements.Add(specialExpression);
            }

            if (!created && !type.IsValueType)
            {
                var defaultCtor = type.GetConstructor(new Type[] { });
                var il = defaultCtor?.GetMethodBody()?.GetILAsByteArray();
                var sideEffectFreeCtor = il != null && il.Length <= 8; //this is the size of an empty ctor
                if (sideEffectFreeCtor)
                {
                    readStatements.Add(Expression.Assign(result, Expression.New(defaultCtor)));
                }
                else
                {
                    readStatements.Add(Expression.Assign(result,
                        Expression.Convert(
                            Expression.Call(null, GetUnitializedObjectMethodInfo, Expression.Constant(type)), type)));
                }
            }

            if (!created)
            {
                if (!type.IsValueType && settings.SerializationMode == Mode.Graph)
                {
                    readStatements.Add(Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter),
                        SerializerMethods.SavedReferencesListAdd, result));
                }
            }

            if (specialExpression != null && !created)
            {
                readStatements.Add(specialExpression);
            }
            else if (specialExpression == null)
            {
                if (type.IsPointer || fields.Any(x => x.FieldType.IsPointer))
                {
                    throw new NotSupportedException("Pointers or types containing pointers are not supported");
                }

                if (typeof(SafeHandle).IsAssignableFrom(type))
                {
                    throw new NotSupportedException("Objects containing handles are not supported");
                }

                if (type.IsValueType && FieldInfoModifier.MustUseReflectionToSetReadonly)
                {
                    var boxedResult = Expression.Variable(typeof(object), "boxedResult");
                    bool shouldUnbox = false;
                    bool fieldIsBoxed = false;
                    bool addedBoxedVariable = false;
                    for (int i = 0; i < fields.Count; ++i)
                    {
                        var field = fields[i];
                        if (field.IsInitOnly && !fieldIsBoxed)
                        {
                            if (!addedBoxedVariable)
                            {
                                localVariables.Add(boxedResult);
                                addedBoxedVariable = true;
                            }

                            readStatements.Add(Expression.Assign(boxedResult,
                                Expression.Convert(result, typeof(object))));
                            shouldUnbox = true;
                            fieldIsBoxed = true;
                        }
                        else if (!field.IsInitOnly && shouldUnbox)
                        {
                            readStatements.Add(Expression.Assign(result, Expression.Unbox(boxedResult, type)));
                            shouldUnbox = false;
                            fieldIsBoxed = false;
                        }

                        if (fieldIsBoxed)
                        {
                            readStatements.Add(GetReadFieldExpression(field, boxedResult, stream, output));
                        }
                        else
                        {
                            readStatements.Add(GetReadFieldExpression(field, result, stream, output));
                        }
                    }

                    if (shouldUnbox)
                    {
                        readStatements.Add(Expression.Assign(result, Expression.Unbox(boxedResult, type)));
                    }
                }
                else
                {
                    readStatements.AddRange(fields.Select(x => GetReadFieldExpression(x, result, stream, output)));
                }
            }

            if (settings.SupportSerializationHooks)
            {
                var methods = TypeMethods.GetAfterDeserializeMethods(type);
                if (methods.Count > 0)
                {
                    var p = Expression.Parameter(typeof(object));
                    var action = Expression.Lambda<Action<object>>(
                        Expression.Block(
                            methods.Select(m => Expression.Call(Expression.Convert(p, type), m))
                        )
                        , $"AfterDeserialize_{type.FullName}", new[] {p}).Compile();

                    readStatements.Add(Expression.Call(output, SerializerMethods.QueueAfterDeserializationHook, Expression.Constant(action), result));
                }
            }

            if (isBoxed)
            {
                readStatements.Add(Expression.Convert(result, typeof(object)));
            }
            else
            {
                readStatements.Add(result);
            }

            var lambda = Expression.Lambda(Expression.Block(localVariables, readStatements), $"Read_{type.FullName}", new [] {stream, output}).Compile();

            return lambda;
        }

        internal static Expression HandleSpecialRead(Type type, ParameterExpression output, ParameterExpression result, ParameterExpression stream, List<FieldInfo> fields, ImmutableSettings settings,
            out bool created)
        {
            var primitive = HandlePrimitiveRead(stream, output, type);
            if (primitive != null)
            {
                created = true;
                if (type == typeof(string) && settings.SerializationMode == Mode.Graph)
                {
                    return Expression.Block(
                        Expression.Assign(result, primitive),
                        Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter),
                            SerializerMethods.SavedReferencesListAdd, result)
                    );
                }

                return Expression.Assign(result, primitive);
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                created = true;
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, SerializerMethods.ReadTypeRefMethod), type));
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function deserialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                created = true;
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, SerializerMethods.ReadFunctionMethod), type));
            }

            var custom = HandleCustomRead(type, output, result, settings);
            if (custom != null)
            {
                created = false;
                return custom;
            }

            var readStructExpression = ReadStructExpression(type, stream, fields);
            if (readStructExpression != null)
            {
                created = true;
                return Expression.Assign(result, readStructExpression);
            }

            if (type.IsArray)
            {
                created = true;

                var elementType = type.GetElementType();
                var dimensions = type.GetArrayRank();

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType);

                var lengths = new List<ParameterExpression>();
                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int), $"length{i}"));
                }

                var statements = new List<Expression>();
                statements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x, i) => Expression.Assign(x,
                    Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo))));

                statements.Add(Expression.Assign(result, Expression.NewArrayBounds(elementType, lengths)));

                if (settings.SerializationMode == Mode.Graph)
                {
                    statements.Add(Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter),
                        SerializerMethods.SavedReferencesListAdd, result));
                }

                if (IsBlittable(elementType) && dimensions == 1)
                {
                    statements.Add(ReadArrayOfBlittableValues(output, result, stream, dimensions, elementType, elementSize));
                }
                else
                {
                    statements.Add(ReadArrayGeneral(output, result, stream, dimensions, elementType, elementSize, lengths));
                }

                return Expression.Block(lengths, statements);
            }

            var collection = ReadCollection(type, output, result, stream, settings);
            if (collection != null)
            {
                created = true;
            }
            else
            {
                created = false;
            }

            return collection;
        }

        private static Expression HandleCustomRead(Type type, ParameterExpression output, ParameterExpression result, ImmutableSettings settings)
        {
            if (!settings.SupportSerializationHooks)
            {
                return null;
            }

            foreach (var entry in Binary.CustomActionDeserializers)
            {
                if (entry.Key.IsAssignableFrom(type))
                {
                    return Expression.Call(
                        Expression.Convert(
                            Expression.Constant(entry.Value.Action),
                            typeof(Action<,>).MakeGenericType(type, typeof(IBinaryReader))),
                        entry.Value.InvokeMethodInfo, result,
                        Expression.Call(output, SerializerMethods.BinaryReaderGetter));
                }
            }

            return null;
        }

        private static bool IsBlittable(Type elementType)
        {
            if (elementType == typeof(byte))
            {
                return true;
            }
            if (elementType == typeof(sbyte))
            {
                return true;
            }
            if (elementType == typeof(short))
            {
                return true;
            }
            if (elementType == typeof(ushort))
            {
                return true;
            }
            if (elementType == typeof(int))
            {
                return true;
            }
            if (elementType == typeof(uint))
            {
                return true;
            }
            if (elementType == typeof(long))
            {
                return true;
            }
            if (elementType == typeof(ulong))
            {
                return true;
            }
            if (elementType == typeof(float))
            {
                return true;
            }
            if (elementType == typeof(double))
            {
                return true;
            }

            return elementType.IsExplicitLayout && TypeFields.GetFields(elementType).All(x => IsBlittable(x.FieldType));
        }

        private static Expression ReadArrayOfBlittableValues(ParameterExpression output, ParameterExpression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return Expression.Call(output, SerializerMethods.ReadArrayOfValuesMethod,
                Expression.Convert(actualSource, typeof(object)), Expression.Constant(elementSize));
        }

        private static Expression ReadArrayGeneral(ParameterExpression output, ParameterExpression result,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize,
            List<ParameterExpression> lengths)
        {
            var indices = new List<ParameterExpression>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int), $"index{i}"));
            }

            var accessExpression = dimensions > 1
                ? (Expression) Expression.ArrayAccess(result, indices)
                : Expression.ArrayAccess(result, indices[0]);
            var innerRead = Expression.Block(
                Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(elementSize)),
                Expression.Assign(accessExpression, ReadValue(stream, output, elementType))
            );
            var loop = (Expression) innerRead;

            for (int i = 0; i < dimensions; ++i)
            {
                var breakLabel = Expression.Label();
                loop = Expression.Block(
                    Expression.Assign(indices[i], Expression.Constant(0)),
                    Expression.Loop(Expression.IfThenElse(
                        Expression.GreaterThanOrEqual(indices[i], lengths[i]),
                        Expression.Break(breakLabel),
                        Expression.Block(loop, Expression.Assign(indices[i], Expression.Increment(indices[i])))
                    ), breakLabel)
                );
            }

            return Expression.Block(indices, loop);
        }

        private static Expression ReadStructExpression(Type type, ParameterExpression stream,
            List<FieldInfo> fields)
        {
            if (type.IsValueType)
            {
                if (type.IsExplicitLayout)
                {
                    if (fields.Any(x => !x.FieldType.IsValueType))
                    {
                        throw new NotSupportedException(
                            "Structs with explicit layout and reference fields are not supported");
                    }
                }

                if (type.IsExplicitLayout ||
                    (fields.Count <= 1 && fields.All(x => x.FieldType.IsValueType))
                )
                {
                    var method = (MethodInfo) typeof(BufferedStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("ReadValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    return Expression.Call(stream, method);
                }
            }

            return null;
        }

        internal static Expression ReadCollection(Type type, ParameterExpression output, ParameterExpression result, ParameterExpression stream, ImmutableSettings settings)
        {
            return ReadDictionary(type, output, result, stream, settings)
                ?? ReadList(type, output, result, stream, settings);
        }

        private static MethodInfo fieldInfoSetValueMethod = typeof(FieldInfo).GetMethod("SetValue", new[] { typeof(object), typeof(object) });

        internal static Expression GetReadFieldExpression(FieldInfo fieldInfo, ParameterExpression result,
            ParameterExpression stream, ParameterExpression output)
        {
            var declaredType = fieldInfo.FieldType;

            if (fieldInfo.Attributes.HasFlag(FieldAttributes.InitOnly))
            {
                if(FieldInfoModifier.setFieldInfoNotReadonly != null)
                {
                    FieldInfoModifier.setFieldInfoNotReadonly(fieldInfo);
                }
                else
                {
                    return Expression.Call(Expression.Constant(fieldInfo), fieldInfoSetValueMethod, Expression.Convert(result, typeof(object)), Expression.Convert(ReadValue(stream, output, declaredType), typeof(object)));
                }
            }

            var valueAccessExpression = Expression.MakeMemberAccess(result, fieldInfo);

            return Expression.Assign(valueAccessExpression, ReadValue(stream, output, declaredType));
        }

        private static Expression ReadValue(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            var primitiveExpression = HandlePrimitiveRead(stream, output, declaredType);
            if (primitiveExpression != null)
            {
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableRead(stream, output, declaredType);
            if (nullableExpression != null)
            {
                return nullableExpression;
            }

            var readStructExpression = ReadStructExpression(declaredType, stream, TypeFields.GetFields(declaredType));
            if (readStructExpression != null)
            {
                return readStructExpression;
            }

            var shouldReadTypeInfo = !declaredType.IsSealed || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldReadTypeInfo)
            {
                return Expression.Convert(Expression.Call(output, "ReadInternal", null), declaredType);
            }

            if (declaredType.IsValueType)
            {
                return Expression.Call(output, "ReadValueInternal", new[] { declaredType });
            }
            else
            {
                return Expression.Call(output, "ReadSealedInternal", new[] { declaredType });
            }
        }

        private static Expression HandlePrimitiveRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            if (BufferedStreamMethods<TStream>.primitiveReadMethods.TryGetValue(declaredType, out var method))
            {
                return Expression.Call(stream, method);
            }

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BufferedStreamMethods<TStream>.ReadStringMethodInfo);
            }

            return null;
        }

        private static Expression HandleNullableRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            return Expression.Condition(Expression.Not(Expression.Call(output, SerializerMethods.ReadNullByteMethod)),
                Expression.Convert(Expression.Call(output, "ReadValueInternal", declaredType.GenericTypeArguments),
                    declaredType), Expression.Convert(Expression.Constant(null), declaredType));
        }
    }
}
