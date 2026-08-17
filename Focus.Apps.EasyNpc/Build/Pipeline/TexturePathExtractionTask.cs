using Focus.Files;
using Focus.Providers.Mutagen;
using nifly;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class TexturePathExtractionTask : BuildTask<TexturePathExtractionTask.Result>
    {
        private static readonly IReadOnlyList<string> EmptyTexturePaths = ImmutableList<string>.Empty;

        public class Result
        {
            public IReadOnlyCollection<string> FailedSourcePaths { get; private init; }
            public IReadOnlyCollection<string> TexturePaths { get; private init; }

            public Result(
                IReadOnlyCollection<string> texturePaths,
                IReadOnlyCollection<string> failedSourcePaths)
            {
                TexturePaths = texturePaths;
                FailedSourcePaths = failedSourcePaths;
            }
        }

        public delegate TexturePathExtractionTask Factory(
            PatchSaveTask.Result patch, SharedResourceCopyTask.Result headParts, FaceGenCopyTask.Result faceGen);

        private readonly FaceGenCopyTask.Result faceGen;
        private readonly IFileSync fileSync;
        private readonly IFileSystem fs;
        private readonly SharedResourceCopyTask.Result headParts;
        private readonly ILogger log;
        private readonly PatchSaveTask.Result patch;

        public TexturePathExtractionTask(
            IFileSystem fs, IFileSync fileSync, PatchSaveTask.Result patch, SharedResourceCopyTask.Result headParts,
            FaceGenCopyTask.Result faceGen, ILogger log)
        {
            this.faceGen = faceGen;
            this.fileSync = fileSync;
            this.fs = fs;
            this.headParts = headParts;
            this.log = log;
            this.patch = patch;
        }

        protected override async Task<Result> Run(BuildSettings settings)
        {
            var meshPaths = headParts.MeshPaths.Concat(faceGen.MeshPaths).ToList();
            ItemCount.OnNext(meshPaths.Count);
            var pathsFromTextureSets = patch.Mod.TextureSets
                .SelectMany(x => new[]
                {
                    x.Diffuse.PathOrDefault(),
                    x.NormalOrGloss.PathOrDefault(),
                    x.EnvironmentMaskOrSubsurfaceTint.PathOrDefault(),
                    x.GlowOrDetailMap.PathOrDefault(),
                    x.Height.PathOrDefault(),
                    x.Environment.PathOrDefault(),
                    x.Multilayer.PathOrDefault(),
                    x.BacklightMaskOrSpecular.PathOrDefault(),
                })
                .NotNullOrEmpty()
                .Select(x => x.PrefixPath("textures"));
            var failedSourcePaths = new ConcurrentBag<string>();
            var pathsFromMeshes = await meshPaths
                .ThrottledSelect(
                    async path =>
                    {
                        NextItemSync(path);
                        var absolutePath = fs.Path.Combine(settings.OutputDirectory, path);
                        using var fileCts =
                            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                        var extractionTask = Task.Run(() => GetReferencedTexturePaths(absolutePath, fileCts.Token));
                        if (settings.TextureExtractionTimeoutSec > 0)
                            extractionTask = extractionTask
                                .WithTimeout(
                                    TimeSpan.FromSeconds(settings.TextureExtractionTimeoutSec),
                                    () => fileCts.Cancel(), CancellationToken)
                                .Catch((TimeoutException ex) =>
                                {
                                    log.Error(
                                        "Extracting texture paths from {meshPath} timed out after {timeout} seconds. " +
                                        "Some textures may be missing from the merge.",
                                        path, settings.TextureExtractionTimeoutSec);
                                    failedSourcePaths.Add(path);
                                    return EmptyTexturePaths;
                                });
                        return await extractionTask;
                    },
                    new ParallelOptions { CancellationToken = CancellationToken })
                .ToListAsync()
                .ConfigureAwait(false);
            var allTexturePaths = pathsFromMeshes
                .SelectMany(p => p)
                .Concat(pathsFromTextureSets)
                .AsParallel()
                .Select(NormalizeTexturePath)
                .ToHashSet(PathComparer.Default);
            return new Result(allTexturePaths, failedSourcePaths.ToImmutableList());
        }

        private static string? GetPathAfter(string path, string search, int offset)
        {
            var index = path.LastIndexOf(search, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? path.Substring(index + offset) : null;
        }

        // Read the real texture-set paths per shape with NiflySharp. Scanning the raw bytes for ".dds" used to glue
        // adjacent strings into bogus paths that showed up as missing assets.
        private async Task<IReadOnlyList<string>> GetReferencedTexturePaths(
            string nifFileName, CancellationToken cancellationToken)
        {
            if (!fs.File.Exists(nifFileName))
                return EmptyTexturePaths;
            byte[] nifBytes;
            using (fileSync.Lock(nifFileName))
                nifBytes = await fs.File.ReadAllBytesAsync(nifFileName, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Parsed in parallel (throttled to the CPU count by the caller). NiflySharp is safe as long as each NifFile
            // stays on one thread, which it does here.
            var texturePaths = new List<string>();
            try
            {
                using var nif = new NifFile(new vectoruchar(nifBytes));
                foreach (var shape in nif.GetShapes())
                {
                    // Slots 0-7: diffuse, normal, glow/subsurface, height, environment, env mask, tint, specular.
                    for (uint slot = 0; slot < 8; slot++)
                    {
                        string path;
                        try { path = nif.GetTexturePathByIndex(shape, slot); }
                        catch { continue; }
                        if (!string.IsNullOrWhiteSpace(path))
                            texturePaths.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Could not read texture paths from {NifPath}", nifFileName);
            }
            return texturePaths;
        }

        private string NormalizeTexturePath(string rawTexturePath)
        {
            var texturePath = rawTexturePath;
            try
            {
                if (fs.Path.IsPathRooted(texturePath))
                {
                    texturePath =
                        GetPathAfter(texturePath, @"data\textures", 5) ??
                        GetPathAfter(texturePath, @"data/textures", 5) ??
                        GetPathAfter(texturePath, @"\textures\", 1) ??
                        GetPathAfter(texturePath, @"/textures\", 1) ??
                        GetPathAfter(texturePath, @"\textures/", 1) ??
                        GetPathAfter(texturePath, @"/textures/", 1) ??
                        GetPathAfter(texturePath, @"\data\", 1) ??
                        GetPathAfter(texturePath, @"/data\", 1) ??
                        GetPathAfter(texturePath, @"\data/", 1) ??
                        GetPathAfter(texturePath, @"/data/", 1) ??
                        texturePath;
                }
            }
            catch (Exception)
            {
                // Just use the best we were able to come up with before the error.
            }
            return texturePath.PrefixPath("textures");
        }
    }
}
