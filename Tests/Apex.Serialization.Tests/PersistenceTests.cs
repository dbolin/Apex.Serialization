using FluentAssertions;
using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Apex.Serialization.Tests
{
    public class PersistenceTests
    {
        private readonly ITestOutputHelper _output;

        public PersistenceTests(ITestOutputHelper testOutputHelper)
        {
            _output = testOutputHelper;
        }

        private string GetBasePath()
        {
            return Environment.CurrentDirectory.Substring(0, Environment.CurrentDirectory.IndexOf("Apex.Serialization.Tests") - 7);
        }

        private string GetTestFilePath(string fileName)
        {
            return
                Path.Combine(
                    GetBasePath(),
                    "Tests/Apex.Serialization.Tests",
                    fileName
                    );
        }

        [Fact]
        public void SimpleTest()
        {
            var args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest")} -- " + GetTestFilePath("persistence_test_1");
            var p = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            p.Start();
            p.WaitForExit(30000);
            p.HasExited.Should().BeTrue();

            args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest")} -- " + GetTestFilePath("persistence_test_1");
            p = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", args) 
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            p.Start();
            p.WaitForExit(30000);
            p.HasExited.Should().BeTrue();

            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();

            _output.WriteLine(output);
            _output.WriteLine(error);

            p.ExitCode.Should().Be(0);
        }
    }
}
