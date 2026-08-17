using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Messages;
using Ookii.Dialogs.Wpf;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Focus.Apps.EasyNpc.Profiles
{
    [AddINotifyPropertyChangedInterface]
    public class ProfileViewModel
    {
        public delegate ProfileViewModel Factory(Profile profile);

        // Mirror of SelectedNpc.SelectedMugshot; [DependsOn(SelectedNpc)] can't observe a nested property change.
        [DependsOn(nameof(SelectedMugshot))]
        public bool CanBatchApplyFace => SelectedMugshot is not null;
        // A default plugin must be a real installed plugin, so the base-game (Vanilla) card doesn't qualify.
        [DependsOn(nameof(SelectedMugshot))]
        public bool CanBatchSetDefault =>
            SelectedMugshot is not null && !SelectedMugshot.IsBaseGame && SelectedMugshot.InstalledPlugins.Count > 0;
        [DependsOn(nameof(SelectedMugshot))]
        public string? SelectedDefaultPluginName => SelectedMugshot?.InstalledPlugins.Count > 0
            ? SelectedMugshot.InstalledPlugins[0] : null;
        public NpcFiltersViewModel Filters { get; private init; } = new();
        public NpcGridViewModel Grid { get; private init; }
        public MugshotViewModel? SelectedMugshot { get; private set; }
        [DependsOn("SelectedNpc")]
        public bool HasSelectedNpc => Grid.SelectedNpc is not null;
        [DependsOn(nameof(SelectedMugshot))]
        public string? SelectedFaceModName => SelectedMugshot?.ModName;
        public NpcSearchParameters Search { get; private init; } = new();
        public NpcViewModel? SelectedNpc { get; private set; }

        private readonly HashSet<IRecordKey> alwaysVisibleNpcKeys = new(RecordKeyComparer.Default);
        private readonly FaceFinder.FaceFinderMugshotRepository faceFinder;
        private readonly Serilog.ILogger log;
        private readonly ILineupBuilder lineupBuilder;
        private readonly IMessageBus messageBus;
        private bool onlineMugshotsEnabled;
        private readonly Dictionary<IRecordKey, INpc> npcs = new(RecordKeyComparer.Default);
        private readonly Dictionary<string, int> pluginOrder;
        private readonly Profile profile;

        public ProfileViewModel(
            Profile profile, ILineupBuilder lineupBuilder, IGameSettings gameSettings, IMessageBus messageBus,
            FaceFinder.FaceFinderMugshotRepository faceFinder, Serilog.ILogger log,
            IObservableAppSettings appSettings)
        {
            this.faceFinder = faceFinder;
            this.log = log;
            this.lineupBuilder = lineupBuilder;
            this.messageBus = messageBus;

            appSettings.EnableOnlineMugshotsObservable.Subscribe(v => onlineMugshotsEnabled = v);
            this.npcs = profile.Npcs.ToDictionary(x => new RecordKey(x), RecordKeyComparer.Default);
            this.profile = profile;

            pluginOrder = gameSettings.PluginLoadOrder
                .Select((pluginName, index) => (pluginName, index))
                .ToDictionary(x => x.pluginName, x => x.index);

            Grid = new NpcGridViewModel(Search);
            Grid.WhenChanged(nameof(Grid.SelectedNpc), () => UpdateSelectedNpc(Grid.SelectedNpc));
            Filters.AvailablePlugins = gameSettings.PluginLoadOrder.OrderBy(f => f).ToList();

            Filters.PropertyChanged += (_, _) => ApplyFilters();
            Search.PropertyChanged += (_, _) => ApplyFilters();

            messageBus.Subscribe<JumpToProfile>(HandleJumpToProfile);

            ApplyFilters();
        }

        public void LoadFromFile(Window dialogOwner)
        {
            var dialog = new VistaOpenFileDialog
            {
                Title = "Choose saved profile",
                CheckFileExists = true,
                DefaultExt = ".txt",
                Filter = "Text Files (*.txt)|*.txt",
                Multiselect = false
            };
            if (dialog.ShowDialog(dialogOwner).GetValueOrDefault())
            {
                profile.Load(dialog.FileName);
                ApplyFilters();
            }
        }

        public void SaveToFile(Window dialogOwner)
        {
            var dialog = new VistaSaveFileDialog
            {
                Title = "Choose where to save this profile",
                CheckPathExists = true,
                DefaultExt = ".txt",
                Filter = "Text Files (*.txt)|*.txt",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(dialogOwner).GetValueOrDefault())
                profile.Save(dialog.FileName);
        }

        // Batch operations act on the currently *filtered* NPC set (Grid.Npcs), so the user controls the scope with the
        // normal filters - e.g. filter by "Provided in: <mod>" to grab every NPC that mod covers, then apply in one go.
        public int BatchApplySelectedFaceToFiltered()
        {
            var mugshot = SelectedNpc?.SelectedMugshot;
            if (mugshot is null)
                return 0;
            var applied = 0;
            foreach (var npc in Grid.Npcs.ToList())
                if (npc.CanCustomizeFace)
                {
                    var result = mugshot.IsBaseGame ? npc.RevertToBaseGame() : npc.SetFaceMod(mugshot.ModName);
                    if (result == NpcChangeResult.OK)
                        applied++;
                }
            RefreshAfterBatch();
            return applied;
        }

        // Sets the selected card's plugin as the Default Plugin (stats/behavior source) for every filtered NPC. Only
        // NPCs the plugin actually provides are changed - SetDefaultOption returns non-OK for the rest.
        public int BatchSetDefaultToFiltered()
        {
            var mugshot = SelectedNpc?.SelectedMugshot;
            var pluginName = mugshot?.InstalledPlugins.Count > 0 ? mugshot.InstalledPlugins[0] : null;
            if (mugshot is null || mugshot.IsBaseGame || string.IsNullOrEmpty(pluginName))
                return 0;
            var applied = 0;
            foreach (var npc in Grid.Npcs.ToList())
                if (npc.CanCustomizeFace && npc.SetDefaultOption(pluginName) == NpcChangeResult.OK)
                    applied++;
            RefreshAfterBatch();
            return applied;
        }

        public int BatchResetFilteredToVanilla()
        {
            var applied = 0;
            foreach (var npc in Grid.Npcs.ToList())
                if (npc.CanCustomizeFace && npc.RevertToBaseGame() == NpcChangeResult.OK)
                    applied++;
            RefreshAfterBatch();
            return applied;
        }

        public int CountFilteredNpcs()
        {
            return Grid.Npcs.Count();
        }

        // Applies the recommended face to every filtered NPC (face plugin only). Returns the number changed.
        public int AutoAssignFilteredFaces()
        {
            var applied = 0;
            foreach (var npc in Grid.Npcs.ToList())
                if (npc.CanCustomizeFace && npc.AutoAssignFaceFromPolicy() == NpcChangeResult.OK)
                    applied++;
            RefreshAfterBatch();
            return applied;
        }

        // How many filtered NPCs auto-assign would change (does not mutate).
        public int CountFilteredFacesToAutoAssign()
        {
            return Grid.Npcs.Count(npc =>
                npc.CanCustomizeFace && !npc.IsFacePlugin(npc.GetPolicyRecommendation().FacePluginName));
        }

        public bool SelectNpc(IRecordKey key)
        {
            if (npcs.TryGetValue(key, out var npc))
            {
                Grid.SelectedNpc = npc;
                return true;
            }
            return false;
        }

        private void ApplyFilters()
        {
            var filteredNpcs = npcs.Values.AsEnumerable()
                // We'll add the "always visible" back at the end.
                .Where(x => !alwaysVisibleNpcKeys.Contains(x));
            var minOverrideCount = !Filters.MultipleChoices ? 1 : 2;
            ApplySearchParameter(ref filteredNpcs, x => x.BasePluginName);
            ApplySearchParameter(ref filteredNpcs, x => x.LocalFormIdHex);
            ApplySearchParameter(ref filteredNpcs, x => x.EditorId);
            ApplySearchParameter(ref filteredNpcs, x => x.Name);
            if (Filters.Wigs)
                filteredNpcs = filteredNpcs.Where(x => x.FaceOption.HasWig);
            if (!string.IsNullOrEmpty(Filters.AvailablePlugin))
                filteredNpcs = filteredNpcs.Where(x => x.HasPluginOption(Filters.AvailablePlugin));
            if (!string.IsNullOrEmpty(Filters.SelectedDefaultPlugin))
                filteredNpcs = filteredNpcs.Where(x => x.IsDefaultPlugin(Filters.SelectedDefaultPlugin));
            if (!string.IsNullOrEmpty(Filters.SelectedFacePlugin))
                filteredNpcs = filteredNpcs.Where(x => x.IsFacePlugin(Filters.SelectedFacePlugin));
            if (Filters.Conflicts)
                // Not really a "conflict" anymore, but we'll repurpose the filter.
                filteredNpcs = filteredNpcs.Where(x => x.FaceGenOverride is not null);
            if (Filters.Missing)
                // TODO: Also check for invalid facegen override plugin?
                filteredNpcs = filteredNpcs.Where(x => x.HasMissingPlugins);
            filteredNpcs = filteredNpcs
                .Where(x => x.GetOverrideCount(!Filters.NonDlc) >= minOverrideCount || x.HasAvailableModdedFaceGens);
            filteredNpcs = filteredNpcs
                // This is only the default ordering; grid ordering is independent.
                .OrderBy(x => pluginOrder.GetOrDefault(x.BasePluginName))
                .ThenBy(x => uint.TryParse(x.LocalFormIdHex, NumberStyles.HexNumber, null, out var formId) ?
                    formId : uint.MaxValue);
            // Permanent filter - never show NPCs whose overrides all inherit traits from the same template, as there
            // are no meaningful visual customization choices to be made.
            filteredNpcs = filteredNpcs.Where(x => !x.HasUnmodifiedFaceTemplate);
            Grid.Npcs = alwaysVisibleNpcKeys
                .Select(x => npcs.GetOrDefault(x))
                .NotNull()
                .Concat(filteredNpcs);
        }

        private void ApplySearchParameter(
            ref IEnumerable<INpc> npcs, Func<INpcSearchParameters, string> propertySelector)
        {
            var filterText = propertySelector(Search);
            if (string.IsNullOrEmpty(filterText))
                return;
            npcs = npcs.Where(x =>
                propertySelector(x)?.Contains(filterText, StringComparison.CurrentCultureIgnoreCase) ?? false);
        }

        private IAsyncEnumerable<Mugshot> GetMugshots(INpc? npc)
        {
            if (npc is null)
                return AsyncEnumerable.Empty<Mugshot>();
            var affectingPlugins = npc.Options
                .Where(x => !string.Equals(x.PluginName, FileStructure.MergeFileName, StringComparison.CurrentCultureIgnoreCase))
                .Select(x => x.PluginName);
            return lineupBuilder.Build(npc, affectingPlugins);
        }

        private void HandleJumpToProfile(JumpToProfile message)
        {
            if (message.Filters != null)
            {
                Filters.ResetToDefault();
                if (message.Filters.Conflicts.HasValue)
                    Filters.Conflicts = message.Filters.Conflicts.Value;
                if (message.Filters.DefaultPlugin != null)  // Allow "empty" override
                    Filters.SelectedDefaultPlugin = message.Filters.DefaultPlugin;
                if (message.Filters.FacePlugin != null)
                    Filters.SelectedFacePlugin = message.Filters.FacePlugin;
                if (message.Filters.Missing.HasValue)
                    Filters.Missing = message.Filters.Missing.Value;
                if (message.Filters.NonDlc.HasValue)
                    Filters.NonDlc = message.Filters.NonDlc.Value;
                if (message.Filters.Wigs.HasValue)
                    Filters.Wigs = message.Filters.Wigs.Value;
                Filters.HasForcedFilter = true;
                ApplyFilters();
            }

            messageBus.Send(new NavigateToPage(MainPage.Profile));
        }

        private void SelectedNpc_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not NpcViewModel vm)
                return;
            if (e.PropertyName == nameof(NpcViewModel.SelectedMugshot))
                SelectedMugshot = vm.SelectedMugshot;
            messageBus.Send(new NpcConfigurationChanged(new RecordKey(vm)));
        }

        private void UpdateSelectedNpc(INpc? npc)
        {
            if (npc is null)
            {
                if (SelectedNpc is not null)
                    SelectedNpc.PropertyChanged -= SelectedNpc_PropertyChanged;
                SelectedNpc = null;
                SelectedMugshot = null;
                return;
            }
            var mugshots = GetMugshots(npc);
            SelectedNpc = new NpcViewModel(npc, mugshots);
            SelectedMugshot = SelectedNpc.SelectedMugshot;
            SelectedNpc.PropertyChanged += SelectedNpc_PropertyChanged;
            if (onlineMugshotsEnabled)
                _ = FillMugshotsFromFaceFinderAsync(npc, SelectedNpc);
        }

        // Fills installed face cards that have no local mugshot with an image from NPC Face Finder, asynchronously so it
        // never blocks the gallery. Only touches cards for mods that are actually installed, matched by Nexus mod id
        // first (survives a differently-named mod folder) and mod name as a fallback. Online failures are ignored.
        private async Task FillMugshotsFromFaceFinderAsync(INpc npc, NpcViewModel viewModel)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
            IReadOnlyList<FaceFinder.FaceFinderFace> faces;
            try
            {
                faces = await faceFinder.GetFacesAsync(npc, cts.Token);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "FaceFinder fill: lookup failed for {Npc}", npc.EditorId);
                return;
            }
            if (!ReferenceEquals(Grid.SelectedNpc, npc))
                return;
            if (faces.Count == 0)
            {
                log.Information("FaceFinder fill: 0 faces for {Npc}", npc.EditorId);
                return;
            }
            // The gallery is populated asynchronously, so it can still be empty right after the NPC is selected. Wait a
            // moment for the (fast, local) cards to appear before trying to place images onto them.
            var targets = viewModel.Mugshots.Where(m => m.IsPlaceholder && m.IsModInstalled).ToList();
            for (var wait = 0; targets.Count == 0 && viewModel.Mugshots.Count == 0 && wait < 20; wait++)
            {
                await Task.Delay(100);
                if (!ReferenceEquals(Grid.SelectedNpc, npc))
                    return;
                targets = viewModel.Mugshots.Where(m => m.IsPlaceholder && m.IsModInstalled).ToList();
            }
            var unmatched = new List<string>();
            var jobs = new List<Task<(MugshotViewModel Card, System.Windows.Media.ImageSource? Image)>>();
            foreach (var card in targets)
            {
                if (card.RenderedImage is not null)
                    continue;
                FaceFinder.FaceFinderFace? match;
                if (card.IsBaseGame)
                {
                    // The Vanilla card only ever gets the base-game face (FaceFinder's "Skyrim Special Edition"),
                    // never an overhaul. This is what stopped a mod like "Courageous Women of Skyrim" landing here.
                    match = faces.FirstOrDefault(f => f.IsBaseGame);
                }
                else
                {
                    match =
                        // Nexus mod id (survives a renamed folder), then exact mod name.
                        faces.FirstOrDefault(f => !f.IsBaseGame &&
                            ((!string.IsNullOrEmpty(card.ModNexusId) && !string.IsNullOrEmpty(f.NexusModId) &&
                                string.Equals(card.ModNexusId, f.NexusModId, StringComparison.OrdinalIgnoreCase)) ||
                            string.Equals(card.ModName, f.ModName, StringComparison.CurrentCultureIgnoreCase)))
                        // Fall back to the plugin name, which survives "compilation" installs where the mod folder's
                        // name and id belong to the compilation, not the individual mod (e.g. TSOSRefined.esp -> "True
                        // Sons of Skyrim Refined"). Low risk: every online face is the same NPC, just a different mod.
                        ?? faces.FirstOrDefault(f => !f.IsBaseGame &&
                            card.InstalledPlugins.Any(p => PluginMatchesMod(p, f.ModName)));
                    // A card whose plugins don't change the face (USSEP, AI Overhaul, a merge that kept the vanilla
                    // look) has no mugshot of its own online - but the NPC looks vanilla through it, so show that.
                    if (match is null && card.LeavesFaceVanilla)
                        match = faces.FirstOrDefault(f => f.IsBaseGame);
                }
                if (match is null)
                {
                    unmatched.Add(card.ModName);
                    continue;
                }
                // Download and decode every matched card at once instead of one-at-a-time (that was ~1s per card).
                jobs.Add(DownloadAndDecodeFaceAsync(npc, card, match, cts.Token));
            }
            var results = await Task.WhenAll(jobs);
            // Selection moved on while we were fetching: don't paint stale faces onto the new NPC's cards.
            if (!ReferenceEquals(Grid.SelectedNpc, npc))
                return;
            var filled = 0;
            foreach (var (card, image) in results)
            {
                if (image is null)
                    continue;
                card.RenderedImage = image;
                filled++;
            }
            log.Information(
                "FaceFinder fill: {Faces} faces, {Targets} installed cards, {Filled} filled for {Npc}. " +
                "Face mods: [{FaceMods}]. Unmatched cards: [{Unmatched}]",
                faces.Count, targets.Count, filled, npc.EditorId,
                string.Join(", ", faces.Select(f => f.ModName).Distinct()),
                string.Join(", ", unmatched));
        }

        // Fetches (from cache or network) and decodes one matched face off the UI thread. Never throws: a failure just
        // yields a null image so the card keeps its silhouette.
        private async Task<(MugshotViewModel Card, System.Windows.Media.ImageSource? Image)> DownloadAndDecodeFaceAsync(
            INpc npc, MugshotViewModel card, FaceFinder.FaceFinderFace match, System.Threading.CancellationToken token)
        {
            try
            {
                var path = await faceFinder.EnsureCachedImageAsync(npc, match, token);
                if (path is null)
                    return (card, null);
                return (card, await Task.Run(() => LoadFrozenImage(path)));
            }
            catch
            {
                return (card, null);
            }
        }

        // Matches a plugin file name against a mod name by normalized letters plus an initials/acronym check, so
        // "TSOSRefined.esp" matches "True Sons of Skyrim Refined". Deliberately loose - it only ever runs against the
        // online faces of one specific NPC.
        // Base-game masters are shared by every NPC and their names ("Skyrim", "Dawnguard"...) appear inside many mod
        // titles, so matching on them would wrongly tag any "... of Skyrim" overhaul. Never match on them.
        private static readonly HashSet<string> BaseGamePlugins = new(StringComparer.OrdinalIgnoreCase)
        {
            "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm",
        };

        private static bool PluginMatchesMod(string pluginName, string modName)
        {
            if (BaseGamePlugins.Contains(pluginName))
                return false;
            var plugin = LettersOnly(System.IO.Path.GetFileNameWithoutExtension(pluginName));
            var mod = LettersOnly(modName);
            if (plugin.Length < 4 || mod.Length < 4)
                return false;
            if (mod.Contains(plugin) || plugin.Contains(mod))
                return true;
            var acronym = new string(modName
                .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetter(w[0]))
                .Select(w => char.ToLowerInvariant(w[0]))
                .ToArray());
            return acronym.Length >= 3 && plugin.StartsWith(acronym);
        }

        private static string LettersOnly(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        }

        private static System.Windows.Media.ImageSource? LoadFrozenImage(string path)
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void RefreshAfterBatch()
        {
            // Re-run the filters so any NPCs that no longer match drop out, and rebuild the selected NPC's mugshots so
            // the newly-applied selection shows as active.
            ApplyFilters();
            UpdateSelectedNpc(Grid.SelectedNpc);
        }
    }
}
