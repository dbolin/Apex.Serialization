using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Apex.Serialization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Validators;
using MessagePack;

namespace Benchmark
{
    public class Config : ManualConfig
    {
        public Config()
        {
            AddValidator(JitOptimizationsValidator.DontFailOnError);
            AddLogger(DefaultConfig.Instance.GetLoggers().ToArray()); // manual config has no loggers by default
            AddExporter(DefaultConfig.Instance.GetExporters().ToArray()); // manual config has no exporters by default
            AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray()); // manual config has no columns by default

            AddJob(Job.Default.WithToolchain(CsProjCoreToolchain.NetCoreApp80).WithGcServer(true));
            //AddJob(Job.Clr.With(CsProjClassicNetToolchain.Net472));
            //AddJob(Job.CoreRT);
            //Add(HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions);

            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }

    public sealed class T
    {
    }

    class Program
    {
        static void Main(string[] args)
        {
            /*
            var t = new PerformanceSuite_EmptyStructList();
            while(true)
                t.S_ListEmptyFullWithVersionIds();
            */

            var summaries = BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(config: new Config());

            Console.ReadKey();
        }
    }
}
