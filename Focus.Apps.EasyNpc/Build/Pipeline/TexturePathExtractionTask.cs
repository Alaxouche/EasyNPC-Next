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
        private static readonly char[] InvalidPathChars = System.IO.Path.GetInvalidPathChars();

        public class Result
        {
            // Facegen head meshes that are empty, unparseable or have no geometry - i.e. would render as an invisible
            // face in game. We can't regenerate them (that needs the Creation Kit), but we flag them so the user can
            // pick a different face plugin or fix the source mod.
            public IReadOnlyCollection<string> BrokenFaceGenPaths { get; private init; }
            // One baked shape of a FaceGen head mesh. The name is the editor ID of the head part the shape was baked
            // from, and the vertex count is what the morph data of that head part has to match.
            public record FaceGenShape(string Name, int VertexCount);

            // Shapes found in each copied FaceGen head mesh, by output path. The meshes are parsed here anyway, and
            // their shapes are what the game matches against the head parts on the NPC's record, so collecting them
            // costs nothing and lets the build verify that the two actually agree.
            public IReadOnlyDictionary<string, IReadOnlyList<FaceGenShape>> FaceGenShapes { get; private init; }
            public IReadOnlyCollection<string> FailedSourcePaths { get; private init; }
            public IReadOnlyCollection<string> TexturePaths { get; private init; }
            // Which copied meshes reference each texture, so a missing texture can be traced to the mod that asks for
            // it instead of being reported as a bare path.
            public IReadOnlyDictionary<string, IReadOnlyList<string>> TextureReferences { get; private init; }

            public Result(
                IReadOnlyCollection<string> texturePaths,
                IReadOnlyCollection<string> failedSourcePaths,
                IReadOnlyCollection<string> brokenFaceGenPaths,
                IReadOnlyDictionary<string, IReadOnlyList<string>>? textureReferences = null,
                IReadOnlyDictionary<string, IReadOnlyList<FaceGenShape>>? faceGenShapes = null)
            {
                TexturePaths = texturePaths;
                FailedSourcePaths = failedSourcePaths;
                BrokenFaceGenPaths = brokenFaceGenPaths;
                TextureReferences = textureReferences ?? new Dictionary<string, IReadOnlyList<string>>();
                FaceGenShapes = faceGenShapes ?? new Dictionary<string, IReadOnlyList<FaceGenShape>>();
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
            // Only the per-NPC facegen head meshes are validated for "invisible face" breakage; shared head parts
            // (hair, brows...) are copied too but aren't facegen.
            var faceGenMeshes = faceGen.MeshPaths.ToHashSet(PathComparer.Default);
            var brokenFaceGen = new ConcurrentBag<string>();
            var faceGenShapes =
                new ConcurrentDictionary<string, IReadOnlyList<Result.FaceGenShape>>(PathComparer.Default);
            var pathsFromMeshes = await meshPaths
                .ThrottledSelect(
                    async path =>
                    {
                        NextItemSync(path);
                        var absolutePath = fs.Path.Combine(settings.OutputDirectory, path);
                        using var fileCts =
                            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                        var extractionTask = Task.Run(() => GetReferencedTexturePaths(
                            absolutePath, path, faceGenMeshes.Contains(path), brokenFaceGen, faceGenShapes,
                            fileCts.Token));
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
                        return (Mesh: path, Textures: await extractionTask);
                    },
                    new ParallelOptions { CancellationToken = CancellationToken })
                .ToListAsync()
                .ConfigureAwait(false);
            var textureReferences = pathsFromMeshes
                .SelectMany(x => x.Textures.Select(t => (Texture: NormalizeTexturePath(t), x.Mesh)))
                .Where(x => IsPlausibleTexturePath(x.Texture))
                .GroupBy(x => x.Texture, PathComparer.Default)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(x => x.Mesh).Distinct(PathComparer.Default).ToList(),
                    PathComparer.Default);
            var allTexturePaths = pathsFromMeshes
                .SelectMany(p => p.Textures)
                .Concat(pathsFromTextureSets)
                .AsParallel()
                .Select(NormalizeTexturePath)
                .Where(IsPlausibleTexturePath)
                .ToHashSet(PathComparer.Default);
            if (!brokenFaceGen.IsEmpty)
                log.Warning(
                    "{Count} NPC face mesh(es) look broken (empty, corrupt or no geometry) and will show as invisible " +
                    "faces in game. Pick a different face for these NPCs, or fix the source mod: {Paths}",
                    brokenFaceGen.Count, string.Join(", ", brokenFaceGen.OrderBy(p => p)));
            return new Result(
                allTexturePaths, failedSourcePaths.ToImmutableList(), brokenFaceGen.ToImmutableList(),
                textureReferences, faceGenShapes);
        }

        // Texture slots in the wild are not always real paths: mods ship meshes whose slots hold a stray "=", a
        // fragment of some other string, or a folder with no file name at all. The game never loads those, but we
        // would still try to copy them and then report every one as a missing asset, burying the genuine misses.
        private static bool IsPlausibleTexturePath(string path)
        {
            return path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) &&
                path.IndexOfAny(InvalidPathChars) < 0;
        }

        private static string? GetPathAfter(string path, string search, int offset)
        {
            var index = path.LastIndexOf(search, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? path.Substring(index + offset) : null;
        }

        // Read the real texture-set paths per shape with NiflySharp. Scanning the raw bytes for ".dds" used to glue
        // adjacent strings into bogus paths that showed up as missing assets.
        private async Task<IReadOnlyList<string>> GetReferencedTexturePaths(
            string nifFileName, string relativePath, bool isFaceGen, ConcurrentBag<string> brokenFaceGen,
            ConcurrentDictionary<string, IReadOnlyList<Result.FaceGenShape>> faceGenShapes,
            CancellationToken cancellationToken)
        {
            if (!fs.File.Exists(nifFileName))
            {
                if (isFaceGen)
                    brokenFaceGen.Add(relativePath);
                return EmptyTexturePaths;
            }
            byte[] nifBytes;
            using (fileSync.Lock(nifFileName))
                nifBytes = await fs.File.ReadAllBytesAsync(nifFileName, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // An empty facegen mesh renders as an invisible face in game.
            if (isFaceGen && nifBytes.Length == 0)
            {
                brokenFaceGen.Add(relativePath);
                return EmptyTexturePaths;
            }

            // Parsed in parallel (throttled to the CPU count by the caller). NiflySharp is safe as long as each NifFile
            // stays on one thread, which it does here.
            var texturePaths = new List<string>();
            var shapes = new List<Result.FaceGenShape>();
            var shapeCount = 0;
            try
            {
                using var nif = new NifFile(new vectoruchar(nifBytes));
                foreach (var shape in nif.GetShapes())
                {
                    shapeCount++;
                    if (isFaceGen)
                    {
                        // The shape name is the editor ID of the head part this geometry was baked from, and its
                        // vertex count is what that head part's morph data has to match.
                        var shapeName = shape.name.get();
                        if (!string.IsNullOrEmpty(shapeName))
                            shapes.Add(new Result.FaceGenShape(shapeName, (int)shape.GetNumVertices()));
                    }
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
                log.Warning(ex, "Could not read {NifPath}", nifFileName);
                // A facegen head mesh we can't even parse is corrupt -> invisible face.
                if (isFaceGen)
                    brokenFaceGen.Add(relativePath);
                return texturePaths;
            }
            // A facegen head mesh with no shapes has no geometry to render -> invisible face.
            if (isFaceGen && shapeCount == 0)
                brokenFaceGen.Add(relativePath);
            if (isFaceGen)
                faceGenShapes[relativePath] = shapes;
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
