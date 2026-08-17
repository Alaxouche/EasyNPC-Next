using Focus.Analysis.Plugins;
using Focus.Analysis.Records;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Profiles;
using Moq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Profiles
{
    public class ProfilePolicyTests
    {
        // A default mock returns false for IncludeChildNpcs, which is the shipping default (children excluded).
        private readonly ProfilePolicy policy = new(new Mock<IObservableAppSettings>().Object);

        [Fact]
        public void IsModdable_ForPlayerRecord_IsFalse()
        {
            var chain = CreateChain("Skyrim.esm", "000007");
            Assert.False(policy.IsModdable(chain));
        }

        [Fact]
        public void IsModdable_ForOrdinaryNpc_IsTrue()
        {
            var chain = CreateChain("Skyrim.esm", "01A696");
            Assert.True(policy.IsModdable(chain));
        }

        [Fact]
        public void IsModdable_ForChildNpc_IsFalse()
        {
            var chain = CreateChain("Skyrim.esm", "01A696", isChild: true);
            Assert.False(policy.IsModdable(chain));
        }

        private static RecordAnalysisChain<NpcAnalysis> CreateChain(
            string basePluginName, string localFormIdHex, bool isChild = false)
        {
            var analysis = new NpcAnalysis
            {
                BasePluginName = basePluginName,
                LocalFormIdHex = localFormIdHex,
                Exists = true,
                CanUseFaceGen = true,
                IsChild = isChild,
            };
            return new RecordAnalysisChain<NpcAnalysis>(new[] { new Sourced<NpcAnalysis>(basePluginName, analysis) });
        }
    }
}
