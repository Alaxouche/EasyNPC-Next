using Moq;
using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Focus.Providers.Mutagen.Tests
{
    public class GameSettingsTest
    {
        private const string GameDataPath = @"C:\game\data";

        private static readonly GameRelease GameRelease = GameRelease.SkyrimSE;

        private readonly Mock<IArchiveStatics> archiveMock;
        private readonly Mock<IReadOnlyGameEnvironment<ISkyrimModGetter>> environmentMock;
        private readonly Mock<ILoadOrder<IModListing<ISkyrimModGetter>>> loadOrderMock;
        private readonly MockFileSystem fs;
        private readonly GameSettings<ISkyrimModGetter> settings;

        public GameSettingsTest()
        {
            archiveMock = new Mock<IArchiveStatics>();
            archiveMock.Setup(x => x.GetIniListings(GameRelease)).Returns(new[]
            {
                new FileName("ini1.bsa"),
                new FileName("ini2.bsa"),
            });
            archiveMock.Setup(x => x.GetExtension(GameRelease)).Returns(".bsa");
            environmentMock = new Mock<IReadOnlyGameEnvironment<ISkyrimModGetter>>();
            loadOrderMock = new Mock<ILoadOrder<IModListing<ISkyrimModGetter>>>();
            loadOrderMock.Setup(x => x.ListedOrder).Returns(new[]
            {
                ModListing("plugin1.esp"),
                ModListing("plugin2.esp"),
                ModListing("plugin3.esp"),
            });
            environmentMock.SetupGet(x => x.DataFolderPath).Returns(GameDataPath);
            environmentMock.SetupGet(x => x.LoadOrder).Returns(loadOrderMock.Object);
            var gameSelection = new GameSelection(GameRelease);
            fs = new MockFileSystem();
            settings = new GameSettings<ISkyrimModGetter>(
                environmentMock.Object, archiveMock.Object, gameSelection, fs);
        }

        [Fact]
        public void ArchiveOrder_ListsExistingBaseGameArchivesThenPerPluginArchivesInLoadOrder()
        {
            // The archive order is built by hand (Mutagen's own archive sort crashes in 0.53). Only archives that
            // actually exist in the data folder are included: ini-listed base archives first, then each plugin's
            // "<name>.bsa" and "<name> - Textures.bsa" in load order.
            foreach (var name in new[]
            {
                "ini1.bsa", "ini2.bsa",
                "plugin1.bsa", "plugin1 - Textures.bsa",
                "plugin2.bsa",
                // plugin3 has no archives on disk, so it should be skipped entirely.
                "unrelated.bsa",
            })
            {
                fs.AddFile(System.IO.Path.Combine(GameDataPath, name), new MockFileData(string.Empty));
            }

            var archiveOrder = settings.ArchiveOrder.ToList();

            Assert.Equal(
                new[] { "ini1.bsa", "ini2.bsa", "plugin1.bsa", "plugin1 - Textures.bsa", "plugin2.bsa" },
                archiveOrder);
        }

        [Fact]
        public void DataDirectory_ReturnsDataDirectoryFromEnvironment()
        {
            Assert.Equal(GameDataPath, settings.DataDirectory);
        }

        [Fact]
        public void PluginLoadOrder_ReturnsListedOrder()
        {
            Assert.Equal(new[] { "plugin1.esp", "plugin2.esp", "plugin3.esp" }, settings.PluginLoadOrder);
        }

        private static IModListing<ISkyrimModGetter> ModListing(string pluginName)
        {
            var listingMock = new Mock<IModListing<ISkyrimModGetter>>();
            listingMock.Setup(x => x.ModKey).Returns(ModKey.FromNameAndExtension(pluginName));
            return listingMock.Object;
        }
    }
}
