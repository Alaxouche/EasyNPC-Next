using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Profiles;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Maintenance
{
    // A non-destructive summary of what a reset would do, so the user can review before committing.
    public class ResetPreview
    {
        public int ChangeCount { get; init; }
        public int Considered { get; init; }
        public IReadOnlyList<string> SampleChanges { get; init; } = new List<string>();
    }

    [AddINotifyPropertyChangedInterface]
    public class MaintenanceViewModel
    {
        public delegate MaintenanceViewModel Factory(Profile profile);

        // How many example lines to include in a reset preview.
        private const int PreviewSampleSize = 20;

        public int AutosaveInvalidNpcCount { get; private set; }
        public int AutosaveRecordCount { get; private set; }
        public int AutoSaveRedundantRecordCount { get; private set; }
        [DependsOn("IsDeletingLogFiles")]
        public bool CanDeleteLogFiles => !IsDeletingLogFiles;
        [DependsOn("IsResettingNpcs")]
        public bool CanResetNpcs => !IsResettingNpcs;
        [DependsOn("IsTrimmingAutoSave")]
        public bool CanTrimAutoSave => !IsTrimmingAutoSave;
        public bool IsDeletingLogFiles { get; private set; }
        public bool IsResettingNpcs { get; private set; }
        public bool IsTrimmingAutoSave { get; private set; }
        public int LogFileCount { get; private set; }
        public decimal LogFileSizeMb { get; private set; }
        public bool OnlyResetInvalid { get; set; }

        private readonly IReadOnlySet<IRecordKey> npcKeys;
        private readonly IProfileEventLog profileEventLog;
        private readonly Profile profile;

        public MaintenanceViewModel(Profile profile, IProfileEventLog profileEventLog)
        {
            this.profile = profile;
            this.profileEventLog = profileEventLog;

            npcKeys = profile.Npcs.Select(x => new RecordKey(x)).ToHashSet(RecordKeyComparer.Default);
        }

        public void DeleteOldLogFiles()
        {
            IsDeletingLogFiles = true;
            try
            {
                var logFileNames = Directory.GetFiles(ProgramData.DirectoryPath, "Log_*.txt")
                    .Where(f => f != ProgramData.LogFileName)
                    .ToList();
                foreach (var logFileName in logFileNames)
                    File.Delete(logFileName);
                RefreshLogStats();
            }
            finally
            {
                IsDeletingLogFiles = false;
            }
        }

        public void Refresh()
        {
            RefreshLogStats();
            RefreshProfileStats();
        }

        // Computes, without changing anything, which NPCs a reset would actually affect and to what. Lets the UI show
        // the impact and get confirmation first, instead of applying a blind, irreversible reset.
        public ResetPreview PreviewReset(NpcProfileField field)
        {
            var resetPredicate = GetResetPredicate(field);
            var samples = new List<string>();
            var changeCount = 0;
            var considered = 0;
            foreach (var npc in profile.Npcs)
            {
                if (!resetPredicate(npc))
                    continue;
                considered++;
                var recommendation = npc.GetPolicyRecommendation();
                var current = field == NpcProfileField.FacePlugin ?
                    npc.FaceOption.PluginName : npc.DefaultOption.PluginName;
                var recommended = field == NpcProfileField.FacePlugin ?
                    recommendation.FacePluginName : recommendation.DefaultPluginName;
                if (!string.Equals(current, recommended, StringComparison.CurrentCultureIgnoreCase))
                {
                    changeCount++;
                    if (samples.Count < PreviewSampleSize)
                    {
                        var label = !string.IsNullOrEmpty(npc.Name) ? npc.Name : npc.EditorId;
                        samples.Add($"{label}: {current} -> {recommended}");
                    }
                }
            }
            return new ResetPreview
            {
                ChangeCount = changeCount,
                Considered = considered,
                SampleChanges = samples,
            };
        }

        public void ResetNpcDefaults()
        {
            IsResettingNpcs = true;
            try
            {
                var resetPredicate = GetResetPredicate(NpcProfileField.DefaultPlugin);
                // This is only going to work on configurations that are actually loaded, i.e. for NPCs that are present
                // in the current load order AND have at least one override. It seems somehow unintuitive that this
                // won't clean up all the garbage from previous runs, but on the other hand, that's how the autosave
                // system is actually supposed to work - NPCs that are no longer "valid" are simply ignored on this run
                // but will come back with their previous settings (or best available alternative) if restored.
                //
                // Resetting is distinct from trimming; if someone has made major changes to their load order and wants
                // to ensure that their profile/autosave is absolutely squeaky clean, they should trim, THEN reset.
                foreach (var npc in profile.Npcs)
                    if (resetPredicate(npc))
                        npc.ApplyPolicy(resetDefaultPlugin: true);
            }
            finally
            {
                IsResettingNpcs = false;
            }
        }

        public void ResetNpcFaces()
        {
            IsResettingNpcs = true;
            try
            {
                var resetPredicate = GetResetPredicate(NpcProfileField.FacePlugin);
                // Refer to caveats in ResetNpcDefaults.
                foreach (var npc in profile.Npcs)
                    if (resetPredicate(npc))
                        npc.ApplyPolicy(resetFacePlugin: true);
            }
            finally
            {
                IsResettingNpcs = false;
            }
        }

        public void TrimAutoSave()
        {
            IsTrimmingAutoSave = true;
            try
            {
                profileEventLog.Erase();
                foreach (var npc in profile.Npcs)
                    npc.WriteToEventLog();
                RefreshProfileStats();
            }
            finally
            {
                IsTrimmingAutoSave = false;
            }
        }

        private Predicate<INpc> GetResetPredicate(NpcProfileField field)
        {
            if (!OnlyResetInvalid)
                return npc => true;
            return field switch
            {
                NpcProfileField.DefaultPlugin => npc => !string.IsNullOrEmpty(npc.MissingDefaultPluginName),
                NpcProfileField.FacePlugin => npc => !string.IsNullOrEmpty(npc.MissingFacePluginName),
                _ => npc => true
            };
        }

        private void RefreshLogStats()
        {
            var logFileNames = Directory.GetFiles(ProgramData.DirectoryPath, "Log_*.txt")
                .Where(f => f != ProgramData.LogFileName)
                .ToList();
            LogFileCount = logFileNames.Count;
            LogFileSizeMb = (decimal)Math.Round(
                logFileNames.Select(f => new FileInfo(f).Length / 1024f / 1024f).Sum(), 1);
        }

        private void RefreshProfileStats()
        {
            var profileEvents = ProfileEventLog.ReadEventsFromFile(ProgramData.ProfileLogFileName).ToList();
            AutosaveRecordCount = profileEvents.Count;
            AutosaveInvalidNpcCount = profileEvents.Where(x => !npcKeys.Contains(x)).Count();
            // To detect redundant events, it's more efficient to check what ISN'T redundant and then subtract.
            var profileEventGroups = profileEvents
                .GroupBy(x => Tuple.Create(x.BasePluginName, x.LocalFormIdHex, x.Field))
                .ToList();
            AutoSaveRedundantRecordCount = AutosaveRecordCount - profileEventGroups.Count;
        }
    }
}