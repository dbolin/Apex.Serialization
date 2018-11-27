using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Apex.Serialization.Internal.Reflection
{
    // If I didn't do it, then someone else would
    internal static class FieldInfoModifier
    {
        internal static Action<FieldInfo> setFieldInfoNotReadonly;

        static FieldInfoModifier()
        {
            var type = Type.GetType("System.Reflection.RtFieldInfo", false);
            var fieldInfo_m_Attributes = type?.GetField("m_fieldAttributes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo_m_Attributes != null)
            {
                var fieldInfoParam = Expression.Parameter(typeof(FieldInfo));
                var castedType = Expression.Convert(fieldInfoParam, type);
                var returnLabel = Expression.Label();
                setFieldInfoNotReadonly = (Action<FieldInfo>)Expression.Lambda(
                    Expression.Block(
                        Expression.Assign(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes),
                            Expression.Convert(Expression.And(Expression.Convert(Expression.MakeMemberAccess(castedType, fieldInfo_m_Attributes), typeof(int)), Expression.Constant((int)(~FieldAttributes.InitOnly)))
                                ,typeof(FieldAttributes))
                            )
                        , Expression.Return(returnLabel),
                        Expression.Label(returnLabel)
                        )
                    , fieldInfoParam
                    ).Compile();
            }
        }
    }
}
