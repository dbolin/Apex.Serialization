using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using Apex.Serialization.Internal.Reflection;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
        where TStream : IBufferedStream
        where TBinary : ISerializer
    {
        internal static Expression WriteDictionary(Type type, ParameterExpression output, ParameterExpression actualSource,
            ParameterExpression stream, ParameterExpression source, ImmutableSettings settings)
        {
            //var collectionType = TypeFields.GetCustomCollectionBaseCollection(type);
            var collectionType = type;
            if (collectionType == null || !collectionType.IsGenericType)
            {
                return null;
            }

            if (!TypeFields.IsKnownCollection(collectionType))
            {
                return null;
            }

            var genericTypeDef = collectionType.GetGenericTypeDefinition();
            if (genericTypeDef != typeof(Dictionary<,>)
                && genericTypeDef != typeof(ConcurrentDictionary<,>)
                && genericTypeDef != typeof(SortedDictionary<,>)
                && genericTypeDef != typeof(SortedList<,>)
                )
            {
                return null;
            }

            var keyType = collectionType.GetGenericArguments()[0];
            var valueType = collectionType.GetGenericArguments()[1];

            var elementType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
            var enumeratorType = typeof(IEnumerator<>).MakeGenericType(elementType);
            var enumeratorVar = Expression.Variable(enumeratorType);
            var getEnumeratorCall = Expression.Convert(Expression.Call(actualSource, collectionType.GetMethod("GetEnumerator")), enumeratorType);
            var enumeratorAssign = Expression.Assign(enumeratorVar, getEnumeratorCall);
            var moveNextCall = Expression.Call(enumeratorVar, typeof(IEnumerator).GetMethod("MoveNext"));

            var loopVar = Expression.Variable(elementType);

            var maxSize = GetWriteSizeof(keyType) + GetWriteSizeof(valueType);

            var breakLabel = Expression.Label();

            var loop = Expression.Block(new[] { enumeratorVar },
                Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4)),
                Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo,
                    Expression.Property(actualSource, collectionType.GetProperty("Count"))),
                enumeratorAssign,
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(moveNextCall, Expression.Constant(true)),
                        Expression.Block(new[] { loopVar },
                            Expression.Assign(loopVar, Expression.Property(enumeratorVar, "Current")),
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSize)),
                            WriteValue(stream, output, keyType, Expression.Property(loopVar, "Key")),
                            WriteValue(stream, output, valueType, Expression.Property(loopVar, "Value"))
                        ),
                        Expression.Break(breakLabel)
                    )
                ),
                Expression.Label(breakLabel));

            return loop;
        }

        internal static Expression ReadDictionary(Type type, ParameterExpression output, ParameterExpression result, ParameterExpression stream, ImmutableSettings settings)
        {
            //var collectionType = TypeFields.GetCustomCollectionBaseCollection(type);
            var collectionType = type;
            if (collectionType == null || !collectionType.IsGenericType)
            {
                return null;
            }

            if (!TypeFields.IsKnownCollection(collectionType))
            {
                return null;
            }

            var genericTypeDef = collectionType.GetGenericTypeDefinition();
            if (genericTypeDef != typeof(Dictionary<,>)
                && genericTypeDef != typeof(ConcurrentDictionary<,>)
                && genericTypeDef != typeof(SortedDictionary<,>)
                && genericTypeDef != typeof(SortedList<,>)
            )
            {
                return null;
            }

            var addMethod = genericTypeDef == typeof(ConcurrentDictionary<,>) ? "TryAdd" : "Add";

            var keyType = collectionType.GetGenericArguments()[0];
            var valueType = collectionType.GetGenericArguments()[1];

            var blockStatements = new List<Expression>();
            var countVar = Expression.Variable(typeof(int));
            blockStatements.Add(Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4)));
            blockStatements.Add(Expression.Assign(countVar, Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo)));

            // TODO: need to save equality comparer
            
            if (type == collectionType)
            {
                var constructor = type.GetConstructor(new Type[] { typeof(int) });
                if (constructor != null)
                {
                    blockStatements.Add(Expression.Assign(result,
                        Expression.Convert(
                            Expression.New(constructor, countVar), type)));
                }
                else
                {
                    constructor = type.GetConstructor(Array.Empty<Type>());
                    blockStatements.Add(Expression.Assign(result,
                        Expression.Convert(
                            Expression.New(constructor), type)));
                }
            }
            else
            {
                blockStatements.Add(Expression.Assign(result,
                    Expression.Convert(Expression.Call(null, GetUnitializedObjectMethodInfo, Expression.Constant(type)), type)));
            }
            

            if (settings.SerializationMode == Mode.Graph)
            {
                blockStatements.Add(Expression.Call(Expression.Call(output, SerializerMethods.SavedReferencesGetter),
                    SerializerMethods.SavedReferencesListAdd, result));
            }

            var maxSize = GetWriteSizeof(keyType) + GetWriteSizeof(valueType);

            var breakLabel = Expression.Label();

            blockStatements.Add(
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(countVar, Expression.Constant(0)),
                        Expression.Break(breakLabel),
                        Expression.Block(
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSize)),
                            Expression.Call(result, collectionType.GetMethod(addMethod, collectionType.GenericTypeArguments),
                                ReadValue(stream, output, keyType),
                                ReadValue(stream, output, valueType)
                                ),
                            Expression.AddAssign(countVar, Expression.Constant(-1))
                            )
                        )
                    )
                );

            blockStatements.Add(Expression.Label(breakLabel));

            return Expression.Block(new [] {countVar}, blockStatements);
        }
    }
}
