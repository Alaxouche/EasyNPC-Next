using Focus.Files;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public interface IFileCopier
    {
        void CopyAll(
            HashSet<string> paths, string outputDirectory, Action<string> beforeCopy,
            out IImmutableList<string> failedPaths, CancellationToken cancellationToken);
    }

    public class FileCopier : IFileCopier
    {
        // Scales with the CPU (BSA reads decompress); capped to avoid thrashing on an HDD.
        private static readonly int MaxDegreeOfParallelism = Math.Clamp(System.Environment.ProcessorCount / 2, 4, 8);

        private readonly IFileProvider fileProvider;

        public FileCopier(IFileProvider fileProvider)
        {
            this.fileProvider = fileProvider;
        }

        public void CopyAll(
            HashSet<string> paths, string outputDirectory, Action<string> beforeCopy,
            out IImmutableList<string> failedPaths, CancellationToken cancellationToken)
        {
            var mutableFailedPaths = new List<string>();
            var physicalSource = fileProvider as IPhysicalFilePathProvider;
            Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism }, path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                beforeCopy(path);
                var outputPath = Path.Combine(outputDirectory, path);
                if (!CopyOne(physicalSource, path, outputPath))
                    lock (mutableFailedPaths)
                        mutableFailedPaths.Add(outputPath);
            });
            failedPaths = mutableFailedPaths.ToImmutableList();
            paths.ExceptWith(failedPaths);
        }

        // Loose files link (or copy) via the file system directly. Archived files fall back to read-into-memory.
        private bool CopyOne(IPhysicalFilePathProvider? physicalSource, string path, string outputPath)
        {
            var sourcePath = physicalSource?.GetPhysicalFilePath(path);
            if (sourcePath is not null)
            {
                try
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    LinkOrCopy(sourcePath, outputPath);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
            if (fileProvider.CopyToFile(path, outputPath))
                return true;
            // Some mods bake broken texture paths into their FaceGen meshes (missing separator, doubled segment). The
            // real file usually exists nearby, so copy the first de-mangled variant that resolves to the expected path.
            foreach (var candidate in RepairCandidates(path))
                if (fileProvider.CopyToFile(candidate, outputPath))
                    return true;
            return false;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(
            string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        // These files are never edited after copying, so when source and output share a volume we hard-link instead of
        // copying (instant, no extra space). Cross-volume or on failure, fall back to a real copy.
        private static void LinkOrCopy(string sourcePath, string outputPath)
        {
            if (SameVolume(sourcePath, outputPath))
            {
                try
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    if (CreateHardLinkW(outputPath, sourcePath, IntPtr.Zero))
                        return;
                }
                catch
                {
                    // Fall through to a normal copy.
                }
            }
            File.Copy(sourcePath, outputPath, overwrite: true);
        }

        private static bool SameVolume(string a, string b)
        {
            var rootA = Path.GetPathRoot(Path.GetFullPath(a));
            var rootB = Path.GetPathRoot(Path.GetFullPath(b));
            return !string.IsNullOrEmpty(rootA) && string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] HeadPartFolders =
            { "brows", "lashes", "eyes", "mouth", "head", "beard", "gash", "hair" };

        private static IEnumerable<string> RepairCandidates(string path)
        {
            // First fix a garbage "textures" root (e.g. "tetextures\...", "textures\!textures\..."), then apply the
            // structural repairs on top, so both kinds of corruption can be fixed together.
            foreach (var basePath in RootFixes(path))
            {
                if (basePath != path)
                    yield return basePath;
                foreach (var repaired in StructuralRepairs(basePath))
                    yield return repaired;
            }
        }

        private static IEnumerable<string> RootFixes(string path)
        {
            yield return path;
            var segments = path.Split('\\');
            // First segment is a mangled "textures" (e.g. "tetextures", "texTextures").
            if (segments.Length > 1 && !segments[0].Equals("textures", StringComparison.OrdinalIgnoreCase) &&
                segments[0].Contains("textures", StringComparison.OrdinalIgnoreCase))
            {
                var fixedRoot = (string[])segments.Clone();
                fixedRoot[0] = "textures";
                yield return string.Join('\\', fixedRoot);
            }
            // A junk second segment that duplicates "textures" (e.g. "textures\!textures\...").
            if (segments.Length > 2 && segments[1].Contains("textures", StringComparison.OrdinalIgnoreCase))
            {
                var withoutJunk = segments.ToList();
                withoutJunk.RemoveAt(1);
                yield return string.Join('\\', withoutJunk);
            }
        }

        private static IEnumerable<string> StructuralRepairs(string path)
        {
            var segments = path.Split('\\');

            // Collapse consecutive duplicate folders (e.g. ...\Head\Head\... -> ...\Head\...).
            var collapsed = new List<string>();
            foreach (var segment in segments)
                if (collapsed.Count == 0 || !collapsed[^1].Equals(segment, StringComparison.OrdinalIgnoreCase))
                    collapsed.Add(segment);
            if (collapsed.Count != segments.Length)
                yield return string.Join('\\', collapsed);

            if (segments.Length < 2)
                yield break;
            var file = segments[^1];
            var parent = segments[^2];

            // Filename repeats its parent folder (e.g. lashes\lashesEyeLashes.dds -> lashes\EyeLashes.dds).
            if (file.Length > parent.Length && file.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            {
                var repaired = (string[])segments.Clone();
                repaired[^1] = file[parent.Length..];
                yield return string.Join('\\', repaired);
            }

            // Filename begins with a head-part folder that's missing from the path (e.g. !COR\browsbrow016.dds ->
            // !COR\brows\brow016.dds).
            foreach (var folder in HeadPartFolders)
                if (file.Length > folder.Length && file.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                    !parent.Equals(folder, StringComparison.OrdinalIgnoreCase))
                {
                    var repaired = segments.ToList();
                    repaired[^1] = file[folder.Length..];
                    repaired.Insert(repaired.Count - 1, folder);
                    yield return string.Join('\\', repaired);
                }
        }
    }
}
