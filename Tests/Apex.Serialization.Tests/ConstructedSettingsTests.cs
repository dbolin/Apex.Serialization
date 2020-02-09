using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ConstructedSettingsTests
    {
        [Fact]
        public void Duplicates()
        {
            int initialCount = Settings._constructedSettings.Count;
            var settings = new Settings { DisableInlining = true, SupportSerializationHooks = true };
            _ = settings.ToImmutable();
            Settings._constructedSettings.Count.Should().Be(initialCount + 1);
            settings = new Settings { DisableInlining = true, SupportSerializationHooks = true };
            _ = settings.ToImmutable();
            Settings._constructedSettings.Count.Should().Be(initialCount + 1);
        }
    }
}
