using System;
using System.IO;
using FastExpressionCompiler.LightExpression;
using System.Reflection;
using System.Security;

namespace Apex.Serialization.Internal.Reflection
{
    // If I didn't do it, then someone else would
    internal static class FieldInfoModifier
    {
        internal class TestReadonly
        {
            public TestReadonly()
            {
            }

            public TestReadonly(int v)
            {
                Value = v;
            }

            public readonly int Value;
        }

        internal static Action<FieldInfo>? SetFieldInfoNotReadonly { get; }
        internal static Action<FieldInfo>? SetFieldInfoReadonly { get; }

        internal static bool MustUseReflectionToSetReadonly(ImmutableSettings settings) => SetFieldInfoNotReadonly == null || settings.ForceReflectionToSetReadonlyFields;

        static FieldInfoModifier()
        {
            var type = Type.GetType("System.Reflection.RtFieldInfo", false);
            var fieldInfo_m_Attributes = type?.GetField("m_fieldAttributes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo_m_Attributes != null)
            {
                var fieldInfoParam = Expression.Parameter(typeof(FieldInfo));
                var castedType = Expression.Convert(fieldInfoParam, type!);
                var returnLabel = Expression.Label();
                SetFieldInfoNotReadonly = (Action<FieldInfo>)Expression.Lambda(
                    Expression.Block(
                        Expression.Assign(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes),
                            Expression.Convert(Expression.And(Expression.Convert(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes), typeof(int)), Expression.Constant((int)(~FieldAttributes.InitOnly)))
                                ,typeof(FieldAttributes))
                            )
                        , Expression.Return(returnLabel),
                        Expression.Label(returnLabel)
                        )
                    , fieldInfoParam
                    ).CompileFast();
                SetFieldInfoReadonly = (Action<FieldInfo>)Expression.Lambda(
                    Expression.Block(
                        Expression.Assign(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes),
                            Expression.Convert(Expression.Or(Expression.Convert(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes), typeof(int)), Expression.Constant((int)(FieldAttributes.InitOnly)))
                                , typeof(FieldAttributes))
                            )
                        , Expression.Return(returnLabel),
                        Expression.Label(returnLabel)
                        )
                    , fieldInfoParam
                    ).CompileFast();

                var s = Binary.Create(new Settings());
                try
                {
                    var test = new TestReadonly(5);
                    var m = new MemoryStream();
                    s.Write(test, m);
                    m.Seek(0, SeekOrigin.Begin);
                    test = s.Read<TestReadonly>(m);
                    if (test.Value != 5)
                    {
                        SetFieldInfoNotReadonly = null;
                        SetFieldInfoReadonly = null;
                    }
                }
                catch (VerificationException)
                {
                    SetFieldInfoNotReadonly = null;
                    SetFieldInfoReadonly = null;
                }
                finally
                {
                    s.Dispose();
                }
            }
        }
    }
}
