using Focus.Environment;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Providers.Mutagen
{
    public class GameSetup : IGameSetup
    {
        public IReadOnlyList<PluginInfo> AvailablePlugins { get; private set; } = new List<PluginInfo>().AsReadOnly();
        public string DataDirectory => game.DataDirectory;
        public bool IsConfirmed { get; private set; }
        public ILoadOrderGraph LoadOrderGraph { get; private set; } = new NullLoadOrderGraph();

        private readonly IFileSystem fs;
        private readonly GameInstance game;
        private readonly ILogger log;
        private readonly ISetupStatics setupStatics;

        public GameSetup(IFileSystem fs, ISetupStatics setupStatics, GameInstance game, ILogger log)
        {
            this.fs = fs;
            this.game = game;
            this.log = log;
            this.setupStatics = setupStatics;
        }

        public void Confirm()
        {
            IsConfirmed = true;
        }

        public void Detect(IReadOnlySet<string> blacklistedPluginNames)
        {
            log.Information("Using game data directory: {dataDirectory}", DataDirectory);
            var implicits = setupStatics.GetBaseMasters(game.GameRelease)
                .Select(x => x.FileName.String)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var listings = setupStatics.GetLoadOrderListings(game.GameRelease, DataDirectory, true).ToList();
            var masterTasks = listings.Select(x => TryGetMasterNames(DataDirectory, x.ModKey.FileName)).ToArray();
            var masterResults = Task.WhenAll(masterTasks).Result;
            AvailablePlugins = listings.Zip(masterResults)
                .Select((x, i) =>
                {
                    return new PluginInfo
                    {
                        FileName = x.First.ModKey.FileName.String,
                        IsEnabled = x.First.Enabled,
                        IsImplicit = implicits.Contains(x.First.ModKey.FileName.String),
                        IsReadable = x.Second.Item1,
                        Masters = x.Second.Item2.ToList().AsReadOnly(),
                    };
                })
                .Where(x => x is not null)
                .ToList()
                .AsReadOnly();
            LoadOrderGraph = new LoadOrderGraph(AvailablePlugins, blacklistedPluginNames);
            LogLoadOrderSummary();
        }

        private void LogLoadOrderSummary()
        {
            var sources = setupStatics.GetLoadOrderSources(game.GameRelease, DataDirectory);
            var enabled = AvailablePlugins.Where(x => x.IsEnabled).ToList();
            var enabledMods = enabled.Count(x => !x.IsImplicit);
            log.Information(
                "Load order read from plugins file {pluginsFile} (exists: {pluginsFileExists}) and Creation Club " +
                "list {cccFile}: {pluginCount} plugins, {enabledCount} enabled ({modCount} beyond the base game)",
                sources?.PluginsFilePath ?? "<none>",
                sources?.PluginsFilePath is string p && fs.File.Exists(p),
                sources?.CreationClubFilePath ?? "<none>",
                AvailablePlugins.Count, enabled.Count, enabledMods);
            if (enabledMods == 0)
                log.Warning(
                    "No plugins beyond the base game are enabled, so no NPC overhaul will be visible. The plugins file " +
                    "above was probably the wrong one: start EasyNPC from your mod manager (so it sees the profile's " +
                    "load order), or on Linux/Proton make sure it runs in the same prefix as the game.");
        }

        private async Task<(bool, IEnumerable<string>)> TryGetMasterNames(string dataDirectory, string pluginFileName)
        {
            var path = fs.Path.Combine(dataDirectory, pluginFileName);
            try
            {
                var data = await fs.File.ReadAllBytesAsync(path).ConfigureAwait(false);
                using var stream = new MemoryStream(data);
                var modKey = ModKey.FromNameAndExtension(pluginFileName);
                using var mod = SkyrimMod.CreateFromBinaryOverlay(
                    stream, game.GameRelease.ToSkyrimRelease(), modKey);
                var masterNames = mod.ModHeader.MasterReferences.Select(x => x.Master.FileName.String);
                return (true, masterNames);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Plugin {pluginName} appears to be corrupt and cannot be loaded.", pluginFileName);
                return (false, Enumerable.Empty<string>());
            }
        }
    }
}
