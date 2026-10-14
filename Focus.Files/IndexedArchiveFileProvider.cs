using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Focus.Files
{
    // Resolves a file against a whole list of archives with one lookup. Asking each archive in turn cost one lookup per
    // archive for every file, and a large mod list has well over a thousand archives; a build copying tens of thousands
    // of textures, each checked several times, spent most of that time asking archives that don't have the file.
    public class IndexedArchiveFileProvider : IFileProvider
    {
        private readonly IArchiveProvider archiveProvider;
        private readonly IReadOnlyList<string> archivePaths;
        private readonly Lazy<Dictionary<string, int>> index;

        // Archive paths must be in priority order, winner first: when several archives have a file, the first one wins.
        public IndexedArchiveFileProvider(IArchiveProvider archiveProvider, IEnumerable<string> archivePaths)
        {
            this.archiveProvider = archiveProvider;
            this.archivePaths = archivePaths.ToList().AsReadOnly();
            index = new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool Exists(string fileName)
        {
            return index.Value.ContainsKey(fileName);
        }

        public ulong GetSize(string fileName)
        {
            return TryGetArchivePath(fileName, out var archivePath) ?
                archiveProvider.GetArchiveFileSize(archivePath, fileName) : 0;
        }

        public ReadOnlySpan<byte> ReadBytes(string fileName)
        {
            return TryGetArchivePath(fileName, out var archivePath) ?
                archiveProvider.ReadBytes(archivePath, fileName) : null;
        }

        private Dictionary<string, int> BuildIndex()
        {
            // Listing is the slow part (it reads each archive's header), so do it in parallel; merging has to be in
            // priority order so the winner is the one that ends up in the index.
            var listings = new IReadOnlyList<string>[archivePaths.Count];
            Parallel.For(0, archivePaths.Count, i =>
                listings[i] = archiveProvider.GetArchiveFileNames(archivePaths[i]).ToList());
            var result = new Dictionary<string, int>(PathComparer.Default);
            for (var i = 0; i < listings.Length; i++)
                foreach (var fileName in listings[i])
                    result.TryAdd(fileName, i);
            return result;
        }

        private bool TryGetArchivePath(string fileName, out string archivePath)
        {
            if (index.Value.TryGetValue(fileName, out var archiveIndex))
            {
                archivePath = archivePaths[archiveIndex];
                return true;
            }
            archivePath = string.Empty;
            return false;
        }
    }
}
