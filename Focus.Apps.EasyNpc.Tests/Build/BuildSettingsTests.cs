using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Profiles;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build
{
    /// <summary>
    /// Covers which NPCs a build actually writes.
    /// </summary>
    /// <remarks>
    /// A reduced build is only safe if it keeps the NPCs the merge repairs regardless of user choices. Template-based
    /// NPCs (guards and other generics) are the ones at stake: leaving them out reintroduces the invisible-head bug
    /// that clearing their inherited "Traits" flag was added to fix.
    /// </remarks>
    public class BuildSettingsTests
    {
        [Fact]
        public void All_IncludesEveryNpcInTheProfile()
        {
            var settings = SettingsFor(
                NpcInclusion.All,
                Npc("001", isCustomized: true, isTemplated: false),
                Npc("002", isCustomized: false, isTemplated: false),
                Npc("003", isCustomized: false, isTemplated: true));

            Assert.Equal(3, settings.IncludedNpcCount);
            Assert.Equal(3, settings.IncludedNpcs.Count());
        }

        [Fact]
        public void CustomizedOnly_KeepsCustomizedNpcs()
        {
            var settings = SettingsFor(
                NpcInclusion.CustomizedOnly,
                Npc("001", isCustomized: true, isTemplated: false),
                Npc("002", isCustomized: false, isTemplated: false));

            var included = settings.IncludedNpcs.ToList();

            Assert.Equal("001", Assert.Single(included).LocalFormIdHex);
            Assert.Equal(1, settings.IncludedNpcCount);
        }

        [Fact]
        public void CustomizedOnly_KeepsTemplatedNpcsEvenWhenUntouched()
        {
            var settings = SettingsFor(
                NpcInclusion.CustomizedOnly,
                Npc("001", isCustomized: false, isTemplated: true),
                Npc("002", isCustomized: false, isTemplated: false));

            Assert.Equal("001", Assert.Single(settings.IncludedNpcs).LocalFormIdHex);
        }

        [Fact]
        public void CustomizedOnly_CanExcludeEverything()
        {
            var settings = SettingsFor(
                NpcInclusion.CustomizedOnly,
                Npc("001", isCustomized: false, isTemplated: false));

            Assert.Empty(settings.IncludedNpcs);
            Assert.Equal(0, settings.IncludedNpcCount);
        }

        [Fact]
        public void DefaultsToIncludingEverything()
        {
            // A build that silently dropped NPCs would be a nasty surprise, so the full merge has to be the default.
            var settings = new BuildSettings(
                new Profile(new[] { Npc("001", isCustomized: false, isTemplated: false) }), @"C:\out", "out");

            Assert.Equal(NpcInclusion.All, settings.NpcInclusion);
            Assert.Equal(1, settings.IncludedNpcCount);
        }

        private static BuildSettings SettingsFor(NpcInclusion inclusion, params INpc[] npcs)
        {
            return new BuildSettings(new Profile(npcs), @"C:\out", "out") { NpcInclusion = inclusion };
        }

        private static INpc Npc(string localFormIdHex, bool isCustomized, bool isTemplated)
        {
            var mock = new Mock<INpc>();
            mock.SetupGet(x => x.BasePluginName).Returns("Skyrim.esm");
            mock.SetupGet(x => x.LocalFormIdHex).Returns(localFormIdHex);
            // Profile only keeps NPCs whose face can be customized at all.
            mock.SetupGet(x => x.HasAvailableFaceCustomizations).Returns(true);
            mock.SetupGet(x => x.IsCustomized).Returns(isCustomized);
            mock.SetupGet(x => x.IsTemplated).Returns(isTemplated);
            return mock.Object;
        }
    }
}
