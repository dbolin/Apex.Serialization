using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal.Reflection;

namespace Apex.Serialization.Internal
{
    internal static class DynamicCodeMethods
    {
        internal struct GeneratedDelegate
        {
            public Delegate Delegate;
            public int SerializedVersionUniqueId;
        }

        internal static readonly ConcurrentDictionary<TypeKey, GeneratedDelegate> _virtualWriteMethods = new ConcurrentDictionary<TypeKey, GeneratedDelegate>();
        internal static readonly ConcurrentDictionary<TypeKey, GeneratedDelegate> _virtualReadMethods = new ConcurrentDictionary<TypeKey, GeneratedDelegate>();
        internal static readonly ThreadLocal<HashSet<FieldInfo>> _fieldsToRestoreInitOnly = new ThreadLocal<HashSet<FieldInfo>>(() => new HashSet<FieldInfo>());
        internal static readonly Dictionary<FieldInfo, int> _allFieldsToRestoreInitOnly = new Dictionary<FieldInfo, int>();
        internal static readonly object _fieldInfoModifierLock = new object();
    }

    internal static partial class DynamicCode<TStream, TBinary>
        where TStream : IBinaryStream
        where TBinary : ISerializer
    {
        internal static T GenerateWriteMethod<T>(Type type, ImmutableSettings settings,
            bool shouldWriteTypeInfo,
            bool isolated)
            where T : Delegate
        {
            return (T)DynamicCodeMethods._virtualWriteMethods.GetOrAdd(
                new TypeKey(type, settings, shouldWriteTypeInfo, isolated), 
                t => GenerateWriteMethodImpl<T>(type, settings, shouldWriteTypeInfo, isolated)).Delegate;
        }

        internal static DynamicCodeMethods.GeneratedDelegate GenerateWriteMethodImpl<T>(Type type,
            ImmutableSettings settings,
            bool shouldWriteTypeInfo,
            bool isolated)
            where T : Delegate
        {
            var stream = Expression.Parameter(typeof(TStream).MakeByRefType(), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");
            var visitedTypes = ImmutableHashSet<Type>.Empty;
            var writeStatements = new List<Expression>();

            if (isolated)
            {
                var isolatedSource = Expression.Parameter(type, "source");

                var isolatedFields = TypeFields.GetOrderedFields(type, settings);
                var isolatedFieldsSize = isolatedFields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size);
                writeStatements.Add(ReserveConstantSize(stream, isolatedFieldsSize));
                writeStatements.AddRange(isolatedFields.Select(x =>
                                    GetWriteFieldExpression(x, isolatedSource, stream, output, settings, visitedTypes, 0)));

                var isolatedBody = Expression.Block(writeStatements);
                var isolatedLambda = Expression.Lambda<T>(isolatedBody, $"Apex.Serialization.Write_Isolated_{type.FullName}", new[] { isolatedSource, stream, output }).Compile();

                return new DynamicCodeMethods.GeneratedDelegate { Delegate = isolatedLambda, SerializedVersionUniqueId = GetSerializedVersionUniqueId(type, isolatedBody) };
            }

            var source = Expression.Parameter(shouldWriteTypeInfo ? typeof(object) : type, "source");

            var localVariables = new List<ParameterExpression>();

            var castedSourceType = (ParameterExpression?)null;

            if (shouldWriteTypeInfo)
            {
                castedSourceType = Expression.Variable(type, "castedSource");
                localVariables.Add(castedSourceType);
                if(type.IsValueType)
                {
                    writeStatements.Add(Expression.Assign(castedSourceType, Expression.Unbox(source, type)));
                }
                else
                {
                    writeStatements.Add(Expression.Assign(castedSourceType, Expression.Convert(source, type)));
                }
            }

            var actualSource = castedSourceType ?? source;

            writeStatements.AddRange(GetWriteStatementsForType(type, settings, stream, output, source, shouldWriteTypeInfo, actualSource, visitedTypes, 0));

            var finalBody = Expression.Block(localVariables, writeStatements);

            var lambda = Expression.Lambda<T>(finalBody, $"Apex.Serialization.Write_{type.FullName}", new[] { source, stream, output }).Compile();

            var uniqueId = GetSerializedVersionUniqueId(type, finalBody);

            if(!shouldWriteTypeInfo)
            {
                var containerType = typeof(WriteMethods<,,>).MakeGenericType(type, typeof(TStream), settings.GetGeneratedType());
                containerType.GetField("VersionUniqueId", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, uniqueId);
            }

            return new DynamicCodeMethods.GeneratedDelegate { Delegate = lambda, SerializedVersionUniqueId = uniqueId};
        }

        private static int GetSerializedVersionUniqueId(Type type, Expression expr)
        {
            var visitor = new VersionUniqueIdExpressionVisitor(type);
            visitor.Visit(expr);
            return visitor.GetResult();
        }

        private static IEnumerable<Expression> GetWriteStatementsForType(Type type, ImmutableSettings settings, ParameterExpression stream,
            ParameterExpression output, Expression source,
            bool shouldWriteTypeInfo, Expression actualSource,
            ImmutableHashSet<Type> visitedTypes,
            int depth,
            bool writeNullByte = false, bool writeSize = true)
        {
            if(!settings.IsTypeSerializable(type))
            {
                throw new InvalidOperationException($"Type {type.FullName} was encountered during serialization but was not marked as serializable. Use Binary.MarkSerializable before creating any serializers if this type is intended to be serialized.");
            }

            var fields = TypeFields.GetOrderedFields(type, settings);
            var maxSizeNeeded = writeSize ? (IsBlittable(type) ? TypeFields.GetSizeForType(type).size : fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size)) : 0;
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

            var finishTarget = Expression.Label("finishWrite");

            var writeStatements = new List<Expression>();
            if (!writeNullByte || type.IsValueType)
            {
                writeStatements.Add(ReserveConstantSize(stream, maxSizeNeeded + metaBytes));
            }

            if (settings.SerializationMode == Mode.Graph)
            {
                if (!type.IsValueType && !typeof(Delegate).IsAssignableFrom(type)
                                      && !typeof(Type).IsAssignableFrom(type))
                {
                    writeStatements.Add(Expression.IfThen(
                        Expression.Call(output, WriteObjectRefMethod, source),
                        Expression.Goto(finishTarget)));
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
                            Expression.Call(output, WriteTypeRefMethod, Expression.Constant(type), Expression.Constant(settings.UseSerializedVersionId)),
                            ReserveConstantSize(stream, maxSizeNeeded)
                        )
                    );
                }
                else
                {
                    writeStatements.Add(Expression.Call(output, WriteTypeRefMethod, Expression.Constant(type), Expression.Constant(settings.UseSerializedVersionId)));
                }
            }

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialWrite(type, output, actualSource, stream, source, fields, settings, visitedTypes, depth);

            if (specialExpression != null)
            {
                writeStatements.Add(specialExpression);
            }
            else
            {
                CheckTypeSupported(type, fields);

                writeStatements.AddRange(fields.Select(x =>
                    GetWriteFieldExpression(x, actualSource, stream, output, settings, visitedTypes, depth + 1)));

                if (!settings.FlattenClassHierarchy && !type.IsValueType)
                {
                    var baseType = type.BaseType;
                    while (baseType != null && baseType != typeof(object))
                    {
                        if(TypeFields.GetOrderedFields(baseType, settings).Count == 0)
                        {
                            baseType = baseType.BaseType;
                            continue;
                        }

                        var writeMethod = typeof(WriteMethods<,,>.WriteSealed).MakeGenericType(baseType, typeof(TStream), settings.GetGeneratedType());
                        var generateWriteMethod = typeof(DynamicCode<,>)
                            .MakeGenericType(typeof(TStream), typeof(TBinary))
                            .GetMethod("GenerateWriteMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(writeMethod);
                        var method = generateWriteMethod.Invoke(null, new object[] { baseType, settings, false, true });
                        writeStatements.Add(Expression.Invoke(Expression.Constant(method), actualSource, stream, output));

                        DynamicCodeMethods._virtualWriteMethods.TryGetValue(
                            new TypeKey(baseType, settings, false, true),
                            out var gd);
                        var doc = Expression.SymbolDocument(gd.SerializedVersionUniqueId.ToString());
                        writeStatements.Add(Expression.DebugInfo(doc, 1, 1, 1, 1));
                        baseType = baseType.BaseType;
                    }
                }
            }


            if (!type.IsValueType && writeNullByte)
            {
                var afterWriteLabel = Expression.Label("afterWrite");
                var wrapperStatements = new List<Expression>();
                wrapperStatements.Add(ReserveConstantSize(stream, maxSizeNeeded + metaBytes));
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

            writeStatements.Add(Expression.Label(finishTarget));

            return writeStatements;
        }

        internal static Expression? HandleSpecialWrite(Type type, ParameterExpression output,
            Expression actualSource, ParameterExpression stream, Expression source, List<FieldInfo> fields,
            ImmutableSettings settings, ImmutableHashSet<Type> visitedTypes, int depth)
        {
            var primitive = HandlePrimitiveWrite(stream, output, type, actualSource);
            if(primitive != null)
            {
                return primitive;
            }

            if (typeof(Type).IsAssignableFrom(type))
            {
                return Expression.Call(output, WriteTypeRefMethod, actualSource, Expression.Constant(false));
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function serialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                return Expression.Call(output, WriteFunctionMethod, Expression.Convert(source, typeof(Delegate)));
            }

            var custom = HandleCustomWrite(output, stream, type, actualSource, settings);
            if (custom != null)
            {
                return custom;
            }

            var writeStruct = WriteStructExpression(type, actualSource, stream, fields);
            if (writeStruct != null)
            {
                return writeStruct;
            }

            var writeArray = WriteArray(type, stream, output, actualSource, settings, visitedTypes, depth);
            if(writeArray != null)
            {
                return writeArray;
            }

            return null;
        }

        private static Expression? WriteStructExpression(Type type, Expression source, ParameterExpression stream,
            List<FieldInfo> fields)
        {
            if (type.IsValueType)
            {
                if (fields.Count == 0)
                {
                    return Expression.Empty();
                }

                var isPrimitive = fields.All(x => TypeFields.IsPrimitive(x.FieldType));
                if (type.IsExplicitLayout && isPrimitive)
                {
                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("WriteValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    return Expression.Call(stream, method, source);
                }
            }

            return null;
        }

        internal static Expression GetWriteFieldExpression(FieldInfo fieldInfo,
            Expression source,
            ParameterExpression stream,
            ParameterExpression output,
            ImmutableSettings settings,
            ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            var declaredType = fieldInfo.FieldType;
            var valueAccessExpression = Expression.MakeMemberAccess(source, fieldInfo);

            return WriteValue(stream, output, declaredType, valueAccessExpression, settings, visitedTypes, depth, out _);
        }

        private static Expression WriteValue(
            ParameterExpression stream,
            ParameterExpression output,
            Type declaredType,
            Expression valueAccessExpression,
            ImmutableSettings settings,
            ImmutableHashSet<Type> visitedTypes,
            int depth,
            out bool inlineWrite)
        {
            var primitiveExpression = HandlePrimitiveWrite(stream, output, declaredType, valueAccessExpression);
            if(primitiveExpression != null)
            {
                inlineWrite = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableWrite(stream, output, declaredType, settings, visitedTypes, depth, valueAccessExpression);
            if (nullableExpression != null)
            {
                inlineWrite = true;
                return nullableExpression;
            }

            var customExpression = HandleCustomWrite(output, stream, declaredType, valueAccessExpression, settings);
            if (customExpression != null)
            {
                inlineWrite = true;
                return customExpression;
            }

            var writeStruct = WriteStructExpression(declaredType, valueAccessExpression, stream, TypeFields.GetOrderedFields(declaredType, settings));
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
                if(depth > settings.InliningMaxDepth)
                {
                    inlineWrite = false;
                    return Expression.Call(output, "WriteValueInternal", new[] { declaredType }, valueAccessExpression, Expression.Constant(false));
                }

                inlineWrite = true;
                var writeStatements = GetWriteStatementsForType(declaredType, settings, stream, output,
                    valueAccessExpression, false, valueAccessExpression,
                    visitedTypes, depth, writeSize: !TypeFields.IsPrimitive(declaredType));
                return Expression.Block(writeStatements);
            }
            else
            {
                if (visitedTypes.Contains(declaredType) || depth > settings.InliningMaxDepth)
                {
                    inlineWrite = false;
                    return Expression.Call(output, "WriteSealedInternal", new[] { declaredType }, valueAccessExpression, Expression.Constant(false));
                }

                visitedTypes = visitedTypes.Add(declaredType);

                inlineWrite = true;
                var temporaryVar = Expression.Variable(declaredType, "tempResult");
                var writeStatements = new List<Expression>
                {
                    Expression.Assign(temporaryVar, valueAccessExpression)
                };
                writeStatements.AddRange(GetWriteStatementsForType(declaredType, settings, stream, output,
                    temporaryVar, false, temporaryVar,
                    visitedTypes, depth, writeNullByte: true));
                return Expression.Block(new[] { temporaryVar },
                    writeStatements
                    );
            }
        }

        private static Expression? HandleNullableWrite(ParameterExpression stream, ParameterExpression output,
            Type declaredType, ImmutableSettings settings, ImmutableHashSet<Type> visitedTypes,
            int depth,
            Expression valueAccessExpression)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            var hasValueMethod = declaredType.GetProperty("HasValue")!.GetGetMethod()!;
            var valueMethod = declaredType.GetProperty("Value")!.GetGetMethod()!;
            var nullableType = declaredType.GenericTypeArguments[0];
            var isPrimitive = TypeFields.IsPrimitive(nullableType);

            return Expression.IfThenElse(
                Expression.Call(valueAccessExpression, hasValueMethod),
                Expression.Block(
                    new[] {
                        !isPrimitive ? ReserveConstantSize(stream, 1) : Expression.Empty(),
                        Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)1)),
                    }
                        .Concat(
                    GetWriteStatementsForType(nullableType, settings, stream, output,
                        Expression.Call(valueAccessExpression, valueMethod), false, Expression.Call(valueAccessExpression, valueMethod),
                        visitedTypes, depth, writeSize: !isPrimitive))
                    ),
                Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)0))
                );
        }

        private static Expression? HandleCustomWrite(ParameterExpression output, ParameterExpression stream, Type declaredType,
            Expression valueAccessExpression, ImmutableSettings settings)
        {
            if (!settings.SupportSerializationHooks)
            {
                return null;
            }

            var statements = new List<Expression>();
            var customWriteStatements = new List<Expression>();

            foreach (var entry in settings.CustomActionSerializers)
            {
                if (entry.Key.IsAssignableFrom(declaredType))
                {
                    var customContextType = entry.Value.CustomContextType;
                    if (customContextType != null)
                    {
                        var customContext = Expression.Call(output, CustomContextGetter.MakeGenericMethod(customContextType));
                        customWriteStatements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(declaredType, typeof(IBinaryWriter), customContextType)),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, BinaryWriterGetter),
                            customContext));
                    }
                    else
                    {
                        customWriteStatements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(declaredType, typeof(IBinaryWriter))),
                            entry.Value.InvokeMethodInfo, valueAccessExpression,
                            Expression.Call(output, BinaryWriterGetter)));
                    }
                }
            }

            if (customWriteStatements.Count == 0)
            {
                return null;
            }

            var afterWriteLabel = Expression.Label("afterWrite");
            if (!declaredType.IsValueType)
            {
                statements.Add(ReserveConstantSize(stream, 1));
                statements.Add(Expression.IfThenElse(
                        Expression.ReferenceEqual(valueAccessExpression, Expression.Constant(null)),
                        Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)0)),
                            Expression.Continue(afterWriteLabel)
                        ),
                        Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.WriteValueMethodInfo, Expression.Constant((byte)1))
                        )
                    ));
                statements.AddRange(customWriteStatements);
                statements.Add(Expression.Label(afterWriteLabel));
            }

            return statements.Count > 0 ? Expression.Block(statements) : Expression.Block(customWriteStatements);
        }

        private static Expression? HandlePrimitiveWrite(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression)
        {
            if(IsBlittable(declaredType))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.GetWriteValueMethodInfo(declaredType), valueAccessExpression);
            }

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.WriteStringMethodInfo, valueAccessExpression);
            }

            return null;
        }

        internal static T GenerateReadMethod<T>(Type type, ImmutableSettings settings, bool isBoxed, bool isolated)
            where T : Delegate
        {
            try
            {
                return (T)DynamicCodeMethods._virtualReadMethods.GetOrAdd(new TypeKey(type, settings, isBoxed, isBoxed),
                    t => GenerateReadMethodImpl<T>(type, settings, isBoxed, isolated)).Delegate;
            }
            finally
            {
                foreach(var fieldInfo in DynamicCodeMethods._fieldsToRestoreInitOnly.Value!)
                {
                    lock (DynamicCodeMethods._fieldInfoModifierLock)
                    {
                        if (DynamicCodeMethods._allFieldsToRestoreInitOnly[fieldInfo]-- == 0)
                        {
                            FieldInfoModifier.SetFieldInfoReadonly!(fieldInfo);
                        }
                    }
                }
                DynamicCodeMethods._fieldsToRestoreInitOnly.Value!.Clear();
            }
        }

        internal static DynamicCodeMethods.GeneratedDelegate GenerateReadMethodImpl<T>(Type type,
            ImmutableSettings settings,
            bool isBoxed,
            bool isolated)
            where T : Delegate
        {
            var stream = Expression.Parameter(typeof(TStream).MakeByRefType(), "stream");
            var output = Expression.Parameter(typeof(TBinary), "io");

            var localVariables = new List<ParameterExpression>();
            var readStatements = new List<Expression>();
            var visitedTypes = ImmutableHashSet<Type>.Empty;

            if (isolated)
            {
                var isolatedResult = Expression.Parameter(type, "result");

                var isolatedFields = TypeFields.GetOrderedFields(type, settings);
                var isolatedFieldsSize = isolatedFields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size);
                readStatements.Add(ReserveConstantSize(stream, isolatedFieldsSize));
                readStatements.AddRange(isolatedFields.Select(x => GetReadFieldExpression(x, isolatedResult, stream, output, settings, localVariables, visitedTypes, 0)));

                var isolatedBody = Expression.Block(localVariables, readStatements);
                var isolatedLambda = Expression.Lambda<T>(isolatedBody, $"Apex.Serialization.Read_Isolated_{type.FullName}", new[] { isolatedResult, stream, output }).Compile();

                return new DynamicCodeMethods.GeneratedDelegate { Delegate = isolatedLambda, SerializedVersionUniqueId = GetSerializedVersionUniqueId(type, isolatedBody) };
            }


            var result = Expression.Variable(type, "result");
            localVariables.Add(result);

            if(type.IsValueType)
            {
                readStatements.Add(Expression.Assign(result, Expression.Default(type)));
            }

            readStatements.AddRange(GetReadStatementsForType(type, settings, stream, output, result, localVariables, visitedTypes, 0));

            if (isBoxed)
            {
                readStatements.Add(Expression.Convert(result, typeof(object)));
            }
            else
            {
                readStatements.Add(result);
            }

            var finalBody = Expression.Block(localVariables, readStatements);
            var lambda = Expression.Lambda<T>(finalBody, $"Apex.Serialization.Read_{type.FullName}", new [] {stream, output}).Compile();

            return new DynamicCodeMethods.GeneratedDelegate { Delegate = lambda, SerializedVersionUniqueId = GetSerializedVersionUniqueId(type, finalBody) };
        }

        private static List<Expression> GetReadStatementsForType(Type type, ImmutableSettings settings, ParameterExpression stream,
            ParameterExpression output, Expression result, List<ParameterExpression> localVariables,
            ImmutableHashSet<Type> visitedTypes, int depth, bool readMetadata = false,
            bool reserveNeededSize = true)
        {
            if (!settings.IsTypeSerializable(type))
            {
                throw new InvalidOperationException($"Type {type.FullName} was encountered during deserialization but was not marked as serializable. Use Binary.MarkSerializable before creating any serializers if this type is intended to be serialized.");
            }

            var fields = TypeFields.GetOrderedFields(type, settings);
            var readStatements = new List<Expression>();

            var skipReadLabel = readMetadata ? Expression.Label("skipRead") : null;
            var maxSizeNeeded = reserveNeededSize ? (IsBlittable(type) ? TypeFields.GetSizeForType(type).size : fields.Sum(x => TypeFields.GetSizeForType(x.FieldType).size)) : 0;
            if (readMetadata)
            {
                maxSizeNeeded++;

                if(settings.SerializationMode == Mode.Graph && !type.IsValueType)
                {
                    maxSizeNeeded += 4;
                }
            }

            readStatements.Add(ReserveConstantSize(stream, maxSizeNeeded));

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
            var specialExpression = HandleSpecialRead(type, output, result, stream, fields, settings, localVariables, visitedTypes, depth, out var created);

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
                            var readValueExpression = ReadValue(stream, output, settings, field.FieldType, localVariables, visitedTypes, depth, out _);
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
                if (type.IsValueType && FieldInfoModifier.MustUseReflectionToSetReadonly(settings))
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
                            readStatements.Add(GetReadFieldExpression(field, boxedResult, stream, output, settings, localVariables, visitedTypes, depth + 1));
                        }
                        else
                        {
                            readStatements.Add(GetReadFieldExpression(field, result, stream, output, settings, localVariables, visitedTypes, depth + 1));
                        }
                    }

                    if (shouldUnbox)
                    {
                        readStatements.Add(Expression.Assign(result, Expression.Unbox(boxedResult, type)));
                    }
                }
                else
                {
                    readStatements.AddRange(fields.Select(x => GetReadFieldExpression(x, result, stream, output, settings, localVariables, visitedTypes, depth + 1)));

                    if(!settings.FlattenClassHierarchy && !type.IsValueType)
                    {
                        var baseType = type.BaseType;
                        while(baseType != null && baseType != typeof(object))
                        {
                            if(TypeFields.GetOrderedFields(baseType, settings).Count == 0)
                            {
                                baseType = baseType.BaseType;
                                continue;
                            }

                            var readMethod = typeof(WriteMethods<,,>.WriteSealed).MakeGenericType(baseType, typeof(TStream), settings.GetGeneratedType());
                            var generateReadMethod = typeof(DynamicCode<,>)
                                .MakeGenericType(typeof(TStream), typeof(TBinary))
                                .GetMethod("GenerateReadMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                                .MakeGenericMethod(readMethod);
                            var method = generateReadMethod.Invoke(null, new object[] { baseType, settings, false, true });
                            readStatements.Add(Expression.Invoke(Expression.Constant(method), result, stream, output));
                            baseType = baseType.BaseType;
                        }
                    }
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
                    || StaticTypeInfo.CannotReferenceSelf(type, settings)
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
            ImmutableHashSet<Type> visitedTypes,
            int depth,
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
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, ReadTypeRefMethod, Expression.Constant(false)), type));
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

            var custom = HandleCustomRead(type, output, stream, result, settings);
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

            var array = ReadArray(type, stream, output, result, settings, visitedTypes, depth);
            if (array != null)
            {
                created = true;
                return array;
            }

            created = false;
            return null;
        }

        private static Expression? HandleCustomRead(Type type, ParameterExpression output, ParameterExpression stream, Expression result, ImmutableSettings settings)
        {
            if (!settings.SupportSerializationHooks)
            {
                return null;
            }

            var statements = new List<Expression>();
            var customReadStatements = new List<Expression>();

            foreach (var entry in settings.CustomActionDeserializers)
            {
                if (entry.Key.IsAssignableFrom(type))
                {
                    var customContextType = entry.Value.CustomContextType;
                    if (customContextType != null)
                    {
                        var customContext = Expression.Call(output, CustomContextGetter.MakeGenericMethod(customContextType));
                        customReadStatements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,,>).MakeGenericType(type, typeof(IBinaryReader), customContextType)),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, BinaryReaderGetter),
                            customContext));
                    }
                    else
                    {
                        customReadStatements.Add(Expression.Call(
                            Expression.Convert(
                                Expression.Constant(entry.Value.Action),
                                typeof(Action<,>).MakeGenericType(type, typeof(IBinaryReader))),
                            entry.Value.InvokeMethodInfo, result,
                            Expression.Call(output, BinaryReaderGetter)));
                    }
                }
            }

            if(customReadStatements.Count == 0)
            {
                return null;
            }

            if (!type.IsValueType)
            {
                statements.Add(ReserveConstantSize(stream, 1));
                statements.Add(Expression.IfThenElse(
                        Expression.Equal(Expression.Constant((byte)0), Expression.Call(stream, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.ReadValueMethodInfo)),
                        Expression.Block(
                            Expression.Constant(null)
                        ),
                        Expression.Block(
                            customReadStatements
                        )
                    ));
            }

            return statements.Count > 0 ? Expression.Block(statements) : Expression.Block(customReadStatements);
        }

        private static Expression? ReadStructExpression(Type type, ParameterExpression stream,
            List<FieldInfo> fields)
        {
            if (type.IsValueType)
            {
                if (fields.Count == 0)
                {
                    return Expression.Default(type);
                }

                var isPrimitive = fields.All(x => TypeFields.IsPrimitive(x.FieldType));
                if (type.IsExplicitLayout && isPrimitive)
                {

                    var method = (MethodInfo) typeof(BinaryStreamMethods<>.GenericMethods<>)
                        .MakeGenericType(typeof(TStream), type)
                        .GetField("ReadValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    return Expression.Call(stream, method);
                }
            }

            return null;
        }

        internal static Expression GetReadFieldExpression(FieldInfo fieldInfo, Expression result,
            ParameterExpression stream, ParameterExpression output,
            ImmutableSettings settings, List<ParameterExpression> localVariables,
            ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            var declaredType = fieldInfo.FieldType;
            var tempValueResult = Expression.Variable(declaredType, "tempResult");
            var statements = new List<Expression>
            {
                Expression.Assign(tempValueResult, ReadValue(stream, output, settings, declaredType, localVariables, visitedTypes, depth, out _))
            };


            if (fieldInfo.Attributes.HasFlag(FieldAttributes.InitOnly))
            {
                if(FieldInfoModifier.SetFieldInfoNotReadonly != null)
                {
                    lock (DynamicCodeMethods._fieldInfoModifierLock)
                    {
                        FieldInfoModifier.SetFieldInfoNotReadonly(fieldInfo);
                        DynamicCodeMethods._fieldsToRestoreInitOnly.Value!.Add(fieldInfo);
                        if(DynamicCodeMethods._allFieldsToRestoreInitOnly.TryGetValue(fieldInfo, out var v))
                        {
                            DynamicCodeMethods._allFieldsToRestoreInitOnly[fieldInfo] = v + 1;
                        }
                        else
                        {
                            DynamicCodeMethods._allFieldsToRestoreInitOnly.Add(fieldInfo, 1);
                        }
                    }
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
            ImmutableHashSet<Type> visitedTypes,
            int depth,
            out bool isInlineRead)
        {
            var primitiveExpression = HandlePrimitiveRead(stream, output, declaredType);
            if (primitiveExpression != null)
            {
                isInlineRead = true;
                return primitiveExpression;
            }

            var nullableExpression = HandleNullableRead(stream, output, declaredType, settings, localVariables, visitedTypes, depth);
            if (nullableExpression != null)
            {
                isInlineRead = true;
                return nullableExpression;
            }

            var readStructExpression = ReadStructExpression(declaredType, stream, TypeFields.GetOrderedFields(declaredType, settings));
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
                if (depth > settings.InliningMaxDepth)
                {
                    isInlineRead = false;
                    return Expression.Call(output, "ReadValueInternal", new[] { declaredType }, Expression.Constant(false));
                }

                isInlineRead = true;
                var result = Expression.Variable(declaredType, "tempResult");
                localVariables.Add(result);
                var readStatements = new List<Expression> {
                    Expression.Assign(result, Expression.Default(declaredType))
                };
                readStatements.AddRange(GetReadStatementsForType(declaredType, settings, stream, output,
                    result, localVariables, visitedTypes, depth, reserveNeededSize: !TypeFields.IsPrimitive(declaredType)));
                readStatements.Add(result);
                return Expression.Block(readStatements);
            }
            else
            {
                if(visitedTypes.Contains(declaredType) || depth > settings.InliningMaxDepth)
                {
                    isInlineRead = false;
                    return Expression.Call(output, "ReadSealedInternal", new[] { declaredType }, Expression.Constant(false));
                }

                visitedTypes = visitedTypes.Add(declaredType);

                isInlineRead = true;
                var result = Expression.Variable(declaredType, "tempResult");
                localVariables.Add(result);
                var readStatements = new List<Expression> {
                        Expression.Assign(result, Expression.Default(declaredType))
                    };
                readStatements.AddRange(GetReadStatementsForType(declaredType, settings, stream, output,
                        result, localVariables, visitedTypes, depth, readMetadata: true));
                readStatements.Add(result);
                return Expression.Block(readStatements);
            }
        }

        private static Expression? HandlePrimitiveRead(ParameterExpression stream, ParameterExpression output, Type declaredType)
        {
            if(IsBlittable(declaredType))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.GetReadValueMethodInfo(declaredType));
            }
            

            // TODO: string interning
            if (declaredType == typeof(string))
            {
                return Expression.Call(stream, BinaryStreamMethods<TStream>.ReadStringMethodInfo);
            }

            return null;
        }

        private static Expression? HandleNullableRead(ParameterExpression stream, ParameterExpression output, Type declaredType,
            ImmutableSettings settings, List<ParameterExpression> localVariables, ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            if (!declaredType.IsGenericType || declaredType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                return null;
            }

            var nullableType = declaredType.GenericTypeArguments[0];
            var isPrimitive = TypeFields.IsPrimitive(nullableType);
            var tempResult = Expression.Variable(nullableType, "tempResult");

            return 
                Expression.Block(
                    !isPrimitive ? ReserveConstantSize(stream, 1) : Expression.Empty(),
                    Expression.Condition(
                        Expression.Equal(Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<byte>.ReadValueMethodInfo), Expression.Constant((byte)0)),
                        Expression.Default(declaredType),
                        Expression.Convert(
                            Expression.Block(new[] { tempResult },
                                GetReadStatementsForType(nullableType, settings, stream, output, tempResult, localVariables,
                                    visitedTypes, depth, reserveNeededSize: !isPrimitive)
                                .Concat(new[] { tempResult })),
                            declaredType)
                        )
                    );
        }
    }
}
