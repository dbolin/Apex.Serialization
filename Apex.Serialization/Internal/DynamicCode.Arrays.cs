using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

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

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType, settings);

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
                        Expression.Equal(Expression.Constant(0), lengths.Aggregate((Expression)Expression.Empty(), (a,b) => a.NodeType == ExpressionType.Default ? (Expression)b : Expression.Or(a,b))),
                        Expression.Goto(skipLabel)
                        )
                    );

                if (IsBlittable(elementType, settings) && dimensions < 3)
                {
                    statements.Add(WriteArrayOfBlittableValues(output, actualSource, dimensions, elementType, elementSize));
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
            int dimensions, Type elementType, int elementSize)
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

                var (elementSize, isRef) = TypeFields.GetSizeForType(elementType, settings);

                var lengths = new List<ParameterExpression>();
                for (int i = 0; i < dimensions; ++i)
                {
                    lengths.Add(Expression.Variable(typeof(int), $"length{i}"));
                }

                var statements = new List<Expression>();
                statements.Add(ReserveConstantSize(stream, 4 * dimensions));
                statements.AddRange(lengths.Select((x, i) => Expression.Assign(x,
                    Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo))));

                var isBlittable = IsBlittable(elementType, settings) && dimensions < 3;

                if (isBlittable)
                {
                    statements.Add(ReadArrayOfBlittableValues(output, result, dimensions, elementType, lengths));
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        statements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, result));
                    }
                }
                else
                {
                    statements.Add(CheckArrayLengths(output, lengths, MinimumReadSize(elementType, settings, new Dictionary<Type, int>())));
                    statements.Add(Expression.Assign(result, Expression.NewArrayBounds(elementType, lengths)));
                    if (settings.SerializationMode == Mode.Graph)
                    {
                        statements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                            SavedReferencesListAdd, result));
                    }
                    var readElements = ReadArrayGeneral(output, result, stream, dimensions, elementType, elementSize, lengths, settings, visitedTypes, depth);
                    // The loops nest one per dimension, so an empty array with long other dimensions would still iterate them.
                    statements.Add(dimensions == 1
                        ? readElements
                        : Expression.IfThen(Expression.NotEqual(Expression.Property(result, nameof(Array.LongLength)), Expression.Constant(0L)), readElements));
                }

                return Expression.Block(lengths, statements);
            }

            return null;
        }

        private static Expression CheckArrayLengths(ParameterExpression output, List<ParameterExpression> lengths, int minimumElementBytes)
        {
            var minimum = Expression.Constant(minimumElementBytes);
            if (lengths.Count == 1)
            {
                return Expression.Call(output, CheckArrayLengthMethod, lengths[0], minimum);
            }

            var elementCount = lengths.Aggregate((Expression)Expression.Constant(1L),
                (count, length) => Expression.Call(MultiplyArrayLengthMethod, count, length));
            return Expression.Call(output, CheckArrayElementCountMethod, elementCount, minimum);
        }

        /// <summary>
        /// The fewest bytes reading a value declared as <paramref name="type"/> takes from the stream, which an array's
        /// length times this must not exceed what the stream holds.  It has to be a true lower bound, or valid data is
        /// rejected: a null reference takes its 1-byte flag, a string its 4-byte length, a null Nullable 1 byte, and a
        /// custom read possibly nothing.
        /// </summary>
        private static int MinimumReadSize(Type type, ImmutableSettings settings, Dictionary<Type, int> known)
        {
            if (type == typeof(string))
            {
                return 4;
            }

            if (!type.IsValueType)
            {
                return 1;
            }

            if (known.TryGetValue(type, out var size))
            {
                return size;
            }

            if (settings.ContainsCustomSerializedValue(type) || HasCustomRead(type, settings))
            {
                size = 0;
            }
            else if (IsBlittable(type, settings))
            {
                size = TypeFields.GetSizeForType(type, settings).size;
            }
            else if (Nullable.GetUnderlyingType(type) != null)
            {
                size = 1;
            }
            else
            {
                var fields = TypeFields.GetOrderedFields(type, settings);
                var perElement = fields.Sum(x => (long)MinimumReadSize(x.FieldType, settings, known));
                var elements = type.IsDefined(typeof(InlineArrayAttribute), false) ? InlineArrayLength(type) : 1;
                size = (int)Math.Min(int.MaxValue, perElement * elements);
            }

            known.Add(type, size);
            return size;
        }

        private static bool HasCustomRead(Type type, ImmutableSettings settings)
        {
            return settings.SupportSerializationHooks
                && settings.CustomActionDeserializers.Keys.Concat(settings.CustomActionInstantiators.Keys)
                    .Any(x => ImmutableSettings.MatchesRegistration(x, type, out _));
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
                            Expression.Call(output, CheckReadingObjectReference),
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
                                                Expression.Convert(Expression.Call(output, GetSavedReferenceMethod, refIndex), elementType)),
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
            int dimensions, Type elementType, List<ParameterExpression> lengths)
        {
            return dimensions switch
            {
                1 => Expression.Assign(result, Expression.Call(output, ReadArrayOfValuesMethod1.MakeGenericMethod(elementType),
                       lengths[0])),
                2 => Expression.Assign(result, Expression.Call(output, ReadArrayOfValuesMethod2.MakeGenericMethod(elementType),
                        lengths[0], lengths[1])),
                _ => throw new InvalidOperationException($"Blitting multidimensional array with {dimensions} dimensions is not supported"),
            };
        }

    }
}
