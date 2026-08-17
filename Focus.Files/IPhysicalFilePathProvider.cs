namespace Focus.Files
{
    // Optional capability for providers backed by real files on disk, so callers can copy natively.
    public interface IPhysicalFilePathProvider
    {
        // Absolute on-disk path for a loose file, or null if it's unavailable or only inside an archive.
        string? GetPhysicalFilePath(string fileName);
    }
}
