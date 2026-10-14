using Focus.Apps.EasyNpc.Configuration;
using Focus.Files;
using Focus.ModManagers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class WriteMetadataTask : BuildTask<WriteMetadataTask.Result>
    {
        private static readonly string MetadataFileName = "build_info.json";

        public class Result
        {
            public IReadOnlyList<MissingAssetGroup> MissingAssetGroups { get; init; } = new List<MissingAssetGroup>();
        }

        // Label for missing textures that no copied mesh references (they come from texture set records).
        private const string TextureSetsSource = "(texture sets in the merged plugin)";

        public delegate WriteMetadataTask Factory(
            FaceGenCopyTask.Result faceGen, TexturePathExtractionTask.Result textureExtraction,
            TextureCopyTask.Result textures, SharedResourceCopyTask.Result sharedResources,
            NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly NpcFacesTask.Result faces;
        private readonly FaceGenCopyTask.Result faceGen;
        private readonly IFileSystem fs;
        private readonly IModRepository modRepository;
        private readonly SharedResourceCopyTask.Result sharedResources;
        private readonly TexturePathExtractionTask.Result textureExtraction;
        private readonly TextureCopyTask.Result textures;

        public WriteMetadataTask(
            IFileSystem fs, IModRepository modRepository, FaceGenCopyTask.Result faceGen,
            TexturePathExtractionTask.Result textureExtraction, TextureCopyTask.Result textures,
            SharedResourceCopyTask.Result sharedResources, NpcDefaultsTask.Result defaults,
            NpcFacesTask.Result faces)
        {
            this.defaults = defaults;
            this.faces = faces;
            this.fs = fs;
            this.modRepository = modRepository;
            this.faceGen = faceGen;
            this.sharedResources = sharedResources;
            this.textureExtraction = textureExtraction;
            this.textures = textures;
        }

        protected override async Task<Result> Run(BuildSettings settings)
        {
            var missingAssetPaths = GetMissingAssetPaths(settings);
            var missingAssetGroups = GroupMissingAssets(missingAssetPaths);
            var metadata = new Metadata
            {
                MissingAssetsByMod = missingAssetGroups,
                SkippedNpcs = defaults.Skipped.Concat(faces.Skipped).ToList(),
                BrokenFaceGenPaths = textureExtraction.BrokenFaceGenPaths.OrderBy(
                    p => p, StringComparer.CurrentCultureIgnoreCase).ToList(),
                FailedTexturePathExtractionSources = textureExtraction.FailedSourcePaths.ToList(),
                MissingAssetPaths = faceGen.FailedPaths
                    .Concat(sharedResources.FailedPaths)
                    .Concat(textures.FailedPaths)
                    // Some tasks might add paths relative to the mod output directory, which tends
                    // to be unhelpful, so strip that.
                    // If an absolute path refers to a source outside the output directory, then we
                    // want to keep it.
                    .Select(path => path.StartsWith(
                        settings.OutputDirectory, StringComparison.CurrentCultureIgnoreCase)
                        ? fs.Path.GetRelativePath(settings.OutputDirectory, path)
                        : path)
                    .Select(path => !fs.Path.IsPathRooted(path) ? path : path.Replace(@"\", "/"))
                    .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
            var outputJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            var metadataPath = fs.Path.Combine(settings.OutputDirectory, MetadataFileName);
            await fs.File.WriteAllTextAsync(metadataPath, outputJson, CancellationToken);
            return new() { MissingAssetGroups = missingAssetGroups };
        }

        private List<string> GetMissingAssetPaths(BuildSettings settings)
        {
            return faceGen.FailedPaths
                .Concat(sharedResources.FailedPaths)
                .Concat(textures.FailedPaths)
                .Select(path => path.StartsWith(settings.OutputDirectory, StringComparison.CurrentCultureIgnoreCase)
                    ? fs.Path.GetRelativePath(settings.OutputDirectory, path)
                    : path)
                .Where(path => !fs.Path.IsPathRooted(path))
                .Distinct(PathComparer.Default)
                .ToList();
        }

        // Attributes each missing asset to the mod(s) whose copied meshes reference it: the FaceGen's source mod for a
        // face, otherwise whichever mod provides the head part mesh.
        private List<MissingAssetGroup> GroupMissingAssets(IEnumerable<string> missingPaths)
        {
            var meshModCache = new Dictionary<string, string>(PathComparer.Default);
            string ModOfMesh(string meshPath)
            {
                if (meshModCache.TryGetValue(meshPath, out var cached))
                    return cached;
                var name = faceGen.SourceModNames.TryGetValue(meshPath, out var faceGenMod) ? faceGenMod :
                    modRepository.SearchForFiles(meshPath, true).Select(x => x.ModComponent.Name).FirstOrDefault() ??
                    "(unknown mod)";
                meshModCache[meshPath] = name;
                return name;
            }

            return missingPaths
                .SelectMany(path =>
                    (textureExtraction.TextureReferences.TryGetValue(path, out var meshes) && meshes.Count > 0 ?
                        meshes.Select(ModOfMesh).Distinct() :
                        new[] { TextureSetsSource })
                    .Select(mod => (Mod: mod, Path: path)))
                .GroupBy(x => x.Mod, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new MissingAssetGroup
                {
                    ModName = g.Key,
                    Count = g.Select(x => x.Path).Distinct(PathComparer.Default).Count(),
                    Examples = g.Select(x => x.Path).Distinct(PathComparer.Default).Take(5).ToList(),
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.ModName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // Private for now, though in the future there may be reasons to pull this out, e.g.
        // incremental builds, restoring previous build settings, etc.
        class Metadata
        {
            public string AppVersion { get; set; } = AssemblyProperties.Version.ToString();
            public List<string> BrokenFaceGenPaths { get; set; } = new();
            public List<string> FailedTexturePathExtractionSources { get; set; } = new();
            public List<string> MissingAssetPaths { get; set; } = new();
            public List<MissingAssetGroup> MissingAssetsByMod { get; set; } = new();
            public List<SkippedNpc> SkippedNpcs { get; set; } = new();
        }
    }
}
