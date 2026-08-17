using Focus.Apps.EasyNpc.Profiles;
using Focus.Apps.EasyNpc.Reports;
using Focus.Environment;
using Focus.ModManagers;
using PropertyChanged;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace Focus.Apps.EasyNpc.Build.Preview
{
    [AddINotifyPropertyChangedInterface]
    public class PluginViewModel
    {
        public static bool IsSuspiciousMasterCategory(PluginCategory category) => category switch
        {
            PluginCategory.NpcOverhaul or PluginCategory.NpcOverhaulPatch => true,
            _ => false,
        };

        public PluginCategory Category { get; private init; }
        public string CategoryDescription => Description.Of(Category);
        public ModComponentInfo? Component { get; private init; }
        // Whether at least one NPC actually uses this plugin as its Default Plugin. A plugin can appear in the master
        // list purely as an indirect dependency of some other default plugin, in which case no NPC points to it and
        // double-clicking it (which filters the NPC grid by Default Plugin) shows nothing.
        public bool IsDefaultPlugin { get; private init; }
        // Whether the user is using this plugin as a Face (appearance) source for at least one NPC. If so, it's a
        // deliberately-chosen overhaul, not something sneaking in as an unwanted master.
        public bool IsUsedAsFace { get; private init; }
        // A "suspicious master" is meant to warn about NPC overhauls that end up as required masters WITHOUT you
        // intending to use them. So we only flag an overhaul that (a) an NPC actually uses as its Default Plugin -
        // which is what the help text promises and what makes double-click show something - and (b) you are NOT
        // already using as a face source. An overhaul you picked as a face source (e.g. your main NPC overhaul) is
        // intentional and shouldn't be flagged just because a few NPCs also inherit their non-face records from it.
        [DependsOn(nameof(Category), nameof(IsDefaultPlugin), nameof(IsUsedAsFace))]
        public bool IsSuspiciousMaster =>
            IsSuspiciousMasterCategory(Category) && IsDefaultPlugin && !IsUsedAsFace;
        public string PluginName { get; private init; }

        public PluginViewModel(
            string pluginName, ModComponentInfo? component, PluginCategory category, bool isDefaultPlugin = false,
            bool isUsedAsFace = false)
        {
            PluginName = pluginName;
            Category = category;
            Component = component;
            IsDefaultPlugin = isDefaultPlugin;
            IsUsedAsFace = isUsedAsFace;
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class PluginsViewModel
    {
        public delegate PluginsViewModel Factory(Profile profile);

        public IReadOnlyDictionary<string, PluginViewModel> FacePlugins { get; private set; } =
            new Dictionary<string, PluginViewModel>();
        [DependsOn(nameof(SuspiciousMasters))]
        public bool HasSuspiciousMasters => SuspiciousMasters.Any();
        [DependsOn(nameof(MasterPlugins))]
        public int MasterCount => MasterPlugins.Count;
        public IReadOnlyDictionary<string, PluginViewModel> MasterPlugins { get; private set; } =
            new Dictionary<string, PluginViewModel>();
        [DependsOn(nameof(FacePlugins), nameof(MasterPlugins))]
        public int MergedCount => FacePlugins.Keys.Count(p => !MasterPlugins.ContainsKey(p));
        public PluginViewModel? SelectedPlugin { get; set; }
        [DependsOn(nameof(SuspiciousMasters))]
        public int SuspiciousMasterCount => SuspiciousMasters.Count();
        [DependsOn(nameof(MasterPlugins))]
        public IEnumerable<PluginViewModel> SuspiciousMasters => MasterPlugins.Values.Where(x => x.IsSuspiciousMaster);

        // Probably slow to regen entire list each time, only used as a test.
        [DependsOn(nameof(MasterCount), nameof(MergedCount), nameof(SuspiciousMasterCount))]
        public IEnumerable<SummaryItem> SummaryItems => new List<SummaryItem>
        {
            new(SummaryItemCategory.StatusInfo, "Required masters", MasterCount),
            new(
                SuspiciousMasterCount > 0 ? SummaryItemCategory.StatusWarning : SummaryItemCategory.StatusOk,
                "Suspicious masters", SuspiciousMasterCount),
            new(SummaryItemCategory.StatusInfo, "Merged overhauls", MergedCount),
        }.AsReadOnly();

        private readonly IModRepository modRepository;
        private readonly IPluginCategorizer pluginCategorizer;

        public PluginsViewModel(
            IReadOnlyLoadOrderGraph loadOrderGraph, IPluginCategorizer pluginCategorizer, IModRepository modRepository,
            ILogger log, Profile profile)
        {
            this.modRepository = modRepository;
            this.pluginCategorizer = pluginCategorizer;

            // Combine the default and face options together so that "suspicious master" detection can tell the
            // difference between an overhaul you deliberately use as a face source and one that's only a master.
            var defaultOptionsObservable =
                Observable.CombineLatest(profile.Npcs.Select(x => x.DefaultOptionObservable));
            var faceOptionsObservable =
                Observable.CombineLatest(profile.Npcs.Select(x => x.FaceOptionObservable));
            Observable
                .CombineLatest(
                    defaultOptionsObservable, faceOptionsObservable,
                    (defaultOptions, faceOptions) => (defaultOptions, faceOptions))
                .SubscribeOn(NewThreadScheduler.Default)
                .SubscribeSafe(log, x =>
                {
                    var facePlugins = x.faceOptions
                        .Select(o => o.PluginName)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                    var defaultPlugins = x.defaultOptions
                        .Select(o => o.PluginName)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                    var allMasters = defaultPlugins
                        .SelectMany(p => loadOrderGraph.GetAllMasters(p))
                        .Concat(defaultPlugins);
                    MasterPlugins = CreatePluginLookup(allMasters, defaultPlugins, facePlugins);
                    FacePlugins = CreatePluginLookup(x.faceOptions.Select(o => o.PluginName));
                });
        }

        private IReadOnlyDictionary<string, PluginViewModel> CreatePluginLookup(
            IEnumerable<string> pluginNames, IReadOnlySet<string>? defaultPlugins = null,
            IReadOnlySet<string>? facePlugins = null)
        {
            return pluginNames
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Select(name => DescribePlugin(
                    name, defaultPlugins?.Contains(name) ?? false, facePlugins?.Contains(name) ?? false))
                .ToDictionary(x => x.PluginName, StringComparer.CurrentCultureIgnoreCase);
        }

        private PluginViewModel DescribePlugin(string pluginName, bool isDefaultPlugin, bool isUsedAsFace)
        {
            var category = pluginCategorizer.GetCategory(pluginName);
            var providingComponent = modRepository.SearchForFiles(pluginName, false)
                .Select(x => x.ModComponent)
                .FirstOrDefault();
            return new(pluginName, providingComponent, category, isDefaultPlugin, isUsedAsFace);
        }
    }
}
