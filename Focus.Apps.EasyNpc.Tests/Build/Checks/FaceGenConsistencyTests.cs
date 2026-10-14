using Focus.Analysis.Plugins;
using Focus.Analysis.Records;
using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Build.Checks;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build.Checks
{
    public class FaceGenConsistencyTests
    {
        private const string BasePlugin = "Skyrim.esm";
        private const string FormIdHex = "0f9618";

        private static readonly RecordKey VanillaHead = new("Skyrim.esm", "000001");
        private static readonly RecordKey OverhaulHead = new("overhaul.esp", "000002");

        private readonly Mock<IModRepository> modRepository = new();
        private readonly FaceGenConsistency check;

        public FaceGenConsistencyTests()
        {
            check = new(modRepository.Object);
        }

        [Fact]
        public void WhenFaceIsAlsoTheDefaultButShipsNoFaceGen_YieldsMissingFaceGen()
        {
            // The case that made whole classes of generic NPCs - forsworn, thalmor, bandits - go faceless: one
            // overhaul is both the winning override and the chosen face, so nothing looked like it was changing, but
            // the overhaul restyled the head parts without shipping a FaceGen. The game keeps resolving the vanilla
            // FaceGen, which has no shape by the overhaul's head part name, and the head stops rendering.
            var npc = CreateNpc(masterHeadPart: VanillaHead, defaultHeadPart: OverhaulHead, faceHeadPart: OverhaulHead);
            SetUpPluginComponents("overhaul.esp", hasFaceGen: false);

            var warnings = check.Run(npc, Settings()).ToList();

            Assert.Collection(warnings, x => Assert.Equal(BuildWarningId.MissingFaceGen, x.Id));
        }

        [Fact]
        public void WhenFaceIsAlsoTheDefaultAndShipsFaceGen_Ignores()
        {
            var npc = CreateNpc(masterHeadPart: VanillaHead, defaultHeadPart: OverhaulHead, faceHeadPart: OverhaulHead);
            SetUpPluginComponents("overhaul.esp", hasFaceGen: true);

            Assert.Empty(check.Run(npc, Settings()));
        }

        [Fact]
        public void WhenFaceLeavesHeadPartsUnchanged_Ignores()
        {
            // Nothing is restyled, so whatever FaceGen the game already resolves still matches the record.
            var npc = CreateNpc(masterHeadPart: VanillaHead, defaultHeadPart: VanillaHead, faceHeadPart: VanillaHead);
            SetUpPluginComponents("overhaul.esp", hasFaceGen: false);

            Assert.Empty(check.Run(npc, Settings()));
        }

        private static BuildSettings Settings()
        {
            return new BuildSettings(new Profile(Enumerable.Empty<INpc>()), "", "");
        }

        private INpc CreateNpc(RecordKey masterHeadPart, RecordKey defaultHeadPart, RecordKey faceHeadPart)
        {
            var master = Option(BasePlugin, masterHeadPart);
            var @default = Option("overhaul.esp", defaultHeadPart);
            var face = Option("overhaul.esp", faceHeadPart);
            var npcMock = new Mock<INpc>();
            npcMock.SetupGet(x => x.BasePluginName).Returns(BasePlugin);
            npcMock.SetupGet(x => x.LocalFormIdHex).Returns(FormIdHex);
            npcMock.SetupGet(x => x.CanCustomizeFace).Returns(true);
            npcMock.SetupGet(x => x.EditorId).Returns("EncForsworn06");
            npcMock.SetupGet(x => x.Name).Returns("Forsworn");
            npcMock.SetupGet(x => x.Options).Returns(new List<NpcOption> { master, @default }.AsReadOnly());
            npcMock.SetupGet(x => x.DefaultOption).Returns(@default);
            npcMock.SetupGet(x => x.FaceOption).Returns(face);
            return npcMock.Object;
        }

        private static NpcOption Option(string pluginName, RecordKey headPart)
        {
            var analysis = new NpcAnalysis { MainHeadParts = new[] { headPart } };
            return new NpcOption(new Sourced<NpcAnalysis>(pluginName, analysis), false);
        }

        private void SetUpPluginComponents(string pluginName, bool hasFaceGen)
        {
            var component = new ModComponentInfo(new ModLocatorKey("1", "Overhaul"), "1", "Overhaul", @"C:\mods\o");
            modRepository
                .Setup(x => x.SearchForFiles(pluginName, false, false))
                .Returns(new[] { new ModSearchResult(component, pluginName) });
            modRepository
                .Setup(x => x.ContainsFile(It.IsAny<IEnumerable<ModComponentInfo>>(), It.IsAny<string>(), true, false))
                .Returns(hasFaceGen);
        }
    }
}
