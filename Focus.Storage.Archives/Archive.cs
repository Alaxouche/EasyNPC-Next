using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Focus.Storage.Archives
{
    public class Archive : IDisposable
    {
        public static Archive FromFile(string fileName)
        {
            var archive = new Archive();
            archive.LoadFromFile(fileName);
            return archive;
        }

        public uint ArchiveFlags
        {
            get { return Functions.BsaArchiveFlagsGet(handle); }
            set { Functions.BsaArchiveFlagsSet(handle, value); }
        }

        public uint FileCount => Functions.BsaFileCountGet(handle);

        public uint FileFlags
        {
            get { return Functions.BsaFileFlagsGet(handle); }
            set { Functions.BsaFileFlagsSet(handle, value); }
        }

        public bool IsCompressionEnabled
        {
            get { return Functions.BsaCompressGet(handle); }
            set { Functions.BsaCompressSet(handle, value); }
        }

        public bool IsDataSharingEnabled
        {
            get { return Functions.BsaShareDataGet(handle); }
            set { Functions.BsaShareDataSet(handle, value); }
        }

        public ArchiveType Type => Functions.BsaArchiveTypeGet(handle);
        public uint Version => Functions.BsaVersionGet(handle);

        private readonly IntPtr handle;

        private bool disposed;

        private Archive()
        {
            handle = Functions.BsaCreate();
        }

        internal Archive(IntPtr handle)
        {
            this.handle = handle;
        }

        ~Archive()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public IReadOnlyList<string> GetFileNames()
        {
            // libbsarch calls the iteration callback from several worker threads at once, so the accumulator has to be
            // thread-safe. Adding to a plain List<T> from those threads silently lost entries, left nulls in the list
            // and occasionally threw - a listing that was short by a random handful of files on every run.
            var fileNames = new ConcurrentBag<string>();
            Functions.BsaIterateFiles(handle, (archive, filePath, fileRecord, folderRecord, context) =>
            {
                fileNames.Add(filePath);
                return false;
            }, IntPtr.Zero);
            return fileNames.ToList().AsReadOnly();
        }

        internal void LoadFromFile(string fileName)
        {
            Functions.BsaLoadFromFile(handle, fileName);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;
            if (disposing)
            {
                Functions.BsaClose(handle);
                Functions.BsaFree(handle);
            }
            disposed = true;
        }
    }
}
