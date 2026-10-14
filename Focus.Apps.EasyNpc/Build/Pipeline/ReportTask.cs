using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Files;
using Focus.Providers.Mutagen;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class ReportTask : BuildTask<BuildReport>
    {
        public delegate ReportTask Factory(
            PatchInitializationTask.Result patchInit, PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces,
            TexturePathExtractionTask.Result textureExtraction);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly NpcFacesTask.Result faces;
        private readonly IFileProvider fileProvider;
        private readonly ILogger log;
        // Head parts are shared between NPCs, so the same .tri is asked about thousands of times per build.
        private readonly ConcurrentDictionary<string, int?> morphVertexCounts = new(PathComparer.Default);
        private readonly WriteMetadataTask.Result metadata;
        private readonly PatchInitializationTask.Result patchInit;
        private readonly IBuildReporter reporter;
        private readonly TexturePathExtractionTask.Result textureExtraction;

        public ReportTask(
            IBuildReporter reporter, IFileProvider fileProvider, ILogger log, PatchInitializationTask.Result patchInit,
            PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces,
            TexturePathExtractionTask.Result textureExtraction)
        {
            RunsAfter(patch, archive, metadata);
            this.defaults = defaults;
            this.faces = faces;
            this.fileProvider = fileProvider;
            this.log = log;
            this.metadata = metadata;
            this.patchInit = patchInit;
            this.reporter = reporter;
            this.textureExtraction = textureExtraction;
        }

        protected override Task<BuildReport> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                // Map each broken facegen mesh path back to the NPC it belongs to, so the report names the NPC (and its
                // face plugin) instead of a cryptic file path.
                var npcByFaceMesh = settings.IncludedNpcs
                    .GroupBy(n => FileStructure.GetFaceMeshFileName(n), PathComparer.Default)
                    .ToDictionary(g => g.Key, g => g.First(), PathComparer.Default);
                var brokenFaceGenNpcs = textureExtraction.BrokenFaceGenPaths
                    .Select(path => npcByFaceMesh.TryGetValue(path, out var npc)
                        ? new SkippedNpc(
                            npc, $"{npc.EditorId} '{npc.Name}'", npc.FaceOption.PluginName,
                            "empty or corrupt FaceGen mesh (invisible face in game)")
                        : new SkippedNpc(path, string.Empty, "empty or corrupt FaceGen mesh (invisible face in game)"))
                    .OrderBy(x => x.Label, System.StringComparer.CurrentCultureIgnoreCase)
                    .ToList().AsReadOnly();
                var faceGenMismatchNpcs = FindFaceGenMismatches(npcByFaceMesh);
                var report = new BuildReport
                {
                    ModName = settings.OutputModName,
                    MergedNpcCount = defaults.Npcs.Count - faces.Skipped.Count,
                    SkippedNpcs = defaults.Skipped.Concat(faces.Skipped).ToList().AsReadOnly(),
                    BrokenFaceGenNpcs = brokenFaceGenNpcs,
                    FaceGenMismatchNpcs = faceGenMismatchNpcs,
                    MissingAssetGroups = metadata.MissingAssetGroups,
                };
                reporter.Save(report);
                return report;
            });
        }

        // The game builds a merged NPC's head by looking up each head part of the record, by editor ID, among the
        // shapes of the FaceGen it resolves, and then driving that shape's vertices with the head part's morph data
        // (its .tri). So the record and the FaceGen the merge just wrote have to describe the same head, and three
        // things can go wrong, in rising order of how much they hurt:
        //  - a head part with no shape of its name simply doesn't render (a bald NPC, or a floating mouth);
        //  - a shape with no head part behind it leaves the game to fill that slot from the race default, and a default
        //    sharing the shape's editor ID - which overhauls routinely arrange, keeping the vanilla names - is then
        //    driven by the wrong morph table;
        //  - a head part whose .tri has a different vertex count from the shape it drives is that same fault, proven:
        //    the game walks the shape's vertices against a shorter morph table, scattering the mesh across the scene
        //    and reading past the end of the table, which crashes during facial animation.
        // All three would be bugs in the merge rather than in any source mod, so verify the output instead of trusting
        // it. Extra parts count as head parts here: a head part can carry sub-parts (hair highlights, eyelashes, the
        // pieces of a braided beard) which the FaceGen bakes as shapes of their own although the NPC's record never
        // names them.
        private IReadOnlyList<SkippedNpc> FindFaceGenMismatches(
            IReadOnlyDictionary<string, Profiles.INpc> npcByFaceMesh)
        {
            if (textureExtraction.FaceGenShapes.Count == 0)
                return Array.Empty<SkippedNpc>();
            var mergedNpcs = patchInit.Mod.Npcs.ToDictionary(x => x.FormKey);
            var mismatches = new List<SkippedNpc>();
            foreach (var (meshPath, shapes) in textureExtraction.FaceGenShapes)
            {
                CancellationToken.ThrowIfCancellationRequested();
                // Nothing readable in the file; already reported as a broken FaceGen.
                if (shapes.Count == 0)
                    continue;
                if (!npcByFaceMesh.TryGetValue(meshPath, out var npc))
                    continue;
                var label = $"{npc.EditorId} '{npc.Name}'";
                if (!mergedNpcs.TryGetValue(npc.ToFormKey(), out var record))
                {
                    mismatches.Add(new SkippedNpc(
                        npc, label, npc.FaceOption.PluginName,
                        "The merge ships a FaceGen for this NPC but no record to go with it, so the game pairs the " +
                        "face with whatever record the load order provides."));
                    continue;
                }
                var headPartsById = new Dictionary<string, IHeadPartGetter>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var headPart in ExpandHeadParts(record.HeadParts.Select(x => x.FormKey)))
                    if (!string.IsNullOrEmpty(headPart.EditorID))
                        headPartsById[headPart.EditorID] = headPart;
                var shapeNames = shapes.Select(x => x.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                var partsWithoutShape = headPartsById
                    // "No beard" / "no scar" placeholder parts carry no model and are never baked into a FaceGen, so
                    // having no shape is the normal state for them. They can still BACK a shape, which is why they
                    // stay in the lookup above.
                    .Where(x => !string.IsNullOrEmpty(x.Value.Model?.File.PathOrDefault()))
                    .Select(x => x.Key)
                    .Where(x => !shapeNames.Contains(x))
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var shapesWithoutPart = new List<string>();
                var wrongVertexCounts = new List<string>();
                foreach (var shape in shapes)
                {
                    if (!headPartsById.TryGetValue(shape.Name, out var headPart))
                    {
                        shapesWithoutPart.Add(shape.Name);
                        continue;
                    }
                    var morphVertexCount = GetMorphVertexCount(headPart);
                    // No morph data means the game never touches the shape's vertices, so nothing can go wrong.
                    if (morphVertexCount is null || morphVertexCount == shape.VertexCount)
                        continue;
                    wrongVertexCounts.Add(
                        $"{shape.Name} ({shape.VertexCount} vertices in the FaceGen, {morphVertexCount} in the " +
                        $"morph data of the head part)");
                }
                if (partsWithoutShape.Count == 0 && shapesWithoutPart.Count == 0 && wrongVertexCounts.Count == 0)
                    continue;
                var details = new List<string>();
                if (wrongVertexCounts.Count > 0)
                    details.Add(
                        $"the head part driving {string.Join(", ", wrongVertexCounts)} was baked from different " +
                        $"geometry, which crashes the game during facial animation");
                if (shapesWithoutPart.Count > 0)
                    details.Add(
                        $"the FaceGen contains {string.Join(", ", shapesWithoutPart.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))} " +
                        $"but the record has no head part of that name");
                if (partsWithoutShape.Count > 0)
                    details.Add(
                        $"the record asks for {string.Join(", ", partsWithoutShape)} but the FaceGen has no shape " +
                        $"of that name");
                mismatches.Add(new SkippedNpc(
                    npc, label, npc.FaceOption.PluginName,
                    $"The record and the FaceGen disagree about this NPC's head: {string.Join("; ", details)}."));
            }
            if (mismatches.Count > 0)
                log.Warning(
                    "{Count} merged NPC(s) have a FaceGen that does not match the head parts written for them. This " +
                    "is an EasyNPC bug, not a problem with the source mods - please report it. Details: {Details}",
                    mismatches.Count,
                    string.Join(" | ", mismatches.Select(x => $"{x.Label}: {x.Reason}")));
            return mismatches
                .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        // Every head part the game will look for in the FaceGen: the ones on the record, plus the sub-parts they pull
        // in. Extra parts nest, and a malformed mod can make them cycle, so visited keys are tracked.
        private IEnumerable<IHeadPartGetter> ExpandHeadParts(IEnumerable<FormKey> keys)
        {
            var seen = new HashSet<FormKey>();
            var pending = new Queue<FormKey>(keys);
            while (pending.Count > 0)
            {
                var key = pending.Dequeue();
                if (!seen.Add(key))
                    continue;
                var headPart = patchInit.Importer.TryResolve<IHeadPartGetter>(key);
                if (headPart is null)
                    continue;
                foreach (var extraPart in headPart.ExtraParts)
                    pending.Enqueue(extraPart.FormKey);
                yield return headPart;
            }
        }

        // The vertex count the head part's morph data is built for, or null when it ships none. Every .tri of a head
        // part describes the same mesh, so the first one that can be read answers for all of them.
        private int? GetMorphVertexCount(IHeadPartGetter headPart)
        {
            foreach (var part in headPart.Parts)
            {
                var path = part.FileName.PathOrDefault();
                if (string.IsNullOrEmpty(path))
                    continue;
                var count = morphVertexCounts.GetOrAdd(path.PrefixPath("meshes"), ReadMorphVertexCount);
                if (count is not null)
                    return count;
            }
            return null;
        }

        // A Skyrim .tri starts with the tag "FRTRI003" followed by its vertex count, so the first twelve bytes answer
        // the question. Anything else - a missing file, another format, an unreadable archive - is no answer at all
        // rather than a mismatch to report.
        private int? ReadMorphVertexCount(string path)
        {
            try
            {
                if (!fileProvider.Exists(path))
                    return null;
                var bytes = fileProvider.ReadBytes(path);
                if (bytes.Length < 12 ||
                    !System.Text.Encoding.ASCII.GetString(bytes[..8]).StartsWith("FRTRI", StringComparison.Ordinal))
                    return null;
                return BitConverter.ToInt32(bytes[8..12]);
            }
            catch (Exception ex)
            {
                log.Debug(ex, "Couldn't read the morph file {MorphPath} to check its vertex count.", path);
                return null;
            }
        }
    }
}
