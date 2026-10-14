using Mutagen.Bethesda;
using System;
using System.Diagnostics;
using System.IO;

namespace Focus.Providers.Mutagen
{
    // Skyrim SE 1.6.1130 moved plugins to header version 1.71 (to widen the ESL record range). Older game versions
    // (1.5.97 up to 1.6.659, still common in modlists) crash on 1.71 plugins unless Backported Extended ESL Support is
    // installed, while 1.70 plugins load on every version. Mutagen writes 1.71 by default.
    public static class PluginHeaderVersions
    {
        public const string Auto = "Auto";
        public const string Legacy = "1.70";
        public const string Extended = "1.71";

        public static readonly Version FirstExtendedGameVersion = new(1, 6, 1130);

        private const float LegacyValue = 1.7f;
        private const float ExtendedValue = 1.71f;

        // Returns the header version to write, or null to keep Mutagen's default for the game release. "reason" says how
        // it was decided, for the log.
        public static float? Resolve(string? setting, GameRelease gameRelease, string dataDirectory, out string reason)
        {
            if (string.Equals(setting, Legacy, StringComparison.OrdinalIgnoreCase))
            {
                reason = "set to 1.70 in Settings";
                return LegacyValue;
            }
            if (string.Equals(setting, Extended, StringComparison.OrdinalIgnoreCase))
            {
                reason = "set to 1.71 in Settings";
                return ExtendedValue;
            }
            // Only Skyrim SE has two header versions to choose between; everything else keeps Mutagen's default.
            if (gameRelease is not (GameRelease.SkyrimSE or GameRelease.SkyrimSEGog))
            {
                reason = $"default for {gameRelease}";
                return null;
            }
            var gameVersion = TryGetGameVersion(dataDirectory);
            if (gameVersion is null)
            {
                // 1.70 loads on every game version; guessing 1.71 wrong would crash the game.
                reason = "game version unknown, using 1.70 because it loads on every game version";
                return LegacyValue;
            }
            if (gameVersion < FirstExtendedGameVersion)
            {
                reason = $"game version {gameVersion} is older than {FirstExtendedGameVersion}";
                return LegacyValue;
            }
            reason = $"game version {gameVersion} supports 1.71";
            return ExtendedValue;
        }

        public static Version? TryGetGameVersion(string dataDirectory)
        {
            try
            {
                var gameFolder = Directory.GetParent(Path.TrimEndingDirectorySeparator(dataDirectory))?.FullName;
                if (gameFolder is null)
                    return null;
                var executable = Path.Combine(gameFolder, "SkyrimSE.exe");
                if (!File.Exists(executable))
                    return null;
                var info = FileVersionInfo.GetVersionInfo(executable);
                return info.FileMajorPart == 0 && info.FileMinorPart == 0 ?
                    null : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
