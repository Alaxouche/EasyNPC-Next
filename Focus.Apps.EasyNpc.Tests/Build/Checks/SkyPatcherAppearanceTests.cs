using Focus.Analysis.Plugins;
using Focus.Analysis.Records;
using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Build.Checks;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using Moq;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build.Checks
{
    public class SkyPatcherAppearanceTests
    {
        private const string DataPath = @"C:\game\data";
        private const string NpcFolder = @"C:\game\data\SKSE\Plugins\SkyPatcher\npc";

        private readonly MockFileSystem fs = new();
        private readonly SkyPatcherAppearance check;

        public SkyPatcherAppearanceTests()
        {
            var gameSettings = new Mock<IGameSettings>();
            gameSettings.SetupGet(x => x.DataDirectory).Returns(DataPath);
            var modRepository = new Mock<IModRepository>();
            check = new(fs, gameSettings.Object, modRepository.Object);
        }

        [Fact]
        public void WhenIniChangesAppearanceOfAProfileNpc_NamesTheNpc()
        {
            // Full FormID with a load order prefix, as copied from xEdit.
            fs.AddFile($@"{NpcFolder}\Replacer\ysolda.ini", new MockFileData(
                "; Ysolda replacer\r\nfilterByNpcs=Skyrim.esm|0001A69F:copyVisualStyle=MyFaces.esp|000800\r\n"));

            var warnings = check.Run(Profile(Npc("Skyrim.esm", "01A69F", "Ysolda")), Settings()).ToList();

            var warning = Assert.Single(warnings);
            Assert.Equal(BuildWarningId.SkyPatcherAppearance, warning.Id);
            Assert.Contains("Ysolda", warning.Message);
            Assert.Contains("copyVisualStyle", warning.Message);
        }

        [Fact]
        public void WhenIniOnlyChangesStats_Ignores()
        {
            fs.AddFile($@"{NpcFolder}\balance.ini", new MockFileData(
                "filterByNpcs=Skyrim.esm|0001A69F:level=30:changeStats=health=500\r\n"));

            Assert.Empty(check.Run(Profile(Npc("Skyrim.esm", "01A69F", "Ysolda")), Settings()));
        }

        [Fact]
        public void WhenIniClearsTraitsInheritanceForAGroup_Warns()
        {
            fs.AddFile($@"{NpcFolder}\bandits.ini", new MockFileData(
                "filterByFactions=Skyrim.esm|0001BCC0:removeTemplateFlags=traits,stats\r\n"));

            var warning = Assert.Single(check.Run(Profile(), Settings()));
            Assert.Contains("group of NPCs", warning.Message);
        }

        [Fact]
        public void WhenNoSkyPatcherFolder_Ignores()
        {
            Assert.Empty(check.Run(Profile(), Settings()));
        }

        private static INpc Npc(string plugin, string localFormIdHex, string editorId)
        {
            var mock = new Mock<INpc>();
            mock.SetupGet(x => x.BasePluginName).Returns(plugin);
            mock.SetupGet(x => x.LocalFormIdHex).Returns(localFormIdHex);
            mock.SetupGet(x => x.EditorId).Returns(editorId);
            mock.SetupGet(x => x.HasAvailableFaceCustomizations).Returns(true);
            mock.SetupGet(x => x.DescriptiveLabel).Returns($"{editorId} ({localFormIdHex}:{plugin})");
            mock.SetupGet(x => x.FaceOption)
                .Returns(new NpcOption(new Sourced<NpcAnalysis>("Overhaul.esp", new()), false));
            return mock.Object;
        }

        private static Profile Profile(params INpc[] npcs)
        {
            return new Profile(npcs);
        }

        private static BuildSettings Settings()
        {
            return new BuildSettings(new Profile(Enumerable.Empty<INpc>()), @"C:\mods\out", "out");
        }
    }
}
