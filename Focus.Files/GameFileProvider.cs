using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace Focus.Files
{
    public class GameFileProvider : CascadingFileProvider
    {
        public GameFileProvider(IGameSettings settings, IArchiveProvider archiveProvider)
            : this(new FileSystem(), settings, archiveProvider) { }

        public GameFileProvider(IFileSystem fs, IGameSettings settings, IArchiveProvider archiveProvider) :
            base(GetFileProviders(fs, settings, archiveProvider)) { }

        private static IEnumerable<IFileProvider> GetFileProviders(
            IFileSystem fs, IGameSettings settings, IArchiveProvider archiveProvider)
        {
            // Loose files always win over archives. The archives are resolved through one index built from the whole
            // archive order (priority order, winner first) instead of one provider per archive, so a lookup costs one
            // dictionary hit rather than one check per archive.
            var archivePaths = settings.ArchiveOrder
                .Select(f => fs.Path.Combine(settings.DataDirectory, f))
                .ToList();
            return new IFileProvider[]
            {
                new DirectoryFileProvider(fs, settings.DataDirectory),
                new IndexedArchiveFileProvider(archiveProvider, archivePaths),
            };
        }
    }
}