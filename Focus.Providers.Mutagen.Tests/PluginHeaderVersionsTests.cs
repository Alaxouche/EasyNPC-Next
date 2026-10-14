using Mutagen.Bethesda;
using System;
using System.IO;
using Xunit;

namespace Focus.Providers.Mutagen.Tests
{
    public class PluginHeaderVersionsTests
    {
        private const string MissingDataFolder = @"C:\nowhere\data";

        [Theory]
        [InlineData("1.70", 1.7f)]
        [InlineData("1.71", 1.71f)]
        public void ExplicitSetting_WinsOverDetection(string setting, float expected)
        {
            Assert.Equal(expected, PluginHeaderVersions.Resolve(setting, GameRelease.SkyrimSE, MissingDataFolder, out _));
        }

        [Fact]
        public void Auto_ForOtherGames_KeepsMutagenDefault()
        {
            Assert.Null(PluginHeaderVersions.Resolve("Auto", GameRelease.Fallout4, MissingDataFolder, out _));
        }

        [Fact]
        public void Auto_WhenGameVersionUnknown_Uses170()
        {
            // 1.70 loads on every game version; a wrong guess of 1.71 crashes older ones.
            var data = Path.Combine(Path.GetTempPath(), "EasyNpcHeader_" + Guid.NewGuid().ToString("N"), "Data");
            Assert.Equal(1.7f, PluginHeaderVersions.Resolve("Auto", GameRelease.SkyrimSE, data, out var reason));
            Assert.Contains("unknown", reason);
        }
    }
}
