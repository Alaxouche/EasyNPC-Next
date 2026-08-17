using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Profiles.FaceFinder
{
    // One face (mugshot) returned by the NPC Face Finder public API.
    public class FaceFinderFace
    {
        public string ModName { get; init; } = string.Empty;
        public string ModUrl { get; init; } = string.Empty;
        // The Nexus mod id, parsed from the mod's Nexus URL. Matches the "modid" MO2 stores for installed mods, so it
        // survives a mod folder being named differently from its Nexus page.
        public string NexusModId { get; init; } = string.Empty;
        public string ThumbnailUrl { get; init; } = string.Empty;
        public string FullUrl { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
        // True when this face is the vanilla base-game look (FaceFinder lists it under the "Skyrim Special Edition"
        // mod by "Bethesda Softworks"), so it can be matched to the Vanilla card and only to that card.
        public bool IsBaseGame { get; init; }
    }

    public interface IFaceFinderClient
    {
        Task<IReadOnlyList<FaceFinderFace>> GetNpcFacesAsync(IRecordKey npc, CancellationToken cancellationToken);
        Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken);
    }

    // Read-only client for the open NPC Face Finder API (https://npcfacefinder.com). No key required. We stay a good
    // citizen: sequential paging, a page cap, and honoring Retry-After on 429.
    public class FaceFinderClient : IFaceFinderClient
    {
        private const string BaseUrl = "https://npcfacefinder.com";
        // One page (25 faces) already covers the common overhauls; a couple more is plenty. Keeping this small avoids a
        // long chain of sequential requests that could run past the caller's timeout.
        private const int MaxPages = 3;

        private static readonly HttpClient http = CreateClient();

        private readonly ILogger log;

        public FaceFinderClient(ILogger log)
        {
            this.log = log;
        }

        public async Task<IReadOnlyList<FaceFinderFace>> GetNpcFacesAsync(
            IRecordKey npc, CancellationToken cancellationToken)
        {
            // The API's ref id is the full 8-char form id (2-char load index + 6-char local id), matching the mugshot
            // pack naming ("00" + local id).
            var formKey = $"00{npc.LocalFormIdHex}:{npc.BasePluginName}";
            var results = new List<FaceFinderFace>();
            for (var page = 1; page <= MaxPages; page++)
            {
                var url = $"{BaseUrl}/api/public/v2/npc/faces/search" +
                    $"?formKey={WebUtility.UrlEncode(formKey)}&page={page}";
                JObject? json;
                try
                {
                    json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timed out mid-paging: keep whatever we already have rather than throwing it all away.
                    break;
                }
                if (json is null)
                    break;
                foreach (var item in json["results"] as JArray ?? new JArray())
                    results.Add(ParseFace(item));
                if (json["hasMore"]?.Type != JTokenType.Boolean || !json["hasMore"]!.Value<bool>())
                    break;
            }
            // Information level so a tester's log shows the exact formKey we queried and the count. If this says
            // "0 faces for 0001A696:Skyrim.esm" (a key that works in a browser), the machine can't reach the API;
            // any real count means the fetch works and the issue is downstream (matching or display).
            log.Information("FaceFinder returned {Count} faces for {FormKey}", results.Count, formKey);
            return results;
        }

        public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    await HonorRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Debug(ex, "FaceFinder image download failed for {Url}", url);
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            // The API sits behind Cloudflare. A bare non-browser User-Agent trips its bot protection on some networks
            // (a cached edge answers fine, an uncached request elsewhere gets the connection dropped, which surfaces as
            // "request failed"). Presenting normal browser headers, modern TLS, and standard compression avoids that,
            // so the feature works the same on every machine, not just ones with a warm Cloudflare cache.
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                // Never touch the Windows system-proxy resolver. On first request it P/Invokes winhttp.dll, which is
                // missing (or has a missing dependency) on debloated / LTSC / N editions of Windows that many modders
                // run, throwing DllNotFoundException and killing every request. This is a public HTTPS API, so a
                // direct connection is all we need.
                UseProxy = false,
            };
            handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var headers = client.DefaultRequestHeaders;
            headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36");
            headers.Accept.ParseAdd("application/json, text/plain, */*");
            headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            headers.Referrer = new Uri(BaseUrl + "/");
            return client;
        }

        // The base game as FaceFinder catalogs it. "Skyrim Special Edition" is the mod title; the author check is a
        // backstop in case that title ever changes.
        private static readonly HashSet<string> BaseGameModNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Skyrim Special Edition", "Skyrim", "The Elder Scrolls V: Skyrim Special Edition",
        };

        private static FaceFinderFace ParseFace(JToken item)
        {
            var mod = item["mod"];
            var images = item["images"];
            var modUrl = mod?["external_url"]?.ToString() ?? string.Empty;
            var modName = mod?["name"]?.ToString() ?? string.Empty;
            // "author" is a nested object ({ name, external_url }).
            var authorName = mod?["author"]?["name"]?.ToString() ?? string.Empty;
            return new FaceFinderFace
            {
                ModName = modName,
                ModUrl = modUrl,
                NexusModId = ParseNexusModId(modUrl),
                ThumbnailUrl = images?["thumbnail"]?.ToString() ?? string.Empty,
                FullUrl = images?["full"]?.ToString() ?? string.Empty,
                UpdatedAt = item["updated_at"]?.ToString() ?? string.Empty,
                IsBaseGame = BaseGameModNames.Contains(modName) ||
                    string.Equals(authorName, "Bethesda Softworks", StringComparison.OrdinalIgnoreCase),
            };
        }

        private static string ParseNexusModId(string url)
        {
            var match = System.Text.RegularExpressions.Regex.Match(url, @"/mods/(\d+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private async Task<JObject?> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    log.Information("FaceFinder rate limited (429) on {Url}", url);
                    await HonorRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    log.Information("FaceFinder request {Url} returned {Status}", url, (int)response.StatusCode);
                    return null;
                }
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JObject.Parse(content);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Put the exception type and message (and inner message) on the same line so a screenshot of the log
                // is enough to tell a DNS block from a TLS failure from a connection reset.
                var inner = ex.InnerException?.Message;
                log.Warning("FaceFinder request failed for {Url}: {Error} [{Type}]{Inner}",
                    url, ex.Message, ex.GetType().Name, inner is null ? "" : " <- " + inner);
                return null;
            }
        }

        private static async Task HonorRetryAfterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 10;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30)), cancellationToken); }
            catch (OperationCanceledException) { }
        }
    }
}
