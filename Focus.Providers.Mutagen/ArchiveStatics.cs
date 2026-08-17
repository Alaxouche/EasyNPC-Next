using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Collections.Generic;

namespace Focus.Providers.Mutagen
{
    public interface IArchiveStatics
    {
        IArchiveReader CreateReader(GameRelease gameRelease, FilePath path);
        IEnumerable<FilePath> GetApplicableArchivePaths(GameRelease release, DirectoryPath dataFolderPath);
        IEnumerable<FilePath> GetApplicableArchivePaths(
            GameRelease release, DirectoryPath dataFolderPath, ModKey modKey);
        public string GetExtension(GameRelease release);
        IEnumerable<FileName> GetIniListings(GameRelease release);
    }

    public class ArchiveStatics : IArchiveStatics
    {
        public static readonly ArchiveStatics Instance = new();

        public IArchiveReader CreateReader(GameRelease gameRelease, FilePath path)
        {
            return Archive.CreateReader(gameRelease, path);
        }

        public IEnumerable<FilePath> GetApplicableArchivePaths(GameRelease release, DirectoryPath dataFolderPath)
        {
            // NOTE: The global (no-ModKey) overload sorts all archives using an internal comparer that throws
            // NotImplementedException when it isn't given a full load-order listing context, which crashes plugin
            // loading. Callers that need ordering should query per ModKey instead (see the overload below).
            return Archive.GetApplicableArchivePaths(release, dataFolderPath);
        }

        public IEnumerable<FilePath> GetApplicableArchivePaths(
            GameRelease release, DirectoryPath dataFolderPath, ModKey modKey)
        {
            return Archive.GetApplicableArchivePaths(release, dataFolderPath, modKey);
        }

        public string GetExtension(GameRelease release)
        {
            return Archive.GetExtension(release);
        }

        public IEnumerable<FileName> GetIniListings(GameRelease release)
        {
            return Archive.GetIniListings(release);
        }
    }
}
