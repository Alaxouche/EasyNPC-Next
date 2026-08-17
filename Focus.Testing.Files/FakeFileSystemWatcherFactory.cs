using System.Collections.Generic;
using System.IO.Abstractions;

namespace Focus.Testing.Files
{
    public class FakeFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public IEnumerable<FakeFileSystemWatcher> Watchers => watchers;

        public IFileSystem FileSystem { get; set; }

        private readonly List<FakeFileSystemWatcher> watchers = new();

        public IFileSystemWatcher New()
        {
            var watcher = new FakeFileSystemWatcher();
            watchers.Add(watcher);
            return watcher;
        }

        public IFileSystemWatcher New(string path)
        {
            var watcher = new FakeFileSystemWatcher { Path = path };
            watchers.Add(watcher);
            return watcher;
        }

        public IFileSystemWatcher New(string path, string filter)
        {
            var watcher = new FakeFileSystemWatcher { Path = path, Filter = filter };
            watchers.Add(watcher);
            return watcher;
        }

        public IFileSystemWatcher Wrap(System.IO.FileSystemWatcher fileSystemWatcher)
        {
            var watcher = new FakeFileSystemWatcher { Path = fileSystemWatcher?.Path };
            watchers.Add(watcher);
            return watcher;
        }
    }
}
