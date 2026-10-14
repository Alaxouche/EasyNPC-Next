using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Build.Checks;
using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using Moq;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build.Checks
{
    public class PreviousMergesTests
    {
        private const string ModsRoot = @"C:\mods";

        private readonly Mock<IModRepository> modRepository = new();
        private readonly PreviousMerges check;

        public PreviousMergesTests()
        {
            check = new(modRepository.Object);
        }

        [Fact]
        public void WhenAnotherEnabledModHasAMerge_YieldsWarning()
        {
            SetUpMerges(Component("NPC Merge - Old"));

            var warnings = check.Run(EmptyProfile(), Settings("NPC Merge - New")).ToList();

            Assert.Collection(warnings, x => Assert.Equal(BuildWarningId.PreviousMergeEnabled, x.Id));
        }

        [Fact]
        public void WhenTheOnlyMergeIsTheOutputMod_Ignores()
        {
            SetUpMerges(Component("NPC Merge"));

            Assert.Empty(check.Run(EmptyProfile(), Settings("NPC Merge")));
        }

        [Fact]
        public void WhenOldMergeIsDisabled_Ignores()
        {
            SetUpMerges(Component("NPC Merge - Old", isEnabled: false));

            Assert.Empty(check.Run(EmptyProfile(), Settings("NPC Merge - New")));
        }

        private static ModComponentInfo Component(string name, bool isEnabled = true)
        {
            return new ModComponentInfo(
                new ModLocatorKey(name, name), name, name, System.IO.Path.Combine(ModsRoot, name), isEnabled);
        }

        private static Profile EmptyProfile()
        {
            return new Profile(Enumerable.Empty<INpc>());
        }

        private static BuildSettings Settings(string outputModName)
        {
            return new BuildSettings(EmptyProfile(), System.IO.Path.Combine(ModsRoot, outputModName), outputModName);
        }

        private void SetUpMerges(params ModComponentInfo[] components)
        {
            modRepository
                .Setup(x => x.SearchForFiles(FileStructure.MergeFileName, false, false))
                .Returns(components.Select(c => new ModSearchResult(c, FileStructure.MergeFileName)).ToList());
        }
    }
}
