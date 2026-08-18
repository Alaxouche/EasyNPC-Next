using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Files;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class ReportTask : BuildTask<BuildReport>
    {
        public delegate ReportTask Factory(
            PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces,
            TexturePathExtractionTask.Result textureExtraction);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly NpcFacesTask.Result faces;
        private readonly IBuildReporter reporter;
        private readonly TexturePathExtractionTask.Result textureExtraction;

        public ReportTask(
            IBuildReporter reporter, PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces,
            TexturePathExtractionTask.Result textureExtraction)
        {
            RunsAfter(patch, archive, metadata);
            this.defaults = defaults;
            this.faces = faces;
            this.reporter = reporter;
            this.textureExtraction = textureExtraction;
        }

        protected override Task<BuildReport> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                // Map each broken facegen mesh path back to the NPC it belongs to, so the report names the NPC (and its
                // face plugin) instead of a cryptic file path.
                var npcByFaceMesh = settings.Profile.Npcs
                    .GroupBy(n => FileStructure.GetFaceMeshFileName(n), PathComparer.Default)
                    .ToDictionary(g => g.Key, g => g.First(), PathComparer.Default);
                var brokenFaceGenNpcs = textureExtraction.BrokenFaceGenPaths
                    .Select(path => npcByFaceMesh.TryGetValue(path, out var npc)
                        ? new SkippedNpc(
                            $"{npc.EditorId} '{npc.Name}'", npc.FaceOption.PluginName,
                            "empty or corrupt FaceGen mesh (invisible face in game)")
                        : new SkippedNpc(path, string.Empty, "empty or corrupt FaceGen mesh (invisible face in game)"))
                    .OrderBy(x => x.Label, System.StringComparer.CurrentCultureIgnoreCase)
                    .ToList().AsReadOnly();
                var report = new BuildReport
                {
                    ModName = settings.OutputModName,
                    MergedNpcCount = defaults.Npcs.Count - faces.Skipped.Count,
                    SkippedNpcs = defaults.Skipped.Concat(faces.Skipped).ToList().AsReadOnly(),
                    BrokenFaceGenNpcs = brokenFaceGenNpcs,
                };
                reporter.Save(report);
                return report;
            });
        }
    }
}
