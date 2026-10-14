using Focus.Testing.Files;
using Xunit;

namespace Focus.Files.Tests
{
    public class IndexedArchiveFileProviderTests
    {
        private readonly FakeArchiveProvider archiveProvider = new();

        [Fact]
        public void WhenSeveralArchivesHaveTheFile_FirstInPriorityOrderWins()
        {
            archiveProvider.AddFile("high.bsa", @"textures\a.dds", new byte[] { 1 });
            archiveProvider.AddFile("low.bsa", @"textures\a.dds", new byte[] { 2, 2 });
            var provider = new IndexedArchiveFileProvider(archiveProvider, new[] { "high.bsa", "low.bsa" });

            Assert.True(provider.Exists(@"textures\a.dds"));
            Assert.Equal(new byte[] { 1 }, provider.ReadBytes(@"textures\a.dds").ToArray());
            Assert.Equal(1U, provider.GetSize(@"textures\a.dds"));
        }

        [Fact]
        public void LookupIgnoresCase()
        {
            archiveProvider.AddFile("a.bsa", @"textures\Actors\a.dds", new byte[] { 1 });
            var provider = new IndexedArchiveFileProvider(archiveProvider, new[] { "a.bsa" });

            Assert.True(provider.Exists(@"TEXTURES\actors\A.DDS"));
        }

        [Fact]
        public void WhenNoArchiveHasTheFile_ReportsMissing()
        {
            archiveProvider.AddFile("a.bsa", @"textures\a.dds", new byte[] { 1 });
            var provider = new IndexedArchiveFileProvider(archiveProvider, new[] { "a.bsa" });

            Assert.False(provider.Exists(@"textures\b.dds"));
            Assert.Equal(0U, provider.GetSize(@"textures\b.dds"));
            Assert.True(provider.ReadBytes(@"textures\b.dds") == null);
        }
    }
}
