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
            var enumeratorType = collectionType.GetMethod("GetEnumerator",
                BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public).ReturnType;
            var enumeratorVar = Expression.Variable(enumeratorType);
            var getEnumeratorCall = Expression.Convert(Expression.Call(actualSource, collectionType.GetMethod("GetEnumerator")), enumeratorType);
            var enumeratorAssign = Expression.Assign(enumeratorVar, getEnumeratorCall);
            var moveNextCall = Expression.Call(enumeratorVar, enumeratorType.GetMethod("MoveNext") ?? typeof(IEnumerator).GetMethod("MoveNext"));

            var loopVar = Expression.Variable(elementType);
            var countVar = Expression.Variable(typeof(int));

            var keySize = GetWriteSizeof(keyType);
            var valueSize = GetWriteSizeof(valueType);

            var breakLabel = Expression.Label();

            var loop = Expression.Block(new[] { enumeratorVar, countVar },
                Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(4)),
                Expression.Assign(countVar, Expression.Property(actualSource, collectionType.GetProperty("Count"))),
                Expression.Call(stream, BufferedStreamMethods<TStream>.GenericMethods<int>.WriteValueMethodInfo, countVar),
                Expression.IfThen(Expression.LessThanOrEqual(countVar, Expression.Constant(0)), Expression.Goto(breakLabel)),
                enumeratorAssign,
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(moveNextCall, Expression.Constant(true)),
                        Expression.Block(new[] { loopVar },
                            Expression.Assign(loopVar, Expression.Property(enumeratorVar, "Current")),
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(keySize)),
                            WriteValue(stream, output, keyType, Expression.Property(loopVar, "Key")),
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(valueSize)),
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

            var keySize = GetReadSizeof(keyType);
            var valueSize = GetReadSizeof(valueType);

            var keyVar = Expression.Variable(keyType);
            var valueVar = Expression.Variable(valueType);

            var breakLabel = Expression.Label();

            blockStatements.Add(
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Equal(countVar, Expression.Constant(0)),
                        Expression.Break(breakLabel),
                        Expression.Block( new [] {keyVar, valueVar},
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(keySize)),
                            Expression.Assign(keyVar, ReadValue(stream, output, keyType)),
                            Expression.Call(stream, BufferedStreamMethods<TStream>.ReserveSizeMethodInfo, Expression.Constant(valueSize)),
                            Expression.Assign(valueVar, ReadValue(stream, output, valueType)),
                            Expression.Call(result, collectionType.GetMethod(addMethod, collectionType.GenericTypeArguments), keyVar, valueVar),
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
