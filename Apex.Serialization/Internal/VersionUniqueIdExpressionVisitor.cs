using System;
using FastExpressionCompiler.LightExpression;
using System.Reflection;

namespace Apex.Serialization.Internal
{
    internal class VersionUniqueIdExpressionVisitor : ExpressionVisitor
    {
        public VersionUniqueIdExpressionVisitor(Type type)
        {
            Type = type;
        }

        private int Result;

        public Type Type { get; }

        public int GetResult()
        {
            return Result;
        }

        private void Combine<T>(T a)
        {
            if (a is MethodInfo m)
            {
                Combine(m);
            }
            else if (a is Type t)
            {
                if(t.Assembly == typeof(VersionUniqueIdExpressionVisitor).Assembly)
                {
                    Combine(t.Name);
                }
                else
                {
                    Combine(t.FullName);
                }
            }
            else if (a is FieldInfo fi)
            {
                Combine(fi.Name);
                Combine(fi.FieldType);
                Combine(fi.DeclaringType);
            }
            else if (a is MemberInfo mi)
            {
                Combine(mi.Name);
                Combine(mi.MemberType);
                Combine(mi.DeclaringType);
            }
            else if (a is string s)
            {
                Combine(NonRandomHashCode.Ordinal(s));
            }
            else if (a is Delegate d)
            {
                Combine(d.Method);
            }
            else
            {
                Result = Result * 31 + (a?.GetHashCode()).GetValueOrDefault();
            }
        }
        private void Combine(MethodInfo? a)
        {
            if(a == null)
            {
                Combine(0);
                return;
            }

            Combine(a.Name);
            Combine(a.DeclaringType);

            if (a.IsGenericMethod)
            {
                foreach (var genericType in a.GetGenericArguments())
                {
                    Combine(genericType);
                }
            }
            foreach(var parameterType in a.GetParameters())
            {
                Combine(parameterType.ParameterType);
            }
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Method);
            return base.VisitBinary(node);
        }

        protected override Expression VisitBlock(BlockExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitBlock(node);
        }

        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            Combine(node.Test.FullName);
            return base.VisitCatchBlock(node);
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitConditional(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Value);
            return base.VisitConstant(node);
        }

        protected override Expression VisitDefault(DefaultExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitDefault(node);
        }

        protected override Expression VisitDynamic(DynamicExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.DelegateType);
            return base.VisitDynamic(node);
        }

        protected override ElementInit VisitElementInit(ElementInit node)
        {
            Combine(node.AddMethod);
            return base.VisitElementInit(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitExtension(node);
        }

        protected override Expression VisitGoto(GotoExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Kind);
            return base.VisitGoto(node);
        }

        protected override Expression VisitIndex(IndexExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            var getMethod = node.Indexer?.GetGetMethod();
            if (getMethod != null)
            {
                Combine(getMethod);
            }
            var setMethod = node.Indexer?.GetSetMethod();
            if (setMethod != null)
            {
                Combine(setMethod);
            }
            return base.VisitIndex(node);
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitInvocation(node);
        }

        protected override Expression VisitLabel(LabelExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitLabel(node);
        }

        protected override LabelTarget VisitLabelTarget(LabelTarget? node)
        {
            if (node != null)
            {
                Combine(node.Name);
                Combine(node.Type);
            }
            return base.VisitLabelTarget(node!);
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.ReturnType);
            return base.VisitLambda(node);
        }

        protected override Expression VisitListInit(ListInitExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitListInit(node);
        }

        protected override Expression VisitLoop(LoopExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitLoop(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Member);
            return base.VisitMember(node);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {            Combine(node.BindingType);
            Combine(node.Member);
            return base.VisitMemberAssignment(node);
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            Combine(node.BindingType);
            Combine(node.Member);
            return base.VisitMemberBinding(node);
        }

        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            // ???
            return base.VisitMemberInit(node);
        }

        protected override MemberListBinding VisitMemberListBinding(MemberListBinding node)
        {
            Combine(node.BindingType);
            Combine(node.Member);
            return base.VisitMemberListBinding(node);
        }

        protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
        {
            Combine(node.BindingType);
            Combine(node.Member);
            return base.VisitMemberMemberBinding(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Method);
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitNew(NewExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Constructor);
            return base.VisitNew(node);
        }

        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitNewArray(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Name);
            Combine(node.IsByRef);
            return base.VisitParameter(node);
        }

        protected override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitRuntimeVariables(node);
        }

        protected override Expression VisitSwitch(SwitchExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.Comparison);
            return base.VisitSwitch(node);
        }

        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            Combine(1);
            return base.VisitSwitchCase(node);
        }

        protected override Expression VisitTry(TryExpression node)
        {
            Combine(node.NodeType);
            return base.VisitTry(node);
        }

        protected override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            Combine(node.TypeOperand);
            return base.VisitTypeBinary(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            Combine(node.NodeType);
            Combine(node.Type);
            return base.VisitUnary(node);
        }

        protected override Expression VisitDebugInfo(DebugInfoExpression node)
        {
            Combine(node.Document.FileName);
            return base.VisitDebugInfo(node);
        }
    }
}
