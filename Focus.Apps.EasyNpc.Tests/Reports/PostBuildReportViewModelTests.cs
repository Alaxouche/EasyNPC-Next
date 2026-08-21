using Focus.Apps.EasyNpc.Reports;
using Focus.ModManagers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Reports
{
    /// <summary>
    /// Covers how the verifier classifies an NPC's FaceGen situation.
    /// </summary>
    /// <remarks>
    /// The distinction these tests pin down is the one the report used to get wrong: an NPC whose face EasyNPC never
    /// touched (so no FaceGen was merged for it) inherits whatever FaceGen the load order provides, and a mismatch
    /// there is not a conflict with the merge. Reporting it as one - with "disable the conflicting mods" as the
    /// advice - led users to disable the Unofficial Patch and reintroduce the blackface they were trying to fix.
    /// </remarks>
    public class PostBuildReportViewModelTests
    {
        private static readonly ModComponentInfo MergeComponent =
            new(new ModLocatorKey("1", "NPC Merge"), "merge", "NPC Merge", @"C:\mods\NPC Merge");
        private static readonly ModComponentInfo VanillaComponent =
            new(ModLocatorKey.Empty, "Vanilla", "Vanilla", @"C:\game\data");
        private static readonly ModComponentInfo OtherModComponent =
            new(new ModLocatorKey("2", "Some Overhaul"), "other", "Some Overhaul", @"C:\mods\Some Overhaul");

        [Fact]
        public void NpcWithNoMergedFaceGen_IsNotAConflict()
        {
            // The USSEP case from the field: EasyNPC merged the record but wrote no FaceGen (the NPC's face plugin is
            // also its default plugin), so the vanilla FaceGen wins - exactly as it did before the build.
            var npc = Npc(isFaceGenProvidedByMerge: false, hasConsistentHeadParts: false, faceGenSource: VanillaComponent);

            Assert.False(npc.HasMergeFaceGenIssue);
            Assert.True(npc.HasInheritedFaceGenMismatch);
        }

        [Fact]
        public void NpcWithNoMergedFaceGen_AndMatchingHeadParts_IsReportedNowhere()
        {
            var npc = Npc(isFaceGenProvidedByMerge: false, hasConsistentHeadParts: true, faceGenSource: VanillaComponent);

            Assert.False(npc.HasMergeFaceGenIssue);
            Assert.False(npc.HasInheritedFaceGenMismatch);
        }

        [Fact]
        public void MergedFaceGenOverriddenByAnotherMod_IsAConflict()
        {
            // The actionable case the section is meant for: the merge shipped a face, but another mod's loose files win.
            var npc = Npc(
                isFaceGenProvidedByMerge: true, hasConsistentHeadParts: true, faceGenSource: OtherModComponent,
                hasFaceGenOverrideConflict: true);

            Assert.True(npc.HasMergeFaceGenIssue);
            Assert.False(npc.HasInheritedFaceGenMismatch);
        }

        [Fact]
        public void MergedFaceGenWithMismatchedHeadParts_IsAConflict()
        {
            var npc = Npc(
                isFaceGenProvidedByMerge: true, hasConsistentHeadParts: false, faceGenSource: MergeComponent);

            Assert.True(npc.HasMergeFaceGenIssue);
            Assert.False(npc.HasInheritedFaceGenMismatch);
        }

        [Fact]
        public void InheritedMismatchesDoNotMakeTheReportRed()
        {
            var viewModel = ViewModelFor(
                Npc(isFaceGenProvidedByMerge: false, hasConsistentHeadParts: false, faceGenSource: VanillaComponent),
                Npc(isFaceGenProvidedByMerge: false, hasConsistentHeadParts: false, faceGenSource: VanillaComponent));

            Assert.True(viewModel.HasConsistentFaceGens);
            Assert.Equal(0, viewModel.FaceGenConflictCount);
            Assert.Equal(2, viewModel.InheritedFaceGenMismatchCount);
            Assert.True(viewModel.HasInheritedFaceGenMismatches);
        }

        [Fact]
        public void RealConflictsAreCountedAndListed()
        {
            var conflicted = Npc(
                isFaceGenProvidedByMerge: true, hasConsistentHeadParts: true, faceGenSource: OtherModComponent,
                hasFaceGenOverrideConflict: true);
            var viewModel = ViewModelFor(
                conflicted,
                Npc(isFaceGenProvidedByMerge: false, hasConsistentHeadParts: false, faceGenSource: VanillaComponent));

            Assert.False(viewModel.HasConsistentFaceGens);
            Assert.Equal(1, viewModel.FaceGenConflictCount);
            Assert.Same(conflicted, Assert.Single(viewModel.InconsistentHeadPartNpcs));
        }

        [Fact]
        public void TextReportSeparatesConflictsFromInheritedMismatches()
        {
            var viewModel = ViewModelFor(
                Npc(
                    isFaceGenProvidedByMerge: true, hasConsistentHeadParts: true, faceGenSource: OtherModComponent,
                    hasFaceGenOverrideConflict: true, editorId: "ConflictedNpc"),
                Npc(
                    isFaceGenProvidedByMerge: false, hasConsistentHeadParts: false, faceGenSource: VanillaComponent,
                    editorId: "InheritedNpc"));

            var text = viewModel.BuildTextReport();

            Assert.Contains("FaceGen conflicts:   1", text);
            Assert.Contains("ConflictedNpc", text);
            Assert.Contains("informational", text);
            Assert.Contains("InheritedNpc", text);
        }

        private static NpcConsistencyInfo Npc(
            bool isFaceGenProvidedByMerge, bool hasConsistentHeadParts, ModComponentInfo faceGenSource,
            bool hasFaceGenOverrideConflict = false, bool hasConsistentFaceTint = true, string editorId = "TestNpc")
        {
            return new NpcConsistencyInfo
            {
                BasePluginName = "Skyrim.esm",
                EditorId = editorId,
                HasConsistentFaceTint = hasConsistentFaceTint,
                HasConsistentHeadParts = hasConsistentHeadParts,
                HasFaceGenOverrideConflict = hasFaceGenOverrideConflict,
                IsFaceGenProvidedByMerge = isFaceGenProvidedByMerge,
                LocalFormIdHex = "01A696",
                Name = "Test NPC",
                WinningFaceGenSource = new AssetSource { ModComponent = faceGenSource },
                WinningPluginName = "NPC Appearances Merged.esp",
                WinningPluginSource = new AssetSource { ModComponent = MergeComponent },
            };
        }

        private static PostBuildReportViewModel ViewModelFor(params NpcConsistencyInfo[] npcs)
        {
            return new PostBuildReportViewModel(new PostBuildReport
            {
                ActiveMergeComponents = new List<ModComponentInfo> { MergeComponent }.AsReadOnly(),
                Npcs = npcs.ToList().AsReadOnly(),
            });
        }
    }
}
