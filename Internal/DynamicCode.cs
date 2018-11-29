using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
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

            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForField(x)) + 12;

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
            }

            writeStatements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSizeNeeded)));

            // write fields for normal types, some things are special like collections
            var specialExpression = HandleSpecialWrite(type, output, actualSource, stream, source, settings);

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

                writeStatements.AddRange(fields.Select(x => GetWriteFieldExpression(x, actualSource, stream, output)));
            }

            writeStatements.Add(Expression.Label(returnTarget));

            var lambda = Expression.Lambda(Expression.Block(localVariables, writeStatements), $"Write_{type.FullName}", new [] {source, stream, output}).Compile();
            return lambda;
        }

        internal static Expression HandleSpecialWrite(Type type, ParameterExpression output, ParameterExpression actualSource, ParameterExpression stream, ParameterExpression source, ImmutableSettings settings)
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

            if (type.IsValueType && type.IsExplicitLayout)
            {
                if (type.GetFields().Any(x => !x.FieldType.IsValueType))
                {
                    throw new NotSupportedException("Structs with explicit layout and reference fields are not supported");
                }

                var method = (MethodInfo)typeof(BufferedStreamMethods<>.GenericMethods<>).MakeGenericType(typeof(TStream), type)
                    .GetField("WriteValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                return Expression.Block(
                    Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo,
                        Expression.Call(null, typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(type))),
                    Expression.Call(stream, method, actualSource)
                );
            }

            if(type.IsArray)
            {
                var elementType = type.GetElementType();
                var dimensions = type.GetArrayRank();

                var lengths = new List<ParameterExpression>();
                var indices = new List<ParameterExpression>();

                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int)));
                    indices.Add(Expression.Variable(typeof(int)));
                }

                var statements = new List<Expression>();
                statements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x,i) => Expression.Assign(x, Expression.Call(actualSource, "GetLength", Array.Empty<Type>(), Expression.Constant(i)))));
                statements.AddRange(lengths.Select(x => Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo, x)));

                var accessExpression = dimensions > 1
                    ? (Expression) Expression.ArrayIndex(actualSource, indices)
                    : Expression.ArrayIndex(actualSource, indices[0]);
                var innerWrite = WriteValue(stream, output, elementType, accessExpression);
                var loop = (Expression) Expression.Block(
                    Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(24)),
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

                statements.Add(loop);

                return Expression.Block(indices.Concat(lengths), statements);
            }

            return WriteCollection(type, output, actualSource, stream, source, settings);
        }

        internal static Expression WriteCollection(Type type, ParameterExpression output,
            ParameterExpression actualSource, ParameterExpression stream, ParameterExpression source, ImmutableSettings
                settings)
        {
            return WriteDictionary(type, output, actualSource, stream, source, settings)
                ?? WriteList(type, output, actualSource, stream, source, settings);
        }

        internal static Expression GetWriteFieldExpression(FieldInfo fieldInfo, ParameterExpression source,
            ParameterExpression stream, ParameterExpression output)
        {
            var declaredType = fieldInfo.FieldType;
            var valueAccessExpression = Expression.MakeMemberAccess(source, fieldInfo);

            return WriteValue(stream, output, declaredType, valueAccessExpression);
        }

        private static Expression WriteValue(ParameterExpression stream, ParameterExpression output, Type declaredType,
            Expression valueAccessExpression)
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

            var shouldWriteTypeInfo = !declaredType.IsSealed || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldWriteTypeInfo)
            {
                return Expression.Call(output, "WriteInternal", null, valueAccessExpression);
            }

            return Expression.Call(output, "WriteSealedInternal", new[] {declaredType}, valueAccessExpression);
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

            return Expression.IfThen(Expression.Not(Expression.Call(output, SerializerMethods.WriteNullByteMethod, Expression.Convert(valueAccessExpression, typeof(object)))),
                Expression.Call(output, "WriteSealedInternal", declaredType.GenericTypeArguments,Expression.Convert(valueAccessExpression, declaredType.GenericTypeArguments[0])));
        }

        private static int GetWriteSizeof(Type type)
        {
            if (type.IsValueType)
            {
                return (int)typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(type).Invoke(null, Array.Empty<object>());
            }

            return 5;
        }

        internal static MethodInfo GetUnitializedObjectMethodInfo = typeof(FormatterServices).GetMethod("GetUninitializedObject");
        private static Type[] emptyTypes = new Type[0];

        internal static Delegate GenerateReadMethod(Type type, ImmutableSettings settings, bool isBoxed)
        {
            var fields = TypeFields.GetFields(type);
            var maxSizeNeeded = fields.Sum(x => TypeFields.GetSizeForField(x)) + 8;

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
            var specialExpression = HandleSpecialRead(type, output, result, stream, settings);

            if (specialExpression != null)
            {
                readStatements.Add(specialExpression);
            }
            else if (!type.IsValueType)
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

            if(specialExpression == null)
            {
                if (!type.IsValueType && settings.SerializationMode == Mode.Graph)
                {
                    readStatements.Add(Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter), SerializerMethods.SavedReferencesListAdd, result));
                }

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
                    for (int i = 0; i < fields.Count; ++i)
                    {
                        var field = fields[i];
                        if (i == 0 && field.IsInitOnly)
                        {
                            localVariables.Add(boxedResult);
                            readStatements.Add(Expression.Assign(boxedResult,
                                Expression.Convert(result, typeof(object))));
                            shouldUnbox = true;
                            fieldIsBoxed = true;
                        } else if (!field.IsInitOnly && shouldUnbox)
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

        internal static Expression HandleSpecialRead(Type type, ParameterExpression output, ParameterExpression result, ParameterExpression stream, ImmutableSettings settings)
        {
            var primitive = HandlePrimitiveRead(stream, output, type);
            if (primitive != null)
            {
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
                return Expression.Assign(result, Expression.Convert(Expression.Call(output, SerializerMethods.ReadTypeRefMethod), type));
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                if (!settings.AllowFunctionSerialization)
                {
                    throw new NotSupportedException("Function deserialization is not supported unless the 'AllowFunctionSerialization' setting is true");
                }

                return Expression.Assign(result, Expression.Convert(Expression.Call(output, SerializerMethods.ReadFunctionMethod), type));
            }

            if (type.IsValueType && type.IsExplicitLayout)
            {
                if (type.GetFields().Any(x => !x.FieldType.IsValueType))
                {
                    throw new NotSupportedException("Structs with explicit layout and reference fields are not supported");
                }

                var method = (MethodInfo)typeof(BufferedStreamMethods<>.GenericMethods<>).MakeGenericType(typeof(TStream), type)
                    .GetField("ReadValueMethodInfo", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                return Expression.Block(
                    Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo,
                        Expression.Call(null, typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(type))),
                    Expression.Assign(result, Expression.Call(stream, method))
                );
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var dimensions = type.GetArrayRank();

                var lengths = new List<ParameterExpression>();
                var indices = new List<ParameterExpression>();

                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int),$"length{i}"));
                    indices.Add(Expression.Variable(typeof(int), $"index{i}"));
                }

                var statements = new List<Expression>();
                statements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4 * dimensions)));
                statements.AddRange(lengths.Select((x, i) => Expression.Assign(x, Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo))));

                statements.Add(Expression.Assign(result, Expression.NewArrayBounds(elementType, lengths)));

                if (settings.SerializationMode == Mode.Graph)
                {
                    statements.Add(Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter),
                        SerializerMethods.SavedReferencesListAdd, result));
                }

                var accessExpression = dimensions > 1
                    ? (Expression) Expression.ArrayAccess(result, indices)
                    : Expression.ArrayAccess(result, indices[0]);
                var innerRead = Expression.Block(
                        Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(24)),
                        Expression.Assign(accessExpression, ReadValue(stream, output, elementType))
                    );
                var loop = (Expression)innerRead;

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

                statements.Add(loop);

                return Expression.Block(indices.Concat(lengths), statements);
            }

            return ReadCollection(type, output, result, stream, settings);
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

            var shouldReadTypeInfo = !declaredType.IsSealed || typeof(Delegate).IsAssignableFrom(declaredType)
                || typeof(Type).IsAssignableFrom(declaredType);

            if (shouldReadTypeInfo)
            {
                return Expression.Convert(Expression.Call(output, "ReadInternal", null), declaredType);
            }

            return Expression.Call(output, "ReadSealedInternal", new[] { declaredType });
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
                Expression.Convert(Expression.Call(output, "ReadSealedInternal", declaredType.GenericTypeArguments),
                    declaredType), Expression.Convert(Expression.Constant(null), declaredType));
        }

        private static int GetReadSizeof(Type type)
        {
            if (type.IsValueType)
            {
                return (int)typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(type).Invoke(null, Array.Empty<object>());
            }

            return 5;
        }
    }
}
