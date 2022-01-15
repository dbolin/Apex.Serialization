﻿using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static System.Linq.Expressions.ExpressionType;
using FastExpressionCompiler.LightExpression;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
        where TStream : IBinaryStream
        where TBinary : ISerializer
    {
        private static Expression? WriteArray(Type type,
            ParameterExpression stream,
            ParameterExpression output,
            Expression actualSource,
            ImmutableSettings settings,
            ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            if (type.IsArray)
            {
                var elementType = type.GetElementType()!;
                var dimensions = type.GetArrayRank();

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType);

                var lengths = new List<ParameterExpression>();
                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int), $"length{i}"));
                }

                var statements = new List<Expression>();

                statements.Add(ReserveConstantSize(stream, 4 * dimensions));
                statements.AddRange(lengths.Select((x, i) =>
                    Expression.Assign(x, Expression.Call(actualSource, "GetLength", Array.Empty<Type>(), Expression.Constant(i)))));
                statements.AddRange(lengths.Select(x =>
                    Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo, x)));

                // don't write anything else if lengths are zero
                var skipLabel = Expression.Label("skipWrite");
                statements.Add(
                    Expression.IfThen(
                        Expression.Equal(Expression.Constant(0), lengths.Aggregate((Expression)Expression.Empty(), (a,b) => a.NodeType == Default ? (Expression)b : Expression.Or(a,b))),
                        Expression.Goto(skipLabel)
                        )
                    );

                if (IsBlittable(elementType) && dimensions < 3)
                {
                    statements.Add(WriteArrayOfBlittableValues(output, actualSource, stream, dimensions, elementType, elementSize));
                }
                else
                {
                    statements.Add(WriteArrayGeneral(output, actualSource, stream, dimensions, lengths, elementType, elementSize, settings, visitedTypes, depth));
                }

                statements.Add(Expression.Label(skipLabel));

                return Expression.Block(lengths, statements);
            }

            return null;
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
            ImmutableSettings settings,
            ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            var indices = new List<ParameterExpression>();
            var breakLabels = new List<LabelTarget>();
            var continueLabels = new List<LabelTarget>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int), $"i{i}"));
                breakLabels.Add(Expression.Label($"break{i}"));
                continueLabels.Add(Expression.Label($"continue{i}"));
            }

            var accessExpression = dimensions > 1
                ? (Expression)Expression.ArrayIndex(actualSource, indices)
                : Expression.ArrayIndex(actualSource, indices[0]);

            var writeValue = WriteValue(stream, output, elementType, accessExpression, settings, visitedTypes, depth, out var isSimpleWrite);

            var shouldWriteTypeInfo = typeof(Delegate).IsAssignableFrom(elementType) || typeof(Type).IsAssignableFrom(elementType);

            if (!isSimpleWrite && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType))
            {
                writeValue = Expression.Block(GetWriteStatementsForType(elementType, settings, stream, output,
                    accessExpression, shouldWriteTypeInfo, accessExpression,
                    visitedTypes, depth, true));
            }
            else
            {
                writeValue = Expression.Block(
                    ReserveConstantSize(stream, elementSize),
                    writeValue);
            }

            var loop = writeValue;

            for (int i = 0; i < dimensions; ++i)
            {
                loop =
                    Expression.Block(
                        Expression.Assign(indices[i], Expression.Subtract(lengths[i], Expression.Constant(1))),
                        Expression.Loop(Expression.IfThenElse(
                            Expression.LessThan(indices[i], Expression.Constant(0)),
                            Expression.Break(breakLabels[i]),
                            Expression.Block(loop, Expression.Label(continueLabels[i]), Expression.Assign(indices[i], Expression.Decrement(indices[i])))
                        ), breakLabels[i])
                    );
            }

            return Expression.Block(indices, loop);
        }

        private static Expression? ReadArray(Type type,
            ParameterExpression stream,
            ParameterExpression output,
            Expression result,
            ImmutableSettings settings,
            ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            if (type.IsArray)
            {
                var elementType = type.GetElementType()!;
                var dimensions = type.GetArrayRank();

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType);

                var lengths = new List<ParameterExpression>();
                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int), $"length{i}"));
                }

                var statements = new List<Expression>();
                statements.Add(ReserveConstantSize(stream, 4 * dimensions));
                statements.AddRange(lengths.Select((x, i) => Expression.Assign(x,
                    Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo))));

                var isBlittable = IsBlittable(elementType) && dimensions < 3;

                if (isBlittable)
                {
                    statements.Add(ReadArrayOfBlittableValues(output, result, stream, dimensions, elementType, elementSize, lengths));
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        statements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, result));
                    }
                }
                else
                {
                    statements.Add(Expression.Assign(result, Expression.NewArrayBounds(elementType, lengths)));
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        statements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, result));
                    }
                    statements.Add(ReadArrayGeneral(output, result, stream, dimensions, elementType, elementSize, lengths, settings, visitedTypes, depth));
                }

                return Expression.Block(lengths, statements);
            }

            return null;
        }

        private static Expression ReadArrayGeneral(ParameterExpression output, Expression result,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize,
            List<ParameterExpression> lengths, ImmutableSettings settings, ImmutableHashSet<Type> visitedTypes,
            int depth)
        {
            var indices = new List<ParameterExpression>();
            var continueLabels = new List<LabelTarget>();
            var localVariables = new List<ParameterExpression>();

            for (int i = 0; i < dimensions; ++i)
            {
                indices.Add(Expression.Variable(typeof(int), $"index{i}"));
                continueLabels.Add(Expression.Label($"continue{i}"));
            }

            var accessExpression = dimensions > 1
                ? (Expression)Expression.ArrayAccess(result, indices)
                : Expression.ArrayAccess(result, indices[0]);

            var readValue = ReadValue(stream, output, settings, elementType, localVariables, visitedTypes, depth, out var isSimpleRead);

            if (!isSimpleRead && StaticTypeInfo.IsSealedOrHasNoDescendents(elementType)
                && !typeof(Type).IsAssignableFrom(elementType)
                && !typeof(Delegate).IsAssignableFrom(elementType))
            {
                var fields = TypeFields.GetOrderedFields(elementType, settings);
                if (fields.Count > 2)
                {
                    var tempVar = Expression.Variable(elementType, "tempElement");
                    var elementReadStatements = GetReadStatementsForType(elementType, settings, stream, output,
                        tempVar, localVariables, visitedTypes, depth);
                    elementReadStatements.Add(Expression.Assign(accessExpression, tempVar));
                    readValue = Expression.Block(new[] { tempVar }, elementReadStatements);
                }
                else
                {
                    readValue = Expression.Block(GetReadStatementsForType(elementType, settings, stream, output,
                        accessExpression, localVariables, visitedTypes, depth));
                }

                if (!elementType.IsValueType)
                {
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        var refIndex = Expression.Variable(typeof(int), "refIndex");
                        readValue = Expression.Block(
                            ReserveConstantSize(stream, 5),
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
                            ReserveConstantSize(stream, 1),
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
                    ReserveConstantSize(stream, elementSize),
                    Expression.Assign(accessExpression, readValue));
            }

            var loop = readValue;

            for (int i = 0; i < dimensions; ++i)
            {
                var breakLabel = Expression.Label();
                loop = Expression.Block(
                    Expression.Assign(indices[i], Expression.Subtract(lengths[i], Expression.Constant(1))),
                    Expression.Loop(Expression.IfThenElse(
                        Expression.LessThan(indices[i], Expression.Constant(0)),
                        Expression.Break(breakLabel),
                        Expression.Block(loop, Expression.Label(continueLabels[i]), Expression.Assign(indices[i], Expression.Decrement(indices[i])))
                    ), breakLabel)
                );
            }

            return Expression.Block(indices.Concat(localVariables), loop);
        }

        private static Expression ReadArrayOfBlittableValues(ParameterExpression output, Expression result,
            ParameterExpression stream, int dimensions, Type elementType, int elementSize, List<ParameterExpression> lengths)
        {
            return dimensions switch
            {
                1 => Expression.Assign(result, Expression.Call(output, ReadArrayOfValuesMethod1.MakeGenericMethod(elementType),
                       Expression.Constant(elementSize), lengths[0])),
                2 => Expression.Assign(result, Expression.Call(output, ReadArrayOfValuesMethod2.MakeGenericMethod(elementType),
                        Expression.Constant(elementSize), lengths[0], lengths[1])),
                _ => throw new InvalidOperationException($"Blitting multidimensional array with {dimensions} dimensions is not supported"),
            };
        }

    }
}
