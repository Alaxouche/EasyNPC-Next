using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Build.Preview;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build.Preview
{
    public class WarningGroupTests
    {
        [Fact]
        public void GroupsByCauseAndPlugin_MostSevereAndLargestFirst()
        {
            var warnings = new[]
            {
                Npc("a.esp", "000001", BuildWarningId.MissingFaceGen),
                Npc("a.esp", "000002", BuildWarningId.MissingFaceGen),
                Npc("b.esp", "000003", BuildWarningId.MissingFaceGen),
                new BuildWarning(BuildWarningId.BadArchive, "broken"),
            };

            var groups = WarningGroup.FromWarnings(warnings);

            Assert.Collection(
                groups,
                g => Assert.Equal(BuildWarningId.BadArchive, g.Id),
                g =>
                {
                    Assert.Equal("a.esp", g.PluginName);
                    Assert.Equal(2, g.Count);
                    Assert.Equal(2, g.NpcKeys.Count);
                },
                g =>
                {
                    Assert.Equal("b.esp", g.PluginName);
                    Assert.Equal(1, g.Count);
                });
            Assert.False(groups[0].HasNpcs);
            Assert.True(groups[1].CanOpenInProfile);
        }

        private static BuildWarning Npc(string plugin, string id, BuildWarningId warningId)
        {
            return new BuildWarning(plugin, new RecordKey("Skyrim.esm", id), warningId, "message");
        }
    }
}
