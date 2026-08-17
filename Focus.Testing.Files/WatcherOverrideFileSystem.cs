using System.IO.Abstractions;

namespace Focus.Testing.Files
{
    // System.IO.Abstractions' MockFileSystem no longer allows replacing its FileSystemWatcher factory (the property is
    // read-only as of v22). This decorator forwards every member to an inner file system except the watcher factory,
    // so tests can still inject a FakeFileSystemWatcherFactory to drive change events.
    public class WatcherOverrideFileSystem : IFileSystem
    {
        private readonly IFileSystem inner;

        public WatcherOverrideFileSystem(IFileSystem inner, IFileSystemWatcherFactory fileSystemWatcher)
        {
            this.inner = inner;
            FileSystemWatcher = fileSystemWatcher;
        }

        public IDirectory Directory => inner.Directory;
        public IDirectoryInfoFactory DirectoryInfo => inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => inner.DriveInfo;
        public IFile File => inner.File;
        public IFileInfoFactory FileInfo => inner.FileInfo;
        public IFileStreamFactory FileStream => inner.FileStream;
        public IFileSystemWatcherFactory FileSystemWatcher { get; }
        public IFileVersionInfoFactory FileVersionInfo => inner.FileVersionInfo;
        public IPath Path => inner.Path;
    }
}
