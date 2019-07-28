
using System.Runtime.CompilerServices;
using System.Security;

[assembly: SecurityRules(SecurityRuleSet.Level1, SkipVerificationInFullTrust = true)]
[assembly: InternalsVisibleTo("Benchmark, PublicKey=0024000004800000940000000602000000240000525341310004000001000100851713cfccbb46486740c0c4de0221716842f96f7914b58e656159d8d379f10cbdab2b2272d667678de100999d4ccf319deaef9242448d2a6a74db8ef030cb5621a15981b52610f892fae903f6384511540af2d1979c76a9895d91b9837673c1200cd0ffc1c3189113355b16f83760b8510446dd8db4bc219dfb92dcb74dbead")]
[assembly: InternalsVisibleTo("Apex.Serialization.Tests, PublicKey=0024000004800000940000000602000000240000525341310004000001000100c14ab4d07e57a809d3a75dc4cba6612a25e5504c18f85e8ddc8b3dc46587a019ce7280589018411e07c4269b5239bb891601f72a504f974f44f51ce94b2b1b5661c6f37c731d7dc184d4fc26fd2a46b55341c908cea9c9974e3dcc9815145f0bdd856e59e1fe02f2bd1a0d201b707976fe492855f61be5f7e15a94393722d8a2")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class AssertsTrueAttribute : Attribute
    {
        public AssertsTrueAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class AssertsFalseAttribute : Attribute
    {
        public AssertsFalseAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class EnsuresNotNullAttribute : Attribute
    {
        public EnsuresNotNullAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class NotNullWhenFalseAttribute : Attribute
    {
        public NotNullWhenFalseAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class NotNullWhenTrueAttribute : Attribute
    {
        public NotNullWhenTrueAttribute() { }
    }
}