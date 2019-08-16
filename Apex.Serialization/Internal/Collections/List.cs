using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Apex.Serialization.Internal.Reflection;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
        where TStream : IBinaryStream
        where TBinary : ISerializer
    {
        internal static Expression? WriteList(Type type, ParameterExpression output, Expression actualSource,
            ParameterExpression stream, Expression source, ImmutableSettings settings, HashSet<Type> visitedTypes)
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
            if (genericTypeDef != typeof(List<>)
                && genericTypeDef != typeof(LinkedList<>)
                && genericTypeDef != typeof(SortedSet<>)
                && genericTypeDef != typeof(HashSet<>)
                && genericTypeDef != typeof(Stack<>)
                && genericTypeDef != typeof(Queue<>)
                && genericTypeDef != typeof(ConcurrentBag<>)
                && genericTypeDef != typeof(ConcurrentQueue<>)
                && genericTypeDef != typeof(ConcurrentStack<>)
                )
            {
                return null;
            }

            var valueType = collectionType.GetGenericArguments()[0];

            var enumeratorType = collectionType.GetMethod("GetEnumerator",
                BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)!.ReturnType;
            var enumeratorVar = Expression.Variable(enumeratorType);
            var getEnumeratorCall = Expression.Convert(Expression.Call(actualSource, collectionType.GetMethod("GetEnumerator")), enumeratorType);
            var enumeratorAssign = Expression.Assign(enumeratorVar, getEnumeratorCall);
            var moveNextCall = Expression.Call(enumeratorVar, enumeratorType.GetMethod("MoveNext") ?? typeof(IEnumerator).GetMethod("MoveNext"));

            var loopVar = Expression.Variable(valueType);

            var (maxSize, isRef) = TypeFields.GetSizeForType(valueType);

            var breakLabel = Expression.Label();

            var loop = Expression.Block(new[] { enumeratorVar },
                Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4)),
                Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo,
                    Expression.Property(actualSource, collectionType.GetProperty("Count"))),
                enumeratorAssign,
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(moveNextCall, Expression.Constant(true)),
                        Expression.Block(new[] { loopVar },
                            Expression.Assign(loopVar, Expression.Property(enumeratorVar, "Current")),
                            Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSize)),
                            WriteValue(stream, output, valueType, loopVar, settings, visitedTypes, out _)
                        ),
                        Expression.Break(breakLabel)
                    )
                ),
                Expression.Label(breakLabel));

            return loop;
        }

        internal static Expression? ReadList(Type type, ParameterExpression output, Expression result,
            ParameterExpression stream, ImmutableSettings settings, List<ParameterExpression> localVariables,
            HashSet<Type> visitedTypes)
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
            if (genericTypeDef != typeof(List<>)
                && genericTypeDef != typeof(LinkedList<>)
                && genericTypeDef != typeof(SortedSet<>)
                && genericTypeDef != typeof(HashSet<>)
                && genericTypeDef != typeof(Stack<>)
                && genericTypeDef != typeof(Queue<>)
                && genericTypeDef != typeof(ConcurrentBag<>)
                && genericTypeDef != typeof(ConcurrentQueue<>)
                && genericTypeDef != typeof(ConcurrentStack<>)
            )
            {
                return null;
            }

            var addMethod = "Add";
            if (genericTypeDef == typeof(LinkedList<>))
            {
                addMethod = "AddLast";
            } else if (genericTypeDef == typeof(Stack<>) || genericTypeDef == typeof(ConcurrentStack<>))
            {
                addMethod = "Push";
            } else if (genericTypeDef == typeof(Queue<>) || genericTypeDef == typeof(ConcurrentQueue<>))
            {
                addMethod = "Enqueue";
            }

            var valueType = collectionType.GetGenericArguments()[0];

            var blockStatements = new List<Expression>();
            var countVar = Expression.Variable(typeof(int));
            blockStatements.Add(Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4)));
            blockStatements.Add(Expression.Assign(countVar, Expression.Call(stream, BinaryStreamMethods<TStream>.GenericMethods<int>.ReadValueMethodInfo)));

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
                blockStatements.Add(Expression.Call(Expression.Call(output, SavedReferencesGetter),
                    SavedReferencesListAdd, result));
            }

            var (maxSize, isRef) = TypeFields.GetSizeForType(valueType);

            var breakLabel = Expression.Label();

            blockStatements.Add(
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(countVar, Expression.Constant(0)),
                        Expression.Break(breakLabel),
                        Expression.Block(
                            Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(maxSize)),
                            Expression.Call(result, collectionType.GetMethod(addMethod, collectionType.GenericTypeArguments),
                                ReadValue(stream, output, settings, valueType, localVariables, visitedTypes, out _)
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
