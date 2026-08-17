using Focus.Apps.EasyNpc.Configuration;
using Serilog;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Profiles.FaceFinder
{
    // Mugshot source backed by the NPC Face Finder API. Downloads each face's thumbnail (WebP), converts it to PNG once
    // (WPF can't reliably show WebP), and caches it on disk keyed by the face's update time so later views and sessions
    // are instant and hit the network as little as possible. Any failure just yields nothing, so the local packs and
    // the 3D preview still work offline.
    public class FaceFinderMugshotRepository : IMugshotRepository
    {
        private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(6);

        private readonly string cacheDirectory;
        private readonly IFaceFinderClient client;
        private readonly ILogger log;

        public FaceFinderMugshotRepository(IFaceFinderClient client, ILogger log)
        {
            this.client = client;
            this.log = log;
            cacheDirectory = Path.Combine(ProgramData.DirectoryPath, "FaceFinderCache");
        }

        public async Task<IEnumerable<MugshotFile>> GetMugshotFiles(IRecordKey npcKey)
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            try
            {
                var faces = await client.GetNpcFacesAsync(npcKey, cts.Token).ConfigureAwait(false);
                var files = new List<MugshotFile>();
                foreach (var face in faces)
                {
                    var path = await GetOrDownloadAsync(npcKey, face, cts.Token).ConfigureAwait(false);
                    if (path is not null)
                        files.Add(new MugshotFile(face.NexusModId, face.ModName, path));
                }
                return files;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Debug(ex, "FaceFinder mugshot lookup failed for {NpcKey}", npcKey);
                return Enumerable.Empty<MugshotFile>();
            }
            catch (OperationCanceledException)
            {
                return Enumerable.Empty<MugshotFile>();
            }
        }

        public void Refresh() { }

        // Total size on disk of the downloaded online mugshots, for showing next to the "clear cache" button.
        public long GetCacheSizeBytes()
        {
            try
            {
                if (!Directory.Exists(cacheDirectory))
                    return 0;
                return Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }
            catch (Exception ex)
            {
                log.Debug(ex, "Could not measure FaceFinder cache");
                return 0;
            }
        }

        // Deletes every cached online mugshot. They are re-downloaded on demand, so this is always safe.
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(cacheDirectory))
                    Directory.Delete(cacheDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Could not clear FaceFinder cache");
            }
        }

        // Metadata only (one API call, no image downloads) so the caller can match faces to installed mods first and
        // then download just the images it actually needs.
        public Task<IReadOnlyList<FaceFinderFace>> GetFacesAsync(IRecordKey npc, CancellationToken cancellationToken)
        {
            return client.GetNpcFacesAsync(npc, cancellationToken);
        }

        // Downloads and caches the image for a single face, returning the local PNG path (or null on failure).
        public Task<string?> EnsureCachedImageAsync(
            IRecordKey npc, FaceFinderFace face, CancellationToken cancellationToken)
        {
            return GetOrDownloadAsync(npc, face, cancellationToken);
        }

        private async Task<string?> GetOrDownloadAsync(
            IRecordKey npcKey, FaceFinderFace face, CancellationToken cancellationToken)
        {
            var url = !string.IsNullOrEmpty(face.ThumbnailUrl) ? face.ThumbnailUrl : face.FullUrl;
            if (string.IsNullOrEmpty(url))
                return null;
            var npcFolder = Path.Combine(cacheDirectory, Sanitize($"{npcKey.BasePluginName}_{npcKey.LocalFormIdHex}"));
            var cachePath = Path.Combine(npcFolder, $"{Sanitize(face.ModName)}_{ShortHash(face.UpdatedAt)}.png");
            if (File.Exists(cachePath))
                return cachePath;
            var bytes = await client.DownloadImageAsync(url, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                return null;
            try
            {
                Directory.CreateDirectory(npcFolder);
                using var image = Image.Load(bytes);
                image.SaveAsPng(cachePath);
                return cachePath;
            }
            catch (Exception ex)
            {
                log.Debug(ex, "Could not convert FaceFinder image for {ModName}", face.ModName);
                return null;
            }
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return builder.ToString();
        }

        private static string ShortHash(string value)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return Convert.ToHexString(bytes, 0, 4);
        }
    }
}
