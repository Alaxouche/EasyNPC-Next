using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Appearance mods (mods with a plugin that changes at least one NPC's face) that you never picked as the face
        // for any NPC - i.e. safe to remove if you only kept them to compare faces.
        public ObservableCollection<string> UnusedAppearanceMods { get; } = new();
        public bool HasScannedUnusedMods { get; private set; }
        public string UnusedModsStatus { get; private set; } = string.Empty;

        private readonly IModRepository modRepository;
        private readonly IReadOnlySet<IRecordKey> npcKeys;
        private readonly IProfileEventLog profileEventLog;
        private readonly Profile profile;

        public MaintenanceViewModel(Profile profile, IProfileEventLog profileEventLog, IModRepository modRepository)
        {
            this.profile = profile;
            this.profileEventLog = profileEventLog;
            this.modRepository = modRepository;

            npcKeys = profile.Npcs.Select(x => new RecordKey(x)).ToHashSet(RecordKeyComparer.Default);
        }

        // Lists mods that provide a face for some NPC but were never chosen. A mod is only listed when NONE of its
        // face-changing plugins are used, so it is genuinely safe to remove (for faces).
        public void FindUnusedAppearanceMods()
        {
            var usedFacePlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var faceProvidingPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var npc in profile.Npcs)
            {
                if (!npc.FaceOption.IsBaseGame)
                    usedFacePlugins.Add(npc.FaceOption.PluginName);
                foreach (var option in npc.Options)
                    if (option.Analysis.ComparisonToBase?.ModifiesFace == true)
                        faceProvidingPlugins.Add(option.PluginName);
            }
            var unused = faceProvidingPlugins
                .Select(plugin => new { Plugin = plugin, Mod = ModNameForPlugin(plugin) })
                .Where(x => !string.IsNullOrEmpty(x.Mod))
                .GroupBy(x => x.Mod!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.All(x => !usedFacePlugins.Contains(x.Plugin)))
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            UnusedAppearanceMods.Clear();
            foreach (var mod in unused)
                UnusedAppearanceMods.Add(mod);
            HasScannedUnusedMods = true;
            UnusedModsStatus = unused.Count == 0
                ? "No unused appearance mods found - every installed face mod is used by at least one NPC."
                : $"{unused.Count} appearance mod(s) provide faces you didn't pick for any NPC. If you only kept them to " +
                  "compare faces, they are safe to remove.";
        }

        private string? ModNameForPlugin(string pluginName)
        {
            return modRepository.SearchForFiles(pluginName, false)
                .Select(result => result.ModKey.Name)
                .FirstOrDefault(name => !string.IsNullOrEmpty(name));
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