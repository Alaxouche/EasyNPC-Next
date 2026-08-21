using Focus.Apps.EasyNpc.GameData.Files;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Reports
{
    [AddINotifyPropertyChangedInterface]
    public class PostBuildReportViewModel
    {
        [DependsOn(nameof(FaceGenArchiveExtractor))]
        public bool CanExtractFaceGenFiles => FaceGenArchiveExtractor is not null;
        [DependsOn(nameof(FaceTintArchiveExtractor))]
        public bool CanExtractFaceTintFiles => FaceTintArchiveExtractor is not null;
        public ArchiveExtractorViewModel? FaceGenArchiveExtractor { get; private set; }
        public ArchiveExtractorViewModel? FaceTintArchiveExtractor { get; private set; }
        public string GenerationStatus { get; private set; } = string.Empty;
        // A loose-file build ships no BSAs, so the archive checks below don't apply.
        [DependsOn(nameof(Report))]
        public bool IsLooseBuild => !Report.Archives.Any(x => !string.IsNullOrEmpty(x.ArchiveName));
        [DependsOn(nameof(Report))]
        public bool HasAllArchives => IsLooseBuild || Report.Archives.All(x => !string.IsNullOrEmpty(x.ArchiveName));
        [DependsOn(nameof(Report))]
        public bool HasAllDummyPluginsEnabled =>
            Report.Archives.All(x => !x.RequiresDummyPlugin || x.DummyPluginState == PluginState.Enabled);
        [DependsOn(nameof(Report))]
        public bool HasAllReadableArchives =>
            IsLooseBuild || Report.Archives.All(x => !string.IsNullOrEmpty(x.ArchiveName) && x.IsReadable);
        [DependsOn(nameof(Report))]
        public bool HasIssues => GetHasIssues();
        [DependsOn(nameof(Report))]
        public bool HasMultipleMergeComponents => Report.ActiveMergeComponents.Count > 1;
        [DependsOn(nameof(Report))]
        public bool HasSingleMergeComponent => Report.ActiveMergeComponents.Count == 1;
        // "Consistent" here means: nothing the merge is responsible for is wrong. FaceGen mismatches inherited from
        // the source mods are tracked separately and deliberately don't turn the report red - they exist with or
        // without EasyNPC, and telling the user to "disable the conflicting mods" for those is actively harmful
        // (it's usually USSEP, and removing it brings back the very blackface the user was trying to avoid).
        [DependsOn(nameof(Report))]
        public bool HasConsistentFaceGens => Report.Npcs.All(x => !x.HasMergeFaceGenIssue);
        [DependsOn(nameof(Report))]
        public bool HasConsistentFaceTints => Report.Npcs.All(x => x.HasConsistentFaceTint);
        [DependsOn(nameof(Report))]
        public bool HasInheritedFaceGenMismatches => Report.Npcs.Any(x => x.HasInheritedFaceGenMismatch);
        [DependsOn(nameof(Report))]
        public int VerifiedNpcCount => Report.Npcs.Count;
        [DependsOn(nameof(Report))]
        public int FaceGenConflictCount => Report.Npcs.Count(x => x.HasMergeFaceGenIssue);
        [DependsOn(nameof(Report))]
        public int FaceTintMismatchCount => Report.Npcs.Count(x => !x.HasConsistentFaceTint);
        [DependsOn(nameof(Report))]
        public int InheritedFaceGenMismatchCount => Report.Npcs.Count(x => x.HasInheritedFaceGenMismatch);
        [DependsOn(nameof(Report))]
        public IEnumerable<NpcConsistencyInfo> InconsistentFaceTintNpcs =>
            Report.Npcs.Where(x => !x.HasConsistentFaceTint);
        // Only the NPCs whose FaceGen the merge actually shipped. These are the ones an override can conflict with,
        // and the only ones the archive extractor can do anything about.
        [DependsOn(nameof(Report))]
        public IEnumerable<NpcConsistencyInfo> InconsistentHeadPartNpcs =>
            Report.Npcs.Where(x => x.HasMergeFaceGenIssue);
        [DependsOn(nameof(Report))]
        public IEnumerable<NpcConsistencyInfo> InheritedFaceGenMismatchNpcs =>
            Report.Npcs.Where(x => x.HasInheritedFaceGenMismatch);
        [DependsOn(nameof(FaceGenArchiveExtractor))]
        public bool IsFaceGenArchiveExtractionStarted => FaceGenArchiveExtractor is not null;
        [DependsOn(nameof(FaceTintArchiveExtractor))]
        public bool IsFaceTintArchiveExtractionStarted => FaceTintArchiveExtractor is not null;
        [DependsOn(nameof(Report))]
        public bool IsMainPluginEnabled => Report.MainPluginState == PluginState.Enabled;
        public bool IsReportReady { get; private set; }
        [DependsOn(nameof(Report))]
        public string? MergeComponentName => Report.ActiveMergeComponents.FirstOrDefault()?.Name;
        public PostBuildReport Report { get; private set; } = new();

        private readonly ArchiveExtractorViewModel.Factory archiveExtractorFactory;
        private readonly PostBuildReportGenerator reportGenerator;

        public PostBuildReportViewModel(
            PostBuildReportGenerator reportGenerator, ArchiveExtractorViewModel.Factory archiveExtractorFactory)
        {
            this.archiveExtractorFactory = archiveExtractorFactory;
            this.reportGenerator = reportGenerator;

            reportGenerator.Status.Subscribe(s => GenerationStatus = s);
        }

        // Builds a view model over an existing report, without a generator. Used to test the classification rules
        // (which NPCs count as conflicts) without standing up a game environment.
        internal PostBuildReportViewModel(PostBuildReport report)
        {
            archiveExtractorFactory = null!;
            reportGenerator = null!;
            Report = report;
            IsReportReady = true;
        }

        public async Task UpdateReport()
        {
            IsReportReady = false;
            FaceGenArchiveExtractor = null;
            FaceTintArchiveExtractor = null;
            Report = await reportGenerator.CreateReport();
            if (HasSingleMergeComponent)
            {
                var mergeComponent = Report.ActiveMergeComponents[0];
                var faceGenFiles = InconsistentHeadPartNpcs
                    .Where(x => x.HasFaceGenArchive && !x.HasFaceGenLoose)
                    .Select(x => new ArchiveFile(x.FaceGenArchivePath!, FileStructure.GetFaceMeshFileName(x)));
                var faceGenTintFiles = InconsistentHeadPartNpcs
                    .Where(x => x.HasFaceTintArchive && !x.HasFaceTintLoose)
                    .Select(x => new ArchiveFile(x.FaceTintArchivePath!, FileStructure.GetFaceTintFileName(x)));
                if (faceGenFiles.Any())
                    FaceGenArchiveExtractor =
                        archiveExtractorFactory(faceGenFiles.Concat(faceGenTintFiles), mergeComponent);

                var faceTintFiles = InconsistentFaceTintNpcs
                    .Where(x => x.HasFaceTintArchive && !x.HasFaceTintLoose)
                    .Select(x => new ArchiveFile(x.FaceTintArchivePath!, FileStructure.GetFaceTintFileName(x)));
                if (faceTintFiles.Any())
                    FaceTintArchiveExtractor = archiveExtractorFactory(faceTintFiles, mergeComponent);
            }
            IsReportReady = true;
        }

        // A plain-text version of the report, so a user can paste it into a bug report instead of trading screenshots.
        public string BuildTextReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"EasyNPC Next - post-build verification ({DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"Merged plugin:       {Report.MainPluginName} ({Report.MainPluginState})");
            sb.AppendLine($"Output mod:          {MergeComponentName ?? "(not found)"}");
            sb.AppendLine($"Build type:          {(IsLooseBuild ? "loose files (no BSAs)" : $"{Report.Archives.Count} archive(s)")}");
            sb.AppendLine($"NPCs verified:       {VerifiedNpcCount}");
            sb.AppendLine($"FaceGen conflicts:   {FaceGenConflictCount}");
            sb.AppendLine($"Tint mismatches:     {FaceTintMismatchCount}");
            sb.AppendLine($"Inherited mismatches:{InheritedFaceGenMismatchCount} (informational)");
            sb.AppendLine();
            if (Report.MainPluginMissingMasters.Count > 0)
            {
                sb.AppendLine("Missing masters:");
                foreach (var master in Report.MainPluginMissingMasters)
                    sb.AppendLine($"  - {master}");
                sb.AppendLine();
            }
            AppendNpcSection(sb, "FaceGen conflicts (merged face is being overridden)", InconsistentHeadPartNpcs);
            AppendNpcSection(sb, "Face tint mismatches", InconsistentFaceTintNpcs);
            AppendNpcSection(
                sb,
                "Inherited FaceGen mismatches (present with or without EasyNPC - informational)",
                InheritedFaceGenMismatchNpcs);
            return sb.ToString();
        }

        private static void AppendNpcSection(
            StringBuilder sb, string title, IEnumerable<NpcConsistencyInfo> npcs)
        {
            var list = npcs.ToList();
            sb.AppendLine($"{title}: {list.Count}");
            foreach (var npc in list)
                sb.AppendLine(
                    $"  - {npc.BasePluginName}#{npc.LocalFormIdHex} {npc.EditorId} ({npc.Name}) | " +
                    $"winner: {npc.WinningPluginName} | " +
                    $"facegen: {npc.WinningFaceGenSource?.ModComponent.Name ?? "(none)"} | " +
                    $"tint: {npc.WinningFaceTintSource?.ModComponent.Name ?? "(none)"}");
            sb.AppendLine();
        }

        private bool GetHasIssues()
        {
            return
                !IsMainPluginEnabled ||
                !HasSingleMergeComponent ||
                !HasAllDummyPluginsEnabled ||
                !HasAllArchives ||
                !HasAllReadableArchives ||
                Report.Npcs.Any(x => x.HasMergeFaceGenIssue || !x.HasConsistentFaceTint);
        }
    }
}
