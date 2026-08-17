using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Storage.Archives;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class ArchiveCreationTask : BuildTask<ArchiveCreationTask.Result>
    {
        public class Result
        {
            public bool Skipped { get; private init; }

            public Result(bool skipped = false)
            {
                Skipped = skipped;
            }
        }

        public delegate ArchiveCreationTask Factory(
            PatchSaveTask.Result patch, FaceGenCopyTask.Result faceGens, DewiggifyFaceGensTask.Result faceGenDewiggify,
            SharedResourceCopyTask.Result headParts, TextureCopyTask.Result textures);

        private const long GB = 1024 * 1024 * 1024;

        private readonly ICompressionEstimator compressionEstimator;
        private readonly IDummyPluginBuilder dummyPluginBuilder;
        private readonly FaceGenCopyTask.Result faceGens;
        private readonly IFileSystem fs;
        private readonly SharedResourceCopyTask.Result headParts;
        private readonly PatchSaveTask.Result patch;
        private readonly TextureCopyTask.Result textures;

        public ArchiveCreationTask(
            IFileSystem fs, ICompressionEstimator compressionEstimator, IDummyPluginBuilder dummyPluginBuilder,
            PatchSaveTask.Result patch, FaceGenCopyTask.Result faceGens, DewiggifyFaceGensTask.Result faceGenDewiggify,
            SharedResourceCopyTask.Result headParts, TextureCopyTask.Result textures)
        {
            RunsAfter(faceGenDewiggify);
            this.compressionEstimator = compressionEstimator;
            this.dummyPluginBuilder = dummyPluginBuilder;
            this.faceGens = faceGens;
            this.fs = fs;
            this.headParts = headParts;
            this.patch = patch;
            this.textures = textures;
        }

        protected override async Task<Result> Run(BuildSettings settings)
        {
            if (!settings.EnableArchiving)
                return new Result(true);

            // FaceGen files are usually much larger and therefore more expensive than other meshes/textures.
            // Applying extra weight to these gives a somewhat more realistic ETA.
            const int FaceGenWeightMultiplier = 3;
            const int MeshProgressWeight = 1;
            // Textures are considerably more expensive to add due to the the type of compression, so applying a higher
            // weight to them avoids "rushing" the progress while the meshes are running in parallel. We'll still get
            // some rushing due to .NET's Parallel implementation that creates a large backlog.
            const int TextureProgressWeight = 3;

            // A BSA must stay under 2 GB or the game can crash unpredictably. The compressed file is always smaller than
            // the uncompressed data, so splitting so no group holds more than 2 GB of uncompressed data guarantees a
            // safe archive whatever the real compression ratio turns out to be (the ratio estimate is only a rough
            // secondary guide - most already-compressed DDS barely shrink further). This is the same conservative,
            // size-based split that tools like BSArch use. Two archives at ~1.9 GB are always safer than one at 3 GB+.
            const long MaxMeshesUncompressedSize = 2 * GB;
            const long MaxTexturesUncompressedSize = 2 * GB;

            // FaceGen kept loose is excluded from the archives, so don't count it toward the progress total or the bar
            // stalls short of full.
            var faceGenMeshWeight =
                settings.KeepFaceGenOutsideArchive ? 0 : faceGens.MeshPaths.Count * FaceGenWeightMultiplier;
            var faceGenTintWeight =
                settings.KeepFaceGenOutsideArchive ? 0 : faceGens.TintPaths.Count * FaceGenWeightMultiplier;
            var meshProgressSize = MeshProgressWeight *
                (faceGenMeshWeight + headParts.MeshPaths.Count + headParts.MorphPaths.Count);
            var textureProgressSize = TextureProgressWeight *
                (faceGenTintWeight + textures.TexturePaths.Count);
            // The "+5" below adds some headroom for follow-up tasks - dummy plugins and file cleanup.
            ItemCount.OnNext(meshProgressSize + textureProgressSize + 5);

            var baseName = fs.Path.GetFileNameWithoutExtension(patch.Path);
            var meshesTask = Task.Run(() => BuildFilteredArchive(settings.OutputDirectory, new()
            {
                Name = baseName,
                RelativePath = "meshes",
                DefaultProgressWeight = MeshProgressWeight,
                FaceGenProgressWeight = MeshProgressWeight * FaceGenWeightMultiplier,
                MaxUncompressedSize = MaxMeshesUncompressedSize,
                ExcludeFaceGen = settings.KeepFaceGenOutsideArchive,
            }));
            var texturesTask = Task.Run(() => BuildFilteredArchive(settings.OutputDirectory, new()
            {
                Name = $"{baseName} - Textures",
                RelativePath = "textures",
                DefaultProgressWeight = TextureProgressWeight,
                FaceGenProgressWeight = TextureProgressWeight * FaceGenWeightMultiplier,
                MaxUncompressedSize = MaxTexturesUncompressedSize,
                ExcludeFaceGen = settings.KeepFaceGenOutsideArchive,
            }));
            await Task.WhenAll(meshesTask, texturesTask);

            ItemName.OnNext("Creating dummy plugins");
            var archiveFileNames = meshesTask.Result.ArchiveResults
                .Concat(texturesTask.Result.ArchiveResults)
                .Select(x => x.FileName);
            foreach (var archiveFileName in archiveFileNames)
            {
                var archiveBaseName = fs.Path.GetFileNameWithoutExtension(archiveFileName);
                // Neither the default archive (with same name as merge) nor the standard textures archive need
                // dummy plugins; the game recognizes these automatically.
                if (archiveBaseName != baseName && archiveBaseName != $"{baseName} - Textures")
                    dummyPluginBuilder.CreateDummyPlugin(fs.Path.ChangeExtension(archiveFileName, ".esp"));
            }

            ItemName.OnNext("Cleaning up loose files");
            if (settings.KeepFaceGenOutsideArchive)
            {
                // Everything else went into the BSA; keep only the loose FaceGen behind.
                DeleteLooseExceptFaceGen(settings.OutputDirectory, "meshes");
                DeleteLooseExceptFaceGen(settings.OutputDirectory, "textures");
            }
            else
            {
                fs.Directory.Delete(fs.Path.Combine(settings.OutputDirectory, "meshes"), true);
                fs.Directory.Delete(fs.Path.Combine(settings.OutputDirectory, "textures"), true);
            }

            return new Result();
        }

        private ArchiveBuilder.BuildResult BuildFilteredArchive(string outputDirectory, ArchiveSettings settings)
        {
            var outputFileName = fs.Path.Combine(outputDirectory, settings.Name) + ".bsa";
            return new ArchiveBuilder(ArchiveType.SSE)
                .AddDirectory(
                    fs.Path.Combine(outputDirectory, settings.RelativePath), settings.RelativePath,
                    excludePathInArchive: settings.ExcludeFaceGen ? FileStructure.IsFaceGen : null)
                .Compress(true)
                .ShareData(true)
                .MaxCompressedSize((long)(1.8 * GB /* leave headroom */), x =>
                {
                    var ratio = compressionEstimator.EstimateCompressionRatio(x.PathInArchive);
                    return (long)(x.Size * ratio);
                })
                .MaxUncompressedSize(settings.MaxUncompressedSize)
                .OnBeforeBuild(entries => CancellationToken.ThrowIfCancellationRequested())
                .OnPacking(entry =>
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    NextItemSync($"[{settings.Name}.bsa] <- {entry.PathInArchive}", 0);
                })
                .OnPacked(entry => NextItemSync(
                    $"[{settings.Name}.bsa] <- {entry.PathInArchive}",
                    FileStructure.IsFaceGen(entry.PathInArchive) ?
                        settings.FaceGenProgressWeight : settings.DefaultProgressWeight))
                .Build(outputFileName);
        }

        // Delete the loose files under a top-level folder (meshes/textures) but leave the FaceGen behind, then prune
        // any directories left empty.
        private void DeleteLooseExceptFaceGen(string outputDirectory, string relativePath)
        {
            var root = fs.Path.Combine(outputDirectory, relativePath);
            if (!fs.Directory.Exists(root))
                return;
            // Materialize before deleting - deleting while enumerating the same tree is not safe.
            var files = fs.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var pathInOutput = fs.Path.GetRelativePath(outputDirectory, file);
                if (!FileStructure.IsFaceGen(pathInOutput))
                    fs.File.Delete(file);
            }
            foreach (var directory in fs.Directory
                .GetDirectories(root, "*", System.IO.SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length))
                if (!fs.Directory.EnumerateFileSystemEntries(directory).Any())
                    fs.Directory.Delete(directory);
        }

        class ArchiveSettings
        {
            public int DefaultProgressWeight { get; init; }
            public bool ExcludeFaceGen { get; init; }
            public int FaceGenProgressWeight { get; init; }
            public long MaxUncompressedSize { get; init; }
            public string Name { get; init; } = string.Empty;
            public string RelativePath { get; init; } = string.Empty;
        }
    }
}
