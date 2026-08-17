using Focus.Apps.EasyNpc.GameData.Files;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class PatchSaveTask : BuildTask<PatchSaveTask.Result>
    {
        public class Result
        {
            public ISkyrimModGetter Mod { get; private init; }
            public string Path { get; private init; }

            public Result(ISkyrimModGetter mod, string path)
            {
                Mod = mod;
                Path = path;
            }
        }

        public delegate PatchSaveTask Factory(
            PatchInitializationTask.Result patch, NpcFacesTask.Result faces, DewiggifyRecordsTask.Result wigs);

        private readonly IFileSystem fs;
        private readonly IGameSettings gameSettings;
        private readonly ILogger log;
        private readonly PatchInitializationTask.Result patch;

        public PatchSaveTask(
            IFileSystem fs, IGameSettings gameSettings, ILogger log, PatchInitializationTask.Result patch,
            NpcFacesTask.Result faces, DewiggifyRecordsTask.Result wigs)
        {
            RunsAfter(faces, wigs);
            this.fs = fs;
            this.gameSettings = gameSettings;
            this.log = log;
            this.patch = patch;
        }

        protected override Task<Result> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                fs.Directory.CreateDirectory(settings.OutputDirectory);
                var outputPath = fs.Path.Combine(settings.OutputDirectory, patch.Mod.ModKey.FileName);
                BackupPreviousMerge(outputPath);
                // Splitting packs the NPCs into several self-contained plugins to dodge "Too Many Masters". BSA builds
                // are not supported yet (the BSA is loaded by the single merge plugin name), so only split loose builds.
                if (settings.MaxNpcsPerPlugin > 0 && !settings.EnableArchiving &&
                    patch.Mod.Npcs.Count > settings.MaxNpcsPerPlugin)
                    SaveSplit(settings);
                else
                    SaveMod(patch.Mod, outputPath);
                return new Result(patch.Mod, outputPath);
            });
        }

        // Write the NPCs across several plugins, each carrying its own copy of the records it references so no plugin
        // depends on another. The first split keeps the standard merge file name; the rest get a numbered suffix. The
        // full merge (patch.Mod) is left as-is in memory, so the resource/texture/archive tasks still see everything.
        private void SaveSplit(BuildSettings settings)
        {
            var npcs = patch.Mod.Npcs.ToList();
            var partitionCount = (int)Math.Ceiling((double)npcs.Count / settings.MaxNpcsPerPlugin);
            var baseName = fs.Path.GetFileNameWithoutExtension(FileStructure.MergeFileName);
            var mergeCache = patch.Mod.ToImmutableLinkCache();
            log.Information(
                "Splitting {npcCount} NPCs into {plugins} plugins ({max} NPCs each).",
                npcs.Count, partitionCount, settings.MaxNpcsPerPlugin);
            for (var i = 0; i < partitionCount; i++)
            {
                var fileName = i == 0 ? FileStructure.MergeFileName : $"{baseName} {i + 1}.esp";
                var split = new SkyrimMod(ModKey.FromNameAndExtension(fileName), patch.Mod.SkyrimRelease);
                foreach (var npc in npcs.Skip(i * settings.MaxNpcsPerPlugin).Take(settings.MaxNpcsPerPlugin))
                    split.Npcs.GetOrAddAsOverride(npc);
                // Pull every record the partition references out of the full merge into this split, so it stands alone.
                split.DuplicateFromOnlyReferenced(mergeCache, patch.Mod.ModKey, out _);
                var splitPath = fs.Path.Combine(settings.OutputDirectory, fileName);
                BackupPreviousMerge(splitPath);
                SaveMod(split, splitPath);
            }
        }

        private void BackupPreviousMerge(string mergeFilePath)
        {
            if (fs.File.Exists(mergeFilePath))
            {
                var backupPath = $"{mergeFilePath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                fs.File.Move(mergeFilePath, backupPath, true);
            }
        }

        private void SaveMod(SkyrimMod mod, string outputPath)
        {
            var loadOrder = gameSettings.PluginLoadOrder.Select(x => ModKey.FromNameAndExtension(x)).ToList();
            // Mutagen's write builder replaces the old BinaryWriteParameters/MastersListOrdering API; ordering the
            // masters by the game's load order is expressed by handing it that load order directly.
            mod.BeginWrite
                .ToPath(outputPath)
                .WithLoadOrder(loadOrder)
                .WithDataFolder(gameSettings.DataDirectory)
                .Write();
        }
    }
}
