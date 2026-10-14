using Focus.Apps.EasyNpc.Profiles;
using Moq;
using Serilog;
using System;
using System.IO;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Profiles
{
    public class NpcExclusionsTests : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"EasyNpcExclusions_{Guid.NewGuid():N}.json");
        private readonly ILogger log = new Mock<ILogger>().Object;

        public void Dispose()
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        [Fact]
        public void ExcludedNpcs_SurviveARestart()
        {
            var exclusions = new NpcExclusions(path, log);
            Assert.Equal(2, exclusions.Exclude(new[] { Key("01A69F"), Key("013BBF") }));

            var reloaded = new NpcExclusions(path, log);

            Assert.Equal(2, reloaded.Count);
            Assert.True(reloaded.IsExcluded(Key("01a69f")));
        }

        [Fact]
        public void Include_RemovesOnlyThoseNpcs()
        {
            var exclusions = new NpcExclusions(path, log);
            exclusions.Exclude(new[] { Key("000001"), Key("000002") });

            Assert.Equal(1, exclusions.Include(new[] { Key("000001") }));

            Assert.False(exclusions.IsExcluded(Key("000001")));
            Assert.True(exclusions.IsExcluded(Key("000002")));
        }

        [Fact]
        public void Excluded_EmitsTheNewSetOnChange()
        {
            var exclusions = new NpcExclusions(path, log);
            var latestCount = -1;
            exclusions.Excluded.Subscribe(set => latestCount = set.Count);

            exclusions.Exclude(new[] { Key("000001") });

            Assert.Equal(1, latestCount);
        }

        private static IRecordKey Key(string localFormIdHex)
        {
            return new RecordKey("Skyrim.esm", localFormIdHex);
        }
    }
}
