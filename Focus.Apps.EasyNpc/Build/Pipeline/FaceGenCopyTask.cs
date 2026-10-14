using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Files;
using Focus.ModManagers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class FaceGenCopyTask : BuildTask<FaceGenCopyTask.Result>
    {
        public class Result
        {
            public IReadOnlyCollection<string> FailedPaths { get; private init; }
            public IReadOnlyCollection<string> MeshPaths { get; private init; }
            public IReadOnlyCollection<string> TintPaths { get; private init; }
            // Mod each copied FaceGen came from, by output path. The output name can differ from the source's (a
            // template's FaceGen saved under the NPC's name), so the mod can't be looked up from the output path.
            public IReadOnlyDictionary<string, string> SourceModNames { get; private init; }

            public Result(
                IReadOnlyCollection<string> meshPaths, IReadOnlyCollection<string> tintPaths,
                IReadOnlyCollection<string> failedPaths, IReadOnlyDictionary<string, string>? sourceModNames = null)
            {
                MeshPaths = meshPaths;
                TintPaths = tintPaths;
                FailedPaths = failedPaths;
                SourceModNames = sourceModNames ?? new Dictionary<string, string>();
            }
        }

        // Takes the faces result so the copy can be restricted to NPCs the merge really wrote a record for. NPCs that
        // never make it into the plugin (an unchanged template-based NPC, a skipped one) must not get a FaceGen.
        public delegate FaceGenCopyTask Factory(NpcFacesTask.Result faces);

        // Scale copy concurrency with the CPU (bounded), matching FileCopier. FaceGen files are a mix of loose and
        // BSA-packed data, so extra threads help decompression-bound work on modern storage without thrashing.
        private static readonly int MaxDegreeOfParallelism = Math.Clamp(System.Environment.ProcessorCount / 2, 4, 8);

        private readonly IArchiveProvider archiveProvider;
        private readonly NpcFacesTask.Result faces;
        private readonly IFileSystem fs;
        private readonly IModRepository modRepository;

        public FaceGenCopyTask(
            IFileSystem fs, IModRepository modRepository, IArchiveProvider archiveProvider, NpcFacesTask.Result faces)
        {
            this.archiveProvider = archiveProvider;
            this.faces = faces;
            this.fs = fs;
            this.modRepository = modRepository;
        }

        protected override Task<Result> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                var meshPaths = new List<string>();
                var tintPaths = new List<string>();
                var failedPaths = new List<string>();
                var sourceModNames = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                    PathComparer.Default);
                var found = settings.IncludedNpcs
                    .AsParallel()
                    // Only NPCs the merge writes a record for, and whose record asks for a FaceGen of its own.
                    .Where(x => faces.FaceGenNpcs.Contains(x))
                    // When the chosen face inherits its traits, the whole appearance - head parts *and* FaceGen -
                    // belongs to the template, and the overhaul deliberately ships no FaceGen for this NPC (Ordinary
                    // People does exactly this for the generic forsworn, thalmor and bandits). NpcFacesTask writes the
                    // template's head parts onto this record, so the FaceGen has to be the template's as well, saved
                    // under this NPC's own name. Skipping these outright is what left them with an overhaul's head
                    // parts over the vanilla FaceGen - i.e. no face at all.
                    .Select(x => new
                    {
                        Npc = x,
                        Source = settings.Profile.TryResolveTemplate(x, out var template) ? template : x,
                    })
                    .Where(x =>
                        // A redirected NPC always needs the copy: nothing else in the load order provides a FaceGen
                        // under its own name.
                        x.Source != x.Npc ||
                        x.Npc.FaceGenOverride is not null ||
                        (!x.Npc.FaceOption.IsBaseGame &&
                            x.Npc.FaceOption.PluginName != x.Npc.DefaultOption.PluginName))
                    .SelectMany(x =>
                        new[]
                        {
                            (Source: FileStructure.GetFaceMeshFileName(x.Source),
                                Output: FileStructure.GetFaceMeshFileName(x.Npc)),
                            (Source: FileStructure.GetFaceTintFileName(x.Source),
                                Output: FileStructure.GetFaceTintFileName(x.Npc)),
                        }
                        .Select(p => new
                        {
                            Path = p.Source,
                            OutputRelativePath = p.Output,
                            // The head parts come from the source model's face plugin, so the FaceGen must too.
                            Components = (x.Source.FaceGenOverride is not null ?
                                x.Source.FaceGenOverride.Components :
                                ComponentsForPlugin(x.Source.FaceOption.PluginName))
                                .ToHashSet(),
                        }))
                    // Mod repository will give us loose first, then archives. If a mod has multiple enabled components
                    // that all have the same facegen as a loose file, then the choice of which one to use is arbitrary
                    // at this time. Is this important enough to track component priorities, with all of the mess that
                    // will create with Vortex (where priorities are actually at the file level)?
                    .Select(x => new
                    {
                        x.OutputRelativePath,
                        Result = modRepository
                            .SearchForFiles(x.Path, true)
                            .Where(r => x.Components.Contains(r.ModComponent))
                            .FirstOrDefault(),
                    })
                    .Where(x => x.Result is not null)
                    .Select(x => new { x.OutputRelativePath, Source = x.Result! })
                    .ToList();
                ItemCount.OnNext(found.Count);
                var outputDirectories = found
                    .Select(x => fs.Path.Combine(settings.OutputDirectory, x.OutputRelativePath))
                    .Select(p => fs.Path.GetDirectoryName(p))
                    .Distinct();
                foreach (var outputDirectory in outputDirectories)
                    fs.Directory.CreateDirectory(outputDirectory);
                Parallel.ForEach(found, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism }, x =>
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    var source = x.Source;
                    // Source and output only differ for an NPC redirected to a template, where the template's FaceGen
                    // is saved under this NPC's own name.
                    var outputRelativePath = x.OutputRelativePath;
                    NextItemSync(outputRelativePath);
                    var outputPath = fs.Path.Combine(settings.OutputDirectory, outputRelativePath);
                    var wasCopied = false;
                    try
                    {
                        if (!string.IsNullOrEmpty(source.ArchiveName))
                        {
                            var archivePath = fs.Path.Combine(source.ModComponent.Path, source.ArchiveName);
                            wasCopied = archiveProvider.CopyToFile(archivePath, source.RelativePath, outputPath);
                        }
                        else
                        {
                            var sourcePath = fs.Path.Combine(source.ModComponent.Path, source.RelativePath);
                            fs.File.Copy(sourcePath, outputPath, overwrite: true);
                            wasCopied = true;
                        }
                    }
                    // One unreadable FaceGen (a corrupt archive entry, a locked file) is one missing asset, not a reason
                    // to abort the whole build; it's reported with the others.
                    catch (Exception ex) when (ex is not OperationCanceledException) { }
                    if (wasCopied)
                    {
                        sourceModNames[outputRelativePath] = source.ModComponent.Name;
                        if (fs.Path.GetExtension(outputRelativePath).Equals(".nif", StringComparison.OrdinalIgnoreCase))
                            lock (meshPaths)
                                meshPaths.Add(outputRelativePath);
                        else
                            lock (tintPaths)
                                tintPaths.Add(outputRelativePath);
                    }
                    else
                        lock (failedPaths)
                            failedPaths.Add(outputRelativePath);
                });
                return new Result(meshPaths, tintPaths, failedPaths, sourceModNames);
            });
        }

        private IEnumerable<ModComponentInfo> ComponentsForPlugin(string pluginName)
        {
            return modRepository.SearchForFiles(pluginName, false).Select(x => x.ModComponent);
        }
    }
}
