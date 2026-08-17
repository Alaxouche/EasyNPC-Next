using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class ReportTask : BuildTask<BuildReport>
    {
        public delegate ReportTask Factory(
            PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly NpcFacesTask.Result faces;
        private readonly IBuildReporter reporter;

        public ReportTask(
            IBuildReporter reporter, PatchSaveTask.Result patch, ArchiveCreationTask.Result archive,
            WriteMetadataTask.Result metadata, NpcDefaultsTask.Result defaults, NpcFacesTask.Result faces)
        {
            RunsAfter(patch, archive, metadata);
            this.defaults = defaults;
            this.faces = faces;
            this.reporter = reporter;
        }

        protected override Task<BuildReport> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                var report = new BuildReport
                {
                    ModName = settings.OutputModName,
                    MergedNpcCount = defaults.Npcs.Count - faces.Skipped.Count,
                    SkippedNpcs = defaults.Skipped.Concat(faces.Skipped).ToList().AsReadOnly(),
                };
                reporter.Save(report);
                return report;
            });
        }
    }
}
