using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

            var source = Expression.Parameter(shouldWriteTypeInfo ? typeof(object) : type, "source");
            var stream = Expression.Parameter(typeof(TStream).MakeByRefType(), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");

            var returnTarget = Expression.Label();

            var writeStatements = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var castedSourceType = (ParameterExpression?)null;

            if (shouldWriteTypeInfo)
            {
                castedSourceType = Expression.Variable(type);
                localVariables.Add(castedSourceType);
                writeStatements.Add(Expression.Assign(castedSourceType, Expression.Convert(source, type)));
            }

            var actualSource = castedSourceType ?? source;

            var visitedTypes = new HashSet<Type>();

            writeStatements.AddRange(GetWriteStatementsForType(type, settings, stream, output, source, returnTarget, shouldWriteTypeInfo, actualSource, fields, visitedTypes));

            writeStatements.Add(Expression.Label(returnTarget));

            var lambda = Expression.Lambda<T>(Expression.Block(localVariables, writeStatements), $"Apex.Serialization.Write_{type.FullName}", new [] {source, stream, output}).Compile();
            return lambda;
        }

        private static IEnumerable<Expression> GetWriteStatementsForType(Type type, ImmutableSettings settings, ParameterExpression stream,
            ParameterExpression output, Expression source, LabelTarget returnTarget,
            bool shouldWriteTypeInfo, Expression actualSource, List<FieldInfo> fields,
            HashSet<Type> visitedTypes,
            bool writeNullByte = false, bool writeSize = true)
        {
            var maxSizeNeeded = writeSize ? fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) : 0;
            int metaBytes = 0;

            if(writeNullByte && !type.IsValueType)
            {
                metaBytes++;
            }

            if(shouldWriteTypeInfo)
            {
                metaBytes += 4;
            }
            if(settings.SerializationMode == Mode.Graph && (!type.IsValueType || shouldWriteTypeInfo))
            {
                metaBytes += 4;
            }

            var writeStatements = new List<Expression>();
            if (maxSizeNeeded + metaBytes > 0 && (!writeNullByte || type.IsValueType))
            {
                writeStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(maxSizeNeeded + metaBytes)));
            }

            if (settings.SerializationMode == Mode.Graph)
            {
                if (!type.IsValueType && !typeof(Delegate).IsAssignableFrom(type)
                                      && !typeof(Type).IsAssignableFrom(type))
                {
                    writeStatements.Add(Expression.IfThen(
                        Expression.Call(output, WriteObjectRefMethod, source),
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
                if (maxSizeNeeded > 0)
                {
                    writeStatements.Add(
                        Expression.IfThen(
                            Expression.Call(output, WriteTypeRefMethod, Expression.Constant(type)),
                            Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                                Expression.Constant(maxSizeNeeded))
                        )
                    );
                }
                else
                {
                    writeStatements.Add(Expression.Call(output, WriteTypeRefMethod, Expression.Constant(type)));
                }
            }

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialWrite(type, output, actualSource, stream, source, fields, settings, visitedTypes);

            if (specialExpression != null)
            {
                writeStatements.Add(specialExpression);
            }
            else
            {
                CheckTypeSupported(type, fields);

                writeStatements.AddRange(fields.Select(x =>
                    GetWriteFieldExpression(x, actualSource, stream, output, settings, visitedTypes)));
            }


            if (!type.IsValueType && writeNullByte)
            {
                var afterWriteLabel = Expression.Label("afterWrite");
                var wrapperStatements = new List<Expression>();
                wrapperStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                        Expression.Constant(maxSizeNeeded + metaBytes)));
                wrapperStatements.Add(Expression.IfThenElse(
                        Expression.ReferenceEqual(actualSource, Expression.Constant(null)),
                        Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)0)),
                            Expression.Continue(afterWriteLabel)
                        ),
                        Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)1))
                        )
                    ));
                if (writeStatements.Count > 0)
                {
                    wrapperStatements.Add(Expression.Block(writeStatements));
                }
                wrapperStatements.Add(Expression.Label(afterWriteLabel));
                var wrappedWrite = Expression.Block(wrapperStatements);
                writeStatements = new List<Expression> { wrappedWrite };
            }

            return writeStatements;
        }

        private static void CheckTypeSupported(Type type, List<FieldInfo> fields)
        {
            if (type.IsPointer || fields.Any(x => x.FieldType.IsPointer))
            {
                throw new NotSupportedException("Pointers or types containing pointers are not supported");
            }

            if (typeof(SafeHandle).IsAssignableFrom(type))
            {
                throw new NotSupportedException("Objects containing handles are not supported");
            }
        }

        internal static Expression? HandleSpecialWrite(Type type, ParameterExpression output,
            Expression actualSource, ParameterExpression stream, Expression source, List<FieldInfo> fields,
            ImmutableSettings settings, HashSet<Type> visitedTypes)
        {
            var primitive = HandlePrimitiveWrite(stream, output, type, actualSource);
            if(primitive != null)
            {
                return primitive;
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                return Expression.Call(output, WriteTypeRefMethod, actualSource);
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function serialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                return Expression.Call(output, WriteFunctionMethod, Expression.Convert(source, typeof(Delegate)));
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
                var elementType = type.GetElementType()!;
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

                if (StaticTypeInfo.IsBlittable(elementType) && dimensions < 3)
                {
                    statements.Add(WriteArrayOfBlittableValues(output, actualSource, stream, dimensions, elementType, elementSize));
                }
                else
                {
                    statements.Add(WriteArrayGeneral(output, actualSource, stream, dimensions, lengths, elementType, elementSize, settings, visitedTypes));
                }
                return Expression.Block(lengths, statements);
            }

            return WriteCollection(type, output, actualSource, stream, source, settings, visitedTypes);
        }

        private static Expression WriteArrayOfBlittableValues(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return dimensions switch
            {
                1 => Expression.Call(output, WriteArrayOfValuesMethod1.MakeGenericMethod(elementType),
                       actualSource,
                       Expression.Constant(elementSize)),
                2 => Expression.Call(output, WriteArrayOfValuesMethod2.MakeGenericMethod(elementType),
                       actualSource,
                       Expression.Constant(elementSize)),
                _ => throw new InvalidOperationException($"Blitting multidimensional array with {dimensions} dimensions is not supported"),
            };
        }

        private static Expression WriteArrayGeneral(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, List<ParameterExpression> lengths, Type elementType, int elementSize,
            ImmutableSettings settings, HashSet<Type> visitedTypes)
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

            var writeValue = WriteValue(stream, output, elementType, accessExpression, settings, visitedTypes, out var isSimpleWrite);

            var shouldWriteTypeInfo = typeof(Delegate).IsAssignableFrom(elementType) || typeof(Type).IsAssignableFrom(elementType);

            if (!isSimpleWrite && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType))
            {
                var fields = TypeFields.GetOrderedFields(elementType);
                writeValue = Expression.Block(GetWriteStatementsForType(elementType, settings, stream, output,
                    accessExpression, continueLabels[continueLabels.Count - 1], shouldWriteTypeInfo, accessExpression,
                    fields, visitedTypes, true));
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

        private static Expression? WriteStructExpression(Type type, Expression source, ParameterExpression stream,
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
                    if(fields.Count == 0)
                    {
                        return Expression.Empty();
                    }

                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("WriteValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    return Expression.Call(stream, method, source);
                }
            }

            return null;
        }

        internal static Expression? WriteCollection(Type type, ParameterExpression output,
            Expression actualSource, ParameterExpression stream, Expression source, ImmutableSettings
                settings, HashSet<Type> visitedTypes)
        {
            return WriteDictionary(type, output, actualSource, stream, source, settings, visitedTypes)
                ?? WriteList(type, output, actualSource, stream, source, settings, visitedTypes);
        }

        internal static Expression GetWriteFieldExpression(FieldInfo fieldInfo, Expression source,
            ParameterExpression stream, ParameterExpression output, ImmutableSettings settings, HashSet<Type> visitedTypes)
        {
            var declaredType = fieldInfo.FieldType;
            var valueAccessExpression = Expression.MakeMemberAccess(source, fieldInfo);

            return WriteValue(stream, output, declaredType, valueAccessExpression, settings, visitedTypes, out _);
        }

        private static Expression WriteValue(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression, ImmutableSettings settings, HashSet<Type> visitedTypes, out bool inlineWrite)
        {
            var primitiveExpression = HandlePrimitiveWrite(stream, output, declaredType, valueAccessExpression);
            if(primitiveExpression != null)
            {
                inlineWrite = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableWrite(stream, output, declaredType, valueAccessExpression);
            if (nullableExpression != null)
            {
                inlineWrite = true;
                return nullableExpression;
            }

            var customExpression = HandleCustomWrite(output, declaredType, valueAccessExpression, settings);
            if (customExpression != null)
            {
                inlineWrite = true;
                return customExpression;
            }

            var writeStruct = WriteStructExpression(declaredType, valueAccessExpression, stream, TypeFields.GetOrderedFields(declaredType));
            if (writeStruct != null)
            {
                inlineWrite = true;
                return writeStruct;
            }

            var shouldWriteTypeInfo = !StaticTypeInfo.IsSealedOrHasNoDescendents(declaredType) || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldWriteTypeInfo)
            {
                inlineWrite = false;
                return Expression.Call(output, "WriteInternal", null, valueAccessExpression);
            }

            if (declaredType.IsValueType)
            {
                inlineWrite = true;
                var returnTarget = Expression.Label();
                var writeStatements = GetWriteStatementsForType(declaredType, settings, stream, output,
                    valueAccessExpression, returnTarget, false, valueAccessExpression, TypeFields.GetOrderedFields(declaredType),
                    visitedTypes, writeSize: !TypeFields.IsPrimitive(declaredType));
                return Expression.Block(
                    Expression.Block(writeStatements),
                    Expression.Label(returnTarget)
                    );
            }
            else
            {
                if (!visitedTypes.Add(declaredType))
                {
                    inlineWrite = false;
                    return Expression.Call(output, "WriteSealedInternal", new[] { declaredType }, valueAccessExpression);
                }

                inlineWrite = true;
                var afterWriteLabel = Expression.Label();
                var temporaryVar = Expression.Variable(declaredType);
                var writeStatements = new List<Expression>
                {
                    Expression.Assign(temporaryVar, valueAccessExpression)
                };
                writeStatements.AddRange(GetWriteStatementsForType(declaredType, settings, stream, output,
                    temporaryVar, afterWriteLabel, false, temporaryVar,
                    TypeFields.GetOrderedFields(declaredType), visitedTypes, writeNullByte: true));
                writeStatements.Add(Expression.Label(afterWriteLabel));
                return Expression.Block(new[] { temporaryVar },
                    writeStatements
                    );
            }
        }

        private static Expression? HandleCustomWrite(ParameterExpression output, Type declaredType,
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
                        var customContext = Expression.Call(output, CustomContextGetter.MakeGenericMethod(customContextType));
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(declaredType, typeof(IBinaryWriter), customContextType)),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, BinaryWriterGetter),
                            customContext));
                    }
                    else
                    {
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(declaredType, typeof(IBinaryWriter))),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, BinaryWriterGetter)));
                    }
                }
            }

            return statements.Count > 0 ? Expression.Block(statements) : null;
        }

        private static Expression? HandlePrimitiveWrite(ParameterExpression stream, ParameterExpression output, Type declaredType,
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

        private static Expression? HandleNullableWrite(ParameterExpression stream, ParameterExpression output,
            Type declaredType,
            Expression valueAccessExpression)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            return Expression.IfThen(Expression.Not(Expression.Call(output, WriteNullableByteMethod.MakeGenericMethod(declaredType.GenericTypeArguments), valueAccessExpression)),
                Expression.Call(output, "WriteValueInternal", declaredType.GenericTypeArguments,Expression.Convert(valueAccessExpression, declaredType.GenericTypeArguments[0])));
        }

        internal static MethodInfo GetUnitializedObjectMethodInfo = typeof(FormatterServices).GetMethod("GetUninitializedObject")!;
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

            var visitedTypes = new HashSet<Type>();

            readStatements.AddRange(GetReadStatementsForType(type, settings, stream, output, result, fields, localVariables, visitedTypes));

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
            ParameterExpression output, Expression result, List<FieldInfo> fields, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes, bool readMetadata = false,
            bool reserveNeededSize = true)
        {
            var readStatements = new List<Expression>();

            var skipReadLabel = readMetadata ? Expression.Label() : null;
            var maxSizeNeeded = reserveNeededSize ? fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size) : 0;
            if (readMetadata)
            {
                maxSizeNeeded++;

                if(settings.SerializationMode == Mode.Graph && !type.IsValueType)
                {
                    maxSizeNeeded += 4;
                }
            }

            if (maxSizeNeeded > 0)
            {
                readStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                    Expression.Constant(maxSizeNeeded)));
            }

            if (readMetadata)
            {
                readStatements.Add(
                    Expression.IfThen(
                            Expression.Equal(Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.ReadValueMethodInfo), Expression.Constant((byte)0)),
                            Expression.Goto(skipReadLabel)
                        )
                    );

                if (settings.SerializationMode == Mode.Graph && !type.IsValueType)
                {
                    var refIndex = Expression.Variable(typeof(int), "refIndex");

                    readStatements.Add(
                        Expression.Block(
                            new[] { refIndex },
                            Expression.Assign(refIndex, Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo)),
                                Expression.IfThen(
                                    Expression.NotEqual(refIndex, Expression.Constant(-1)),
                                    Expression.Block(
                                        Expression.Assign(result,
                                            Expression.Convert(
                                                Expression.Property(
                                                    Expression.Call(output, SavedReferencesGetter),
                                                SavedReferencesListIndexer, Expression.Decrement(refIndex)),
                                            type)
                                        ),
                                        Expression.Goto(skipReadLabel)
                                    )
                                )
                            )
                        );
                }
            }

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialRead(type, output, result, stream, fields, settings, localVariables, visitedTypes, out var created);

            if(specialExpression == null)
            {
                CheckTypeSupported(type, fields);
            }

            if (specialExpression != null && created)
            {
                readStatements.Add(specialExpression);
            }

            bool specificConstructorDeserialization = false;

            if (!created && !type.IsValueType)
            {
                var ctor = Cil.FindEmptyDeserializationConstructor(type);
                if (ctor != null)
                {
                    readStatements.Add(Expression.Assign(result, Expression.New(ctor)));
                }
                else
                {
                    var useConstructorDeserialization = ShouldCheckForSpecificConstructor(type, settings) ? Cil.FindSpecificDeserializationConstructor(type, fields) : null;
                    if (useConstructorDeserialization.HasValue)
                    {
                        specificConstructorDeserialization = true;
                        var constructor = useConstructorDeserialization.Value.constructor;
                        var fieldOrder = useConstructorDeserialization.Value.fieldOrder;

                        var constructorLocalVariables = new List<ParameterExpression>();
                        var constructorLocalFieldVariables = new List<ParameterExpression>();
                        var constructorLocalStatements = new List<Expression>();

                        var currentSavedReferencesIndexVariable = Expression.Variable(typeof(int), "currentSavedRefIndex");

                        if (settings.SerializationMode == Mode.Graph && !type.IsValueType)
                        {
                            constructorLocalVariables.Add(currentSavedReferencesIndexVariable);
                            constructorLocalStatements.Add(Expression.Assign(currentSavedReferencesIndexVariable, Expression.Call(Expression.Call(output, SavedReferencesGetter), SavedReferencesListCountGetter)));
                            constructorLocalStatements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, Expression.Constant(null)));
                        }

                        foreach (var field in fields)
                        {
                            var variableExpression = Expression.Variable(field.FieldType, field.Name);
                            constructorLocalVariables.Add(variableExpression);
                            constructorLocalFieldVariables.Add(variableExpression);
                            var readValueExpression = ReadValue(stream, output, settings, field.FieldType, localVariables, visitedTypes, out _);
                            constructorLocalStatements.Add(Expression.Assign(variableExpression, readValueExpression));
                        }

                        var constructorParams = new Expression[fieldOrder.Count];
                        for (int i = 0; i < fieldOrder.Count; ++i)
                        {
                            constructorParams[i] = constructorLocalFieldVariables[fieldOrder[i]];
                        }

                        constructorLocalStatements.Add(Expression.Assign(result, Expression.New(constructor, constructorParams)));

                        if (settings.SerializationMode == Mode.Graph && !type.IsValueType)
                        {
                            constructorLocalStatements.Add(
                                Expression.Assign(
                                    Expression.Property(Expression.Call(output, SavedReferencesGetter),
                                        SavedReferencesListIndexer, currentSavedReferencesIndexVariable),
                                result)
                            );
                        }

                        readStatements.Add(Expression.Block(constructorLocalVariables, constructorLocalStatements));

                        created = true;
                    }
                    else
                    {
                        readStatements.Add(Expression.Assign(result,
                            Expression.Convert(
                                Expression.Call(null, GetUnitializedObjectMethodInfo, Expression.Constant(type)), type)));
                    }
                }
            }

            if (!created)
            {
                if (!type.IsValueType && settings.SerializationMode == Mode.Graph)
                {
                    readStatements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                        SavedReferencesListAdd, result));
                }
            }

            if (specialExpression != null && !created)
            {
                readStatements.Add(specialExpression);
            }
            else if (specialExpression == null && !specificConstructorDeserialization)
            {
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
                            readStatements.Add(GetReadFieldExpression(field, boxedResult, stream, output, settings, localVariables, visitedTypes));
                        }
                        else
                        {
                            readStatements.Add(GetReadFieldExpression(field, result, stream, output, settings, localVariables, visitedTypes));
                        }
                    }

                    if (shouldUnbox)
                    {
                        readStatements.Add(Expression.Assign(result, Expression.Unbox(boxedResult, type)));
                    }
                }
                else
                {
                    readStatements.AddRange(fields.Select(x => GetReadFieldExpression(x, result, stream, output, settings, localVariables, visitedTypes)));
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

                    readStatements.Add(Expression.Call(output, QueueAfterDeserializationHook,
                        Expression.Constant(action), result));
                }
            }

            if(skipReadLabel != null)
            {
                readStatements.Add(Expression.Label(skipReadLabel));
            }

            return readStatements;
        }

        private static bool ShouldCheckForSpecificConstructor(Type type, ImmutableSettings settings)
        {
            return settings.UseConstructors
                && (
                    settings.SerializationMode == Mode.Tree
                    || (Attribute.GetCustomAttribute(type, typeof(ImmutableAttribute)) as ImmutableAttribute)?.OnFaith == false
                    || StaticTypeInfo.CannotReferenceSelf(type)
                    );
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

        internal static Expression? HandleSpecialRead(Type type, ParameterExpression output, Expression result, ParameterExpression stream,
            List<FieldInfo> fields, ImmutableSettings settings, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes,
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
                        Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, result)
                    );
                }

                return Expression.Assign(result, primitive);
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                created = true;
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, ReadTypeRefMethod), type));
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function deserialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                created = true;
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, ReadFunctionMethod), type));
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

                var elementType = type.GetElementType()!;
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
                    statements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                        SavedReferencesListAdd, result));
                }

                if (StaticTypeInfo.IsBlittable(elementType) && dimensions < 3)
                {
                    statements.Add(ReadArrayOfBlittableValues(output, result, stream, dimensions, elementType, elementSize));
                }
                else
                {
                    statements.Add(ReadArrayGeneral(output, result, stream, dimensions, elementType, elementSize, lengths, settings, visitedTypes));
                }

                return Expression.Block(lengths, statements);
            }

            var collection = ReadCollection(type, output, result, stream, settings, localVariables, visitedTypes);
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

        private static Expression? HandleCustomRead(Type type, ParameterExpression output, Expression result, ImmutableSettings settings)
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
                        var customContext = Expression.Call(output, CustomContextGetter.MakeGenericMethod(customContextType));
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(type, typeof(IBinaryReader), customContextType)),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, BinaryReaderGetter),
                            customContext));
                    }
                    else
                    {
                        statements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(type, typeof(IBinaryReader))),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, BinaryReaderGetter)));
                    }
                }
            }

            return statements.Count > 0 ? Expression.Block(statements) : null;
        }

        private static Expression ReadArrayOfBlittableValues(ParameterExpression output, Expression actualSource,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize)
        {
            return dimensions switch
            {
                1 => Expression.Call(output, ReadArrayOfValuesMethod1.MakeGenericMethod(elementType),
                       actualSource, Expression.Constant(elementSize)),
                2 => Expression.Call(output, ReadArrayOfValuesMethod2.MakeGenericMethod(elementType),
                       actualSource, Expression.Constant(elementSize)),
                _ => throw new InvalidOperationException($"Blitting multidimensional array with {dimensions} dimensions is not supported"),
            };
        }

        private static Expression ReadArrayGeneral(ParameterExpression output, Expression result,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize,
            List<ParameterExpression> lengths, ImmutableSettings settings, HashSet<Type> visitedTypes)
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

            var readValue = ReadValue(stream, output, settings, elementType, localVariables, visitedTypes, out var isSimpleRead);

            if (!isSimpleRead && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType)
                && !typeof(Type).IsAssignableFrom(elementType)
                && !typeof(Delegate).IsAssignableFrom(elementType))
            {
                var fields = TypeFields.GetOrderedFields(elementType);
                if (fields.Count > 2)
                {
                    var tempVar = Expression.Variable(elementType, "tempElement");
                    var elementReadStatements = GetReadStatementsForType(elementType, settings, stream, output,
                        tempVar, fields, localVariables, visitedTypes);
                    elementReadStatements.Add(Expression.Assign(accessExpression, tempVar));
                    readValue = Expression.Block(new[] { tempVar }, elementReadStatements);
                }
                else
                {
                    readValue = Expression.Block(GetReadStatementsForType(elementType, settings, stream, output,
                        accessExpression, fields, localVariables, visitedTypes));
                }

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
                                                        Expression.Call(output, SavedReferencesGetter), 
                                                    SavedReferencesListIndexer, Expression.Decrement(refIndex)), 
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

        private static Expression? ReadStructExpression(Type type, ParameterExpression stream,
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
                    if (fields.Count == 0)
                    {
                        return Expression.Default(type);
                    }

                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("ReadValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    return Expression.Call(stream, method);
                }
            }

            return null;
        }

        internal static Expression? ReadCollection(Type type, ParameterExpression output, Expression result,
            ParameterExpression stream, ImmutableSettings settings, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes)
        {
            return ReadDictionary(type, output, result, stream, settings, localVariables, visitedTypes)
                ?? ReadList(type, output, result, stream, settings, localVariables, visitedTypes);
        }

        private static MethodInfo fieldInfoSetValueMethod = typeof(FieldInfo).GetMethod("SetValue", new[] { typeof(object), typeof(object) })!;

        internal static Expression GetReadFieldExpression(FieldInfo fieldInfo, Expression result,
            ParameterExpression stream, ParameterExpression output,
            ImmutableSettings settings, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes)
        {
            var declaredType = fieldInfo.FieldType;
            var tempValueResult = Expression.Variable(declaredType);
            var statements = new List<Expression>
            {
                Expression.Assign(tempValueResult, ReadValue(stream, output, settings, declaredType, localVariables, visitedTypes, out _))
            };


            if (fieldInfo.Attributes.HasFlag(FieldAttributes.InitOnly))
            {
                if(FieldInfoModifier.setFieldInfoNotReadonly != null)
                {
                    FieldInfoModifier.setFieldInfoNotReadonly(fieldInfo);
                }
                else
                {
                    statements.Add(
                        Expression.Call(
                            Expression.Constant(fieldInfo), fieldInfoSetValueMethod, Expression.Convert(result, typeof(object)), Expression.Convert(tempValueResult, typeof(object))
                            )
                        );
                    return Expression.Block(new[] { tempValueResult }, statements);
                }
            }

            var valueAccessExpression = Expression.MakeMemberAccess(result, fieldInfo);
            statements.Add(Expression.Assign(valueAccessExpression, tempValueResult));

            return Expression.Block(new[] { tempValueResult }, statements);
        }

        private static Expression ReadValue(ParameterExpression stream, ParameterExpression output, ImmutableSettings settings, Type declaredType, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes,
            out bool isInlineRead)
        {
            var primitiveExpression = HandlePrimitiveRead(stream, output, declaredType);
            if (primitiveExpression != null)
            {
                isInlineRead = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableRead(stream, output, declaredType);
            if (nullableExpression != null)
            {
                isInlineRead = true;
                return nullableExpression;
            }

            var readStructExpression = ReadStructExpression(declaredType, stream, TypeFields.GetOrderedFields(declaredType));
            if (readStructExpression != null)
            {
                isInlineRead = true;
                return readStructExpression;
            }

            var shouldReadTypeInfo = !StaticTypeInfo.IsSealedOrHasNoDescendents(declaredType) || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldReadTypeInfo)
            {
                isInlineRead = false;
                return Expression.Convert(Expression.Call(output, "ReadInternal", null), declaredType);
            }

            if (declaredType.IsValueType)
            {
                if (TypeFields.IsPrimitive(declaredType))
                {
                    isInlineRead = true;
                    var result = Expression.Variable(declaredType);
                    localVariables.Add(result);
                    var readStatements = new List<Expression> {
                        Expression.Assign(result, Expression.Default(declaredType))
                    };
                    readStatements.AddRange(GetReadStatementsForType(declaredType, settings, stream, output,
                        result, TypeFields.GetOrderedFields(declaredType), localVariables, visitedTypes, reserveNeededSize: false));
                    readStatements.Add(result);
                    return Expression.Block(readStatements);
                }

                isInlineRead = false;
                return Expression.Call(output, "ReadValueInternal", new[] { declaredType });
            }
            else
            {
                if(!visitedTypes.Add(declaredType))
                {
                    isInlineRead = false;
                    return Expression.Call(output, "ReadSealedInternal", new[] { declaredType });
                }

                isInlineRead = true;
                var result = Expression.Variable(declaredType);
                localVariables.Add(result);
                var readStatements = new List<Expression> {
                        Expression.Assign(result, Expression.Default(declaredType))
                    };
                readStatements.AddRange(GetReadStatementsForType(declaredType, settings, stream, output,
                        result, TypeFields.GetOrderedFields(declaredType), localVariables, visitedTypes, readMetadata: true));
                readStatements.Add(result);
                return Expression.Block(readStatements);
            }
        }

        private static Expression? HandlePrimitiveRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
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

        private static Expression? HandleNullableRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            return Expression.Condition(Expression.Not(Expression.Call(output, ReadNullByteMethod)),
                Expression.Convert(Expression.Call(output, "ReadValueInternal", declaredType.GenericTypeArguments),
                    declaredType), Expression.Convert(Expression.Constant(null), declaredType));
        }

        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo SavedReferencesGetter =
            typeof(TBinary).GetProperty("LoadedObjectRefs", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo WriteObjectRefMethod =
            typeof(TBinary).GetMethod("WriteObjectRef", InstanceFlags)!;

        private static readonly MethodInfo WriteTypeRefMethod =
            typeof(TBinary).GetMethod("WriteTypeRef", InstanceFlags)!;

        private static readonly MethodInfo ReadTypeRefMethod =
            typeof(TBinary).GetMethod("ReadTypeRef", InstanceFlags)!;

        private static readonly MethodInfo SavedReferencesListAdd =
            typeof(List<object>).GetMethod("Add")!;

        private static readonly MethodInfo SavedReferencesListCountGetter =
            typeof(List<object>).GetProperty("Count")!.GetMethod!;

        private static readonly PropertyInfo SavedReferencesListIndexer =
            typeof(List<object>).GetProperty("Item", new[] { typeof(int) })!;

        private static readonly MethodInfo LoadedTypeReferencesGetter =
            typeof(TBinary).GetProperty("LoadedTypeRefs", InstanceFlags)!.GetMethod!;

        private static readonly PropertyInfo LoadedTypeListIndexer =
            typeof(List<Type>).GetProperty("Item", new[] { typeof(int) })!;

        private static readonly MethodInfo BinaryWriterGetter =
            typeof(TBinary).GetProperty("BinaryWriter", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo BinaryReaderGetter =
            typeof(TBinary).GetProperty("BinaryReader", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo CustomContextGetter =
            typeof(TBinary).GetMethod("GetCustomContext", InstanceFlags)!;

        private static readonly MethodInfo WriteNullableByteMethod = typeof(TBinary).GetMethod("WriteNullableByte", InstanceFlags)!;
        private static readonly MethodInfo ReadNullByteMethod = typeof(TBinary).GetMethod("ReadNullByte", InstanceFlags)!;

        private static readonly MethodInfo WriteFunctionMethod = typeof(TBinary).GetMethod("WriteFunction", InstanceFlags)!;
        private static readonly MethodInfo ReadFunctionMethod = typeof(TBinary).GetMethod("ReadFunction", InstanceFlags)!;

        private static readonly MethodInfo WriteArrayOfValuesMethod1 = typeof(TBinary).GetMethod("WriteValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod1 = typeof(TBinary).GetMethod("ReadIntoValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo WriteArrayOfValuesMethod2 = typeof(TBinary).GetMethod("WriteValuesArray2", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod2 = typeof(TBinary).GetMethod("ReadIntoValuesArray2", InstanceFlags)!;

        private static readonly MethodInfo QueueAfterDeserializationHook =
            typeof(TBinary).GetMethod("QueueAfterDeserializationHook", InstanceFlags)!;
    }
}
