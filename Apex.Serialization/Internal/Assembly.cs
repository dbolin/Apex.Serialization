
using System.Runtime.CompilerServices;
using System.Security;

[assembly: SecurityRules(SecurityRuleSet.Level1, SkipVerificationInFullTrust = true)]
[assembly: InternalsVisibleTo("Benchmark")]
[assembly: InternalsVisibleTo("Apex.Serialization.Tests")]
