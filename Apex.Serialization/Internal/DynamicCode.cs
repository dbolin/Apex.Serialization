using System;
using System.Collections.Concurrent;
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
        where TStream : IBinaryStream
        where TBinary : ISerializer
    {
        private static readonly ConcurrentDictionary<TypeKey, Delegate> _virtualWriteMethods = new ConcurrentDictionary<TypeKey, Delegate>();
        private static readonly ConcurrentDictionary<TypeKey, Delegate> _virtualReadMethods = new ConcurrentDictionary<TypeKey, Delegate>();

        internal static T GenerateWriteMethod<T>(Type type, ImmutableSettings settings, bool shouldWriteTypeInfo)
            where T : Delegate
        {
            if (!shouldWriteTypeInfo)
            {
                return GenerateWriteMethodImpl<T>(type, settings, shouldWriteTypeInfo);
            }

            return (T)_virtualWriteMethods.GetOrAdd(new TypeKey {Type = type, SettingsIndex = settings.SettingsIndex}, 
                t => GenerateWriteMethodImpl<T>(type, settings, shouldWriteTypeInfo));
        }

        internal static T GenerateWriteMethodImpl<T>(Type type, ImmutableSettings settings, bool shouldWriteTypeInfo)
            where T : Delegate
        {
            var fields = TypeFields.GetOrderedFields(type);

            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 12;

            var source = Expression.Parameter(shouldWriteTypeInfo ? typeof(object) : type, "source");
            var stream = Expression.Parameter(typeof(TStream).MakeByRefType(), "stream");
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

            writeStatements.AddRange(GetWriteStatementsForType(type, settings, stream, output, source, returnTarget, maxSizeNeeded, shouldWriteTypeInfo, actualSource, fields));

            writeStatements.Add(Expression.Label(returnTarget));

            var lambda = Expression.Lambda<T>(Expression.Block(localVariables, writeStatements), $"Apex.Serialization.Write_{type.FullName}", new [] {source, stream, output}).Compile();
            return lambda;
        }

        private static IEnumerable<Expression> GetWriteStatementsForType(Type type, ImmutableSettings settings, ParameterExpression stream,
            ParameterExpression output, Expression source, LabelTarget returnTarget, int maxSizeNeeded,
            bool shouldWriteTypeInfo, Expression actualSource, List<FieldInfo> fields)
        {
            var writeStatements = new List<Expression>();
            writeStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                Expression.Constant(maxSizeNeeded)));

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
                        BinaryStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo,
                        Expression.Constant(-1)));
                }
            }

            if (shouldWriteTypeInfo)
            {
                writeStatements.Add(
                    Expression.Call(output, SerializerMethods.WriteTypeRefMethod, Expression.Constant(type))
                );
                writeStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(maxSizeNeeded)));
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

                writeStatements.AddRange(fields.Select(x =>
                    GetWriteFieldExpression(x, actualSource, stream, output, settings)));
            }

            return writeStatements;
        }

        internal static Expression HandleSpecialWrite(Type type, ParameterExpression output, Expression actualSource, ParameterExpression stream, Expression source, List<FieldInfo> fields, ImmutableSettings settings)
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

                statements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x, i) =>
                    Expression.Assign(x, Expression.Call(actualSource, "GetLength", Array.Empty<Type>(), Expression.Constant(i)))));
                statements.AddRange(lengths.Select(x =>
                    Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo, x)));

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

        private static Expression WriteArrayOfBlittableValues(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return Expression.Call(output, SerializerMethods.WriteArrayOfValuesMethod,
                Expression.Convert(actualSource, typeof(object)), Expression.Property(actualSource, "Length"),
                Expression.Constant(elementSize));
        }

        private static Expression WriteArrayGeneral(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, List<ParameterExpression> lengths, Type elementType, int elementSize,
            ImmutableSettings settings)
        {
            var indices = new List<ParameterExpression>();
            var breakLabels = new List<LabelTarget>();
            var continueLabels = new List<LabelTarget>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int)));
                breakLabels.Add(Expression.Label());
                continueLabels.Add(Expression.Label());
            }

            var accessExpression = dimensions > 1
                ? (Expression) Expression.ArrayIndex(actualSource, indices)
                : Expression.ArrayIndex(actualSource, indices[0]);

            var writeValue = WriteValue(stream, output, elementType, accessExpression, settings, out var isSimpleWrite);

            var shouldWriteTypeInfo = typeof(Delegate).IsAssignableFrom(elementType) || typeof(Type).IsAssignableFrom(elementType);

            if (!isSimpleWrite && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType))
            {
                var fields = TypeFields.GetOrderedFields(elementType);
                writeValue = Expression.Block(GetWriteStatementsForType(elementType, settings, stream, output,
                    accessExpression, continueLabels[continueLabels.Count - 1], fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 12, shouldWriteTypeInfo, accessExpression,
                    fields));

                if (!elementType.IsValueType)
                {
                    writeValue = Expression.Block(
                        Expression.IfThen(
                            Expression.Call(output, SerializerMethods.WriteNullByteMethod, accessExpression),
                            Expression.Continue(continueLabels[continueLabels.Count - 1])
                        ),
                        writeValue
                    );
                }
            }
            else
            {
                writeValue = Expression.Block(
                    Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(elementSize)),
                    writeValue);
            }

            var loop = writeValue;

            for (int i = 0; i < dimensions; ++i)
            {
                loop =
                    Expression.Block(
                        Expression.Assign(indices[i], Expression.Constant(0)),
                        Expression.Loop(Expression.IfThenElse(
                            Expression.GreaterThanOrEqual(indices[i], lengths[i]),
                            Expression.Break(breakLabels[i]),
                            Expression.Block(loop, Expression.Label(continueLabels[i]), Expression.Assign(indices[i], Expression.Increment(indices[i])))
                        ), breakLabels[i])
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
                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("WriteValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    return Expression.Call(stream, method, source);
                }
            }

            return null;
        }

        internal static Expression WriteCollection(Type type, ParameterExpression output,
            Expression actualSource, ParameterExpression stream, Expression source, ImmutableSettings
                settings)
        {
            return WriteDictionary(type, output, actualSource, stream, source, settings)
                ?? WriteList(type, output, actualSource, stream, source, settings);
        }

        internal static Expression GetWriteFieldExpression(FieldInfo fieldInfo, Expression source,
            ParameterExpression stream, ParameterExpression output, ImmutableSettings settings)
        {
            var declaredType = fieldInfo.FieldType;
            var valueAccessExpression = Expression.MakeMemberAccess(source, fieldInfo);

            return WriteValue(stream, output, declaredType, valueAccessExpression, settings, out _);
        }

        private static Expression WriteValue(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression, ImmutableSettings settings, out bool simpleType)
        {
            var primitiveExpression = HandlePrimitiveWrite(stream, output, declaredType, valueAccessExpression);
            if(primitiveExpression != null)
            {
                simpleType = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableWrite(stream, output, declaredType, valueAccessExpression);
            if (nullableExpression != null)
            {
                simpleType = true;
                return nullableExpression;
            }

            var customExpression = HandleCustomWrite(output, declaredType, valueAccessExpression, settings);
            if (customExpression != null)
            {
                simpleType = true;
                return customExpression;
            }

            var writeStruct = WriteStructExpression(declaredType, valueAccessExpression, stream, TypeFields.GetOrderedFields(declaredType));
            if (writeStruct != null)
            {
                simpleType = true;
                return writeStruct;
            }

            var shouldWriteTypeInfo = !StaticTypeInfo.IsSealedOrHasNoDescendents(declaredType) || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldWriteTypeInfo)
            {
                simpleType = false;
                return Expression.Call(output, "WriteInternal", null, valueAccessExpression);
            }

            if (declaredType.IsValueType)
            {
                simpleType = false;
                return Expression.Call(output, "WriteValueInternal", new[] { declaredType }, valueAccessExpression);
            }
            else
            {
                simpleType = false;
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

            var statements = new List<Expression>();

            foreach (var entry in Binary.CustomActionSerializers)
            {
                if (entry.Key.IsAssignableFrom(declaredType))
                {
                    var customContextType = entry.Value.CustomContextType;
                    if (customContextType != null)
                    {
                        var customContext = Expression.Call(output, SerializerMethods.CustomContextGetter.MakeGenericMethod(customContextType));
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(declaredType, typeof(IBinaryWriter), customContextType)),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, SerializerMethods.BinaryWriterGetter),
                            customContext));
                    }
                    else
                    {
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(declaredType, typeof(IBinaryWriter))),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, SerializerMethods.BinaryWriterGetter)));
                    }
                }
            }

            return statements.Count > 0 ? Expression.Block(statements) : null;
        }

        private static Expression HandlePrimitiveWrite(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression)
        {
            if(BinaryStreamMethods<TStream>.primitiveWriteMethods.TryGetValue(declaredType, out var method))
            {
                return Expression.Call(stream, method, valueAccessExpression);
            }

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.WriteStringMethodInfo, valueAccessExpression);
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

        internal static T GenerateReadMethod<T>(Type type, ImmutableSettings settings, bool isBoxed)
            where T : Delegate
        {
            if (!isBoxed)
            {
                return GenerateReadMethodImpl<T>(type, settings, isBoxed);
            }

            return (T)_virtualReadMethods.GetOrAdd(new TypeKey { Type = type, SettingsIndex = settings.SettingsIndex },
                t => GenerateReadMethodImpl<T>(type, settings, isBoxed));
        }

        internal static T GenerateReadMethodImpl<T>(Type type, ImmutableSettings settings, bool isBoxed)
            where T : Delegate
        {
            var fields = TypeFields.GetOrderedFields(type);
            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 8;

            var stream = Expression.Parameter(typeof(TStream).MakeByRefType(), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");

            var readStatements = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var result = Expression.Variable(type, "result");
            localVariables.Add(result);

            if(type.IsValueType)
            {
                readStatements.Add(Expression.Assign(result, Expression.Default(type)));
            }

            readStatements.AddRange(GetReadStatementsForType(type, settings, stream, maxSizeNeeded, output, result, fields, localVariables));

            if (isBoxed)
            {
                readStatements.Add(Expression.Convert(result, typeof(object)));
            }
            else
            {
                readStatements.Add(result);
            }

            var lambda = Expression.Lambda<T>(Expression.Block(localVariables, readStatements), $"Apex.Serialization.Read_{type.FullName}", new [] {stream, output}).Compile();

            return lambda;
        }

        private static List<Expression> GetReadStatementsForType(Type type, ImmutableSettings settings, ParameterExpression stream,
            int maxSizeNeeded, ParameterExpression output, Expression result, List<FieldInfo> fields, List<ParameterExpression> localVariables)
        {
            var readStatements = new List<Expression>();
            readStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                Expression.Constant(maxSizeNeeded)));

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialRead(type, output, result, stream, fields, settings, out var created);

            if (specialExpression != null && created)
            {
                readStatements.Add(specialExpression);
            }

            if (!created && !type.IsValueType)
            {
                var ctor = Cil.FindDeserializationConstructor(type);
                if (ctor != null)
                {
                    readStatements.Add(Expression.Assign(result, Expression.New(ctor)));
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
                    var objectParameter = Expression.Parameter(typeof(object));
                    var contextParameter = Expression.Parameter(typeof(object));
                    var action = Expression.Lambda<Action<object, object>>(
                        Expression.Block(
                            methods.Select(m => AfterDeserializeCallExpression(type, m, objectParameter, contextParameter))
                        )
                        , $"AfterDeserialize_{type.FullName}", new[] {objectParameter, contextParameter}).Compile();

                    readStatements.Add(Expression.Call(output, SerializerMethods.QueueAfterDeserializationHook,
                        Expression.Constant(action), result));
                }
            }

            return readStatements;
        }

        private static Expression AfterDeserializeCallExpression(Type type, MethodInfo m,
            ParameterExpression objectParameter, ParameterExpression contextParameter)
        {
            var castedObject = Expression.Convert(objectParameter, type);
            var parameters = m.GetParameters();

            if (m.IsStatic)
            {
                if (parameters.Length == 2)
                {
                    return Expression.Call(null, m, castedObject, Expression.Convert(contextParameter, parameters[1].ParameterType));
                }

                return Expression.Call(null, m, castedObject);
            }

            if(parameters.Length == 1)
            {
                return Expression.Call(castedObject, m, Expression.Convert(contextParameter, parameters[0].ParameterType));
            }

            return Expression.Call(castedObject, m);
        }

        internal static Expression HandleSpecialRead(Type type, ParameterExpression output, Expression result, ParameterExpression stream, List<FieldInfo> fields, ImmutableSettings settings,
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
                statements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x, i) => Expression.Assign(x,
                    Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo))));

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
                    statements.Add(ReadArrayGeneral(output, result, stream, dimensions, elementType, elementSize, lengths, settings));
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

        private static Expression HandleCustomRead(Type type, ParameterExpression output, Expression result, ImmutableSettings settings)
        {
            if (!settings.SupportSerializationHooks)
            {
                return null;
            }

            var statements = new List<Expression>();

            foreach (var entry in Binary.CustomActionDeserializers)
            {
                if (entry.Key.IsAssignableFrom(type))
                {
                    var customContextType = entry.Value.CustomContextType;
                    if(customContextType != null)
                    {
                        var customContext = Expression.Call(output, SerializerMethods.CustomContextGetter.MakeGenericMethod(customContextType));
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(type, typeof(IBinaryReader), customContextType)),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, SerializerMethods.BinaryReaderGetter),
                            customContext));
                    }
                    else
                    {
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(type, typeof(IBinaryReader))),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, SerializerMethods.BinaryReaderGetter)));
                    }
                }
            }

            return statements.Count > 0 ? Expression.Block(statements) : null;
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
            if (elementType == typeof(char))
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

            return elementType.IsExplicitLayout && TypeFields.GetOrderedFields(elementType).All(x => IsBlittable(x.FieldType));
        }

        private static Expression ReadArrayOfBlittableValues(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return Expression.Call(output, SerializerMethods.ReadArrayOfValuesMethod,
                Expression.Convert(actualSource, typeof(object)), Expression.Constant(elementSize));
        }

        private static Expression ReadArrayGeneral(ParameterExpression output, Expression result,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize,
            List<ParameterExpression> lengths, ImmutableSettings settings)
        {
            var indices = new List<ParameterExpression>();
            var continueLabels = new List<LabelTarget>();
            var localVariables = new List<ParameterExpression>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int), $"index{i}"));
                continueLabels.Add(Expression.Label());
            }

            var accessExpression = dimensions > 1
                ? (Expression) Expression.ArrayAccess(result, indices)
                : Expression.ArrayAccess(result, indices[0]);

            var readValue = ReadValue(stream, output, elementType, out var isSimpleRead);

            var shouldReadTypeInfo = typeof(Delegate).IsAssignableFrom(elementType) || typeof(Type).IsAssignableFrom(elementType);

            if (!isSimpleRead && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType)
                && !typeof(Type).IsAssignableFrom(elementType)
                && !typeof(Delegate).IsAssignableFrom(elementType))
            {
                var fields = TypeFields.GetOrderedFields(elementType);
                readValue = Expression.Block(GetReadStatementsForType(elementType, settings, stream, fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) + 12, output,
                    accessExpression, fields, localVariables));

                if (!elementType.IsValueType)
                {
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        var refIndex = Expression.Variable(typeof(int), "refIndex");
                        readValue = Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(5)),
                            Expression.IfThenElse(
                                Expression.Equal(Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.ReadValueMethodInfo), Expression.Constant((byte)0)),
                                Expression.Continue(continueLabels[continueLabels.Count - 1]),
                                Expression.Block(new[] { refIndex },
                                    Expression.Assign(refIndex, Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo)),
                                    Expression.IfThen(
                                        Expression.NotEqual(refIndex, Expression.Constant(-1)),
                                        Expression.Block(
                                            Expression.Assign(accessExpression, 
                                                Expression.Convert(
                                                    Expression.Property(
                                                        Expression.Call(output, SerializerMethods.SavedReferencesGetter), 
                                                    SerializerMethods.SavedReferencesListIndexer, Expression.Decrement(refIndex)), 
                                                elementType)
                                            ),
                                            Expression.Continue(continueLabels[continueLabels.Count - 1])
                                        )
                                    )
                                )
                            ),
                            readValue
                        );
                    }
                    else
                    {
                        readValue = Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(1)),
                            Expression.IfThen(
                                Expression.Equal(Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.ReadValueMethodInfo), Expression.Constant((byte)0)),
                                Expression.Continue(continueLabels[continueLabels.Count - 1])
                            ),
                            readValue
                        );
                    }
                }
            }
            else
            {
                readValue = Expression.Block(
                    Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(elementSize)),
                    Expression.Assign(accessExpression, readValue));
            }

            var loop = readValue;

            for (int i = 0; i < dimensions; ++i)
            {
                var breakLabel = Expression.Label();
                loop = Expression.Block(
                    Expression.Assign(indices[i], Expression.Constant(0)),
                    Expression.Loop(Expression.IfThenElse(
                        Expression.GreaterThanOrEqual(indices[i], lengths[i]),
                        Expression.Break(breakLabel),
                        Expression.Block(loop, Expression.Label(continueLabels[i]), Expression.Assign(indices[i], Expression.Increment(indices[i])))
                    ), breakLabel)
                );
            }

            return Expression.Block(indices.Concat(localVariables), loop);
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
                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("ReadValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    return Expression.Call(stream, method);
                }
            }

            return null;
        }

        internal static Expression ReadCollection(Type type, ParameterExpression output, Expression result, ParameterExpression stream, ImmutableSettings settings)
        {
            return ReadDictionary(type, output, result, stream, settings)
                ?? ReadList(type, output, result, stream, settings);
        }

        private static MethodInfo fieldInfoSetValueMethod = typeof(FieldInfo).GetMethod("SetValue", new[] { typeof(object), typeof(object) });

        internal static Expression GetReadFieldExpression(FieldInfo fieldInfo, Expression result,
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
                    return Expression.Call(Expression.Constant(fieldInfo), fieldInfoSetValueMethod, Expression.Convert(result, typeof(object)), Expression.Convert(ReadValue(stream, output, declaredType, out _), typeof(object)));
                }
            }

            var valueAccessExpression = Expression.MakeMemberAccess(result, fieldInfo);

            return Expression.Assign(valueAccessExpression, ReadValue(stream, output, declaredType, out _));
        }

        private static Expression ReadValue(ParameterExpression stream, ParameterExpression output, Type declaredType, out bool isSimpleRead)
        {
            var primitiveExpression = HandlePrimitiveRead(stream, output, declaredType);
            if (primitiveExpression != null)
            {
                isSimpleRead = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableRead(stream, output, declaredType);
            if (nullableExpression != null)
            {
                isSimpleRead = true;
                return nullableExpression;
            }

            var readStructExpression = ReadStructExpression(declaredType, stream, TypeFields.GetOrderedFields(declaredType));
            if (readStructExpression != null)
            {
                isSimpleRead = true;
                return readStructExpression;
            }

            var shouldReadTypeInfo = !StaticTypeInfo.IsSealedOrHasNoDescendents(declaredType) || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldReadTypeInfo)
            {
                isSimpleRead = false;
                return Expression.Convert(Expression.Call(output, "ReadInternal", null), declaredType);
            }

            if (declaredType.IsValueType)
            {
                isSimpleRead = false;
                return Expression.Call(output, "ReadValueInternal", new[] { declaredType });
            }
            else
            {
                isSimpleRead = false;
                return Expression.Call(output, "ReadSealedInternal", new[] { declaredType });
            }
        }

        private static Expression HandlePrimitiveRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            if (BinaryStreamMethods<TStream>.primitiveReadMethods.TryGetValue(declaredType, out var method))
            {
                return Expression.Call(stream, method);
            }

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.ReadStringMethodInfo);
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
