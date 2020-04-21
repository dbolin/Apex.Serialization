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
        public void RoundTripSucceeds()
        {
            var args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest")} --no-build -v q";
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

            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            _output.WriteLine(output);
            _output.WriteLine(error);

            p.ExitCode.Should().Be(0);

            args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest")} --no-build -v q -- {output}";
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

            output = p.StandardOutput.ReadToEnd();
            error = p.StandardError.ReadToEnd();

            _output.WriteLine(output);
            _output.WriteLine(error);

            p.ExitCode.Should().Be(0);
        }

        [Fact]
        public void ChangeOfFieldInBaseTypeFails()
        {
            var args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest")} --no-build -v q";
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

            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            _output.WriteLine(output);
            _output.WriteLine(error);

            p.ExitCode.Should().Be(0);

            args = $"run -p {Path.Combine(GetBasePath(), "DeserializeTest2")} --no-build -v q -- {output}";
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

            output = p.StandardOutput.ReadToEnd();
            error = p.StandardError.ReadToEnd();

            _output.WriteLine(output);
            _output.WriteLine(error);

            p.ExitCode.Should().Be(1);
        }
    }
}
