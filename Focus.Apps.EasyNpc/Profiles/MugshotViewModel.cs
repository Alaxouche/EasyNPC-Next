using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Focus.Apps.EasyNpc.Profiles
{
    [AddINotifyPropertyChangedInterface]
    public class MugshotViewModel
    {
        public IReadOnlyList<string> InstalledPlugins => mugshot.InstalledPlugins;
        public bool IsBaseGame => mugshot.InstalledMod == Placeholders.BaseGameMod;
        public bool IsDisabledByErrors { get; private init; }
        public bool IsFocused { get; set; }
        public bool IsHighlighted { get; set; }
        public bool IsModDisabled => IsModInstalled && !mugshot.InstalledComponents.Any(x => x.IsEnabled);
        public bool IsModInstalled => mugshot.InstalledMod is not null;
        public bool IsPlaceholder => mugshot.IsPlaceholder;
        public bool IsPluginLoaded => mugshot.InstalledPlugins.Count > 0;
        // True when this card's plugins are all non-appearance edits (they don't change the face vs the base game),
        // e.g. USSEP or an AI mod. Such NPCs look vanilla through this card, so the Vanilla mugshot represents them.
        public bool LeavesFaceVanilla { get; private init; }
        public bool IsSelectedSource { get; set; }
        public string ModName => mugshot.ModName;
        // Nexus mod id of the installed mod this card represents, when known (used to match online mugshots).
        public string? ModNexusId => mugshot.InstalledMod?.Id;
        public bool HasNexusLink => !string.IsNullOrEmpty(ModNexusId);
        // Nexus page for this mod, when we know its id. Skyrim SE is the only game this tool targets.
        public string? NexusUrl =>
            HasNexusLink ? $"https://www.nexusmods.com/skyrimspecialedition/mods/{ModNexusId}" : null;
        public string Path => mugshot.Path;
        // Live-rendered face shown instead of the silhouette when there's no packaged mugshot.
        public System.Windows.Media.ImageSource? RenderedImage { get; set; }
        // Rendered face if present, else the file path (WPF converts the string to an image).
        [DependsOn(nameof(RenderedImage))]
        public object DisplaySource => RenderedImage ?? (object)Path;

        private readonly Mugshot mugshot;

        public MugshotViewModel(Mugshot mugshot, IEnumerable<NpcOption> options, bool isSelectedSource = false)
        {
            this.mugshot = mugshot;
            IsSelectedSource = isSelectedSource;

            var applicableOptions = options
                .Where(x => mugshot.InstalledPlugins.Contains(x.PluginName, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
            if (applicableOptions.Count > 0 && applicableOptions.All(x => x.HasErrors))
                IsDisabledByErrors = true;
            // Only claim "vanilla face" when we actually have the plugin(s) analyzed and none of them touch the face.
            LeavesFaceVanilla = applicableOptions.Count > 0
                && applicableOptions.All(x => x.Analysis.ComparisonToBase?.ModifiesFace != true);
        }
    }
}
