using System.IO;
using Focus.Apps.EasyNpc.Configuration;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Configuration
{
    public class SettingsTests
    {
        [Fact]
        public void EnableOnlineMugshots_DefaultsToTrue()
        {
            var path = GetTempSettingsPath();
            try
            {
                var settings = new Settings(path);
                Assert.True(settings.EnableOnlineMugshots);
            }
            finally
            {
                Delete(path);
            }
        }

        [Fact]
        public void EnableOnlineMugshots_RoundTripsThroughSaveAndLoad()
        {
            var path = GetTempSettingsPath();
            try
            {
                var settings = new Settings(path) { EnableOnlineMugshots = false };
                settings.Save();

                var reloaded = new Settings(path);
                Assert.False(reloaded.EnableOnlineMugshots);
            }
            finally
            {
                Delete(path);
            }
        }

        private static string GetTempSettingsPath() =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        private static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
