using Focus.Analysis.Execution;
using Focus.Analysis.Records;
using Focus.Apps.EasyNpc.Profiles;
using Focus.Files;
using Focus.ModManagers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build.Checks
{
    // Many overhauls keep their hair, brows and beards in a separate resource mod. When that mod is missing or disabled,
    // the merge still writes the head parts but can't copy their meshes, and the NPC comes out bald or with broken hair.
    // The build only reported this afterwards, as raw paths in build_info.json; this names the NPC before building.
    public class MissingHeadPartMeshes : INpcBuildCheck, ILoadOrderAnalysisReceiver
    {
        private readonly IArchiveProvider archiveProvider;
        private readonly Lazy<IReadOnlyList<string>> baseGameArchives;
        private readonly IFileSystem fs;
        private readonly Lazy<IGameSettings> gameSettings;
        private readonly ConcurrentDictionary<string, bool> meshExists = new(PathComparer.Default);
        private readonly IModRepository modRepository;

        private IReadOnlyDictionary<IRecordKey, HeadPartAnalysis> headParts =
            new Dictionary<IRecordKey, HeadPartAnalysis>();

        public MissingHeadPartMeshes(
            IFileSystem fs, Lazy<IGameSettings> gameSettings, IModRepository modRepository,
            IArchiveProvider archiveProvider)
        {
            this.archiveProvider = archiveProvider;
            this.fs = fs;
            this.gameSettings = gameSettings;
            this.modRepository = modRepository;
            baseGameArchives = new(() => gameSettings.Value.ArchiveOrder
                .Where(f => gameSettings.Value.IsBaseGameArchive(f))
                .Select(f => fs.Path.Combine(gameSettings.Value.DataDirectory, f))
                .ToList());
        }

        public void Receive(LoadOrderAnalysis analysis)
        {
            headParts = analysis.ExtractChains<HeadPartAnalysis>(RecordType.HeadPart)
                .ToDictionary(x => (IRecordKey)x.Key, x => x.Winner, RecordKeyComparer.Default);
            meshExists.Clear();
        }

        public IEnumerable<BuildWarning> Run(INpc npc, BuildSettings settings)
        {
            if (npc.FaceOption.IsBaseGame || headParts.Count == 0)
                yield break;
            // Template-based faces take their head parts from the template, which is what the merge writes.
            var faceSource = settings.Profile.TryResolveTemplate(npc, out var template) ? template : npc;
            var missing = GetHeadParts(faceSource.FaceOption.Analysis.MainHeadParts)
                .Select(x => x.ModelFileName)
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x!.PrefixPath("meshes"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => !MeshExists(x))
                .ToList();
            if (missing.Count > 0)
                yield return new BuildWarning(
                    npc.FaceOption.PluginName,
                    new RecordKey(npc),
                    BuildWarningId.MissingHeadPartMesh,
                    WarningMessages.MissingHeadPartMesh(npc.EditorId, npc.Name, npc.FaceOption.PluginName, missing));
        }

        private IEnumerable<HeadPartAnalysis> GetHeadParts(IEnumerable<IRecordKey> keys)
        {
            var visited = new HashSet<IRecordKey>(RecordKeyComparer.Default);
            var pending = new Stack<IRecordKey>(keys);
            while (pending.Count > 0)
            {
                var key = pending.Pop();
                if (!visited.Add(key) || !headParts.TryGetValue(key, out var headPart))
                    continue;
                yield return headPart;
                foreach (var extraKey in headPart.ExtraPartKeys)
                    pending.Push(extraKey);
            }
        }

        // Loose files (the data folder already shows every mod's files when started from Mod Organizer), then mod
        // archives through the mod index built at startup, then the base game's own archives.
        private bool MeshExists(string relativePath)
        {
            return meshExists.GetOrAdd(relativePath, path =>
                fs.File.Exists(fs.Path.Combine(gameSettings.Value.DataDirectory, path)) ||
                modRepository.ContainsFile(path, true) ||
                baseGameArchives.Value.Any(a => archiveProvider.ContainsFile(a, path)));
        }
    }
}
