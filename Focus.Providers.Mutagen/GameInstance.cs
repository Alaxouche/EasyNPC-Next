using Mutagen.Bethesda;
using Noggog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Focus.Providers.Mutagen
{
    public class GameInstance : GameSelection
    {
        public string DataDirectory { get; private init; }

        // Alternate distributions of the same game. Each one keeps its own plugins.txt and loadorder.txt in a
        // different folder under %LocalAppData% ("Skyrim Special Edition" vs "Skyrim Special Edition GOG"), so reading
        // a GOG install as the Steam edition silently uses the Steam load order - which is exactly what GOG users
        // reported ("it always defaults to the plugins.txt for the Steam Skyrim", even with the right -p).
        private static readonly IReadOnlyDictionary<GameRelease, GameRelease> AlternateEditions =
            new Dictionary<GameRelease, GameRelease>
            {
                { GameRelease.SkyrimSE, GameRelease.SkyrimSEGog },
            };

        public static GameInstance FromGameId(string gameId, string? dataDirectory = null)
        {
            return FromGameId(new StaticGameLocations(), gameId, dataDirectory);
        }

        public static GameInstance FromGameId(IGameLocations gameLocations, string gameId, string? dataDirectory = null)
        {
            var isValidGameName = Enum.TryParse<GameRelease>(gameId, true, out var gameRelease);
            if (!isValidGameName)
                throw new UnsupportedGameException(gameId);
            if (string.IsNullOrEmpty(dataDirectory))
            {
                if (!TryDetectDataFolder(gameLocations, ref gameRelease, out var detectedDirectory))
                    throw new MissingGameDataException(Enum.GetName(gameRelease)!, GetGameName(gameRelease));
                dataDirectory = detectedDirectory;
            }
            else
            {
                // An explicit path (from "-p" or the game folder setting) tells us where the files are, but not which
                // edition's load order to read. Recognizing the folder as an alternate edition's install is what makes
                // a GOG copy work without the user having to know about game IDs.
                gameRelease = ResolveEditionForDirectory(gameLocations, gameRelease, dataDirectory);
            }
            return new GameInstance(gameRelease, dataDirectory);
        }

        public GameInstance(GameRelease gameRelease, string dataDirectory)
            : base(gameRelease)
        {
            DataDirectory = dataDirectory;
        }

        private static bool TryDetectDataFolder(
            IGameLocations gameLocations, ref GameRelease gameRelease, [MaybeNullWhen(false)] out string dataDirectory)
        {
            if (gameLocations.TryGetDataFolder(gameRelease, out DirectoryPath path))
            {
                dataDirectory = path;
                return true;
            }
            // Not found as the requested edition - try the alternate distribution before giving up, so a user who only
            // has the GOG copy installed doesn't get "game not found" for asking about "SkyrimSE".
            if (AlternateEditions.TryGetValue(gameRelease, out var alternate) &&
                gameLocations.TryGetDataFolder(alternate, out DirectoryPath alternatePath))
            {
                gameRelease = alternate;
                dataDirectory = alternatePath;
                return true;
            }
            dataDirectory = null;
            return false;
        }

        private static GameRelease ResolveEditionForDirectory(
            IGameLocations gameLocations, GameRelease gameRelease, string dataDirectory)
        {
            if (!AlternateEditions.TryGetValue(gameRelease, out var alternate))
                return gameRelease;
            // If the path we were given lives under the primary edition's install, keep the primary edition. Checking
            // this first matters when both editions are installed.
            if (gameLocations.TryGetDataFolder(gameRelease, out DirectoryPath primaryPath) &&
                IsSameInstall(primaryPath, dataDirectory))
                return gameRelease;
            if (gameLocations.TryGetDataFolder(alternate, out DirectoryPath alternatePath) &&
                IsSameInstall(alternatePath, dataDirectory))
                return alternate;
            return gameRelease;
        }

        // True when the two paths point into the same game install. The detected folder is the "data" directory while
        // the configured one may be the game root (or vice versa), so a containment check either way is what we want.
        private static bool IsSameInstall(string detectedPath, string configuredPath)
        {
            var detected = NormalizePath(detectedPath);
            var configured = NormalizePath(configuredPath);
            if (string.IsNullOrEmpty(detected) || string.IsNullOrEmpty(configured))
                return false;
            return
                detected.Equals(configured, StringComparison.OrdinalIgnoreCase) ||
                detected.StartsWith(configured + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                configured.StartsWith(detected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A malformed configured path shouldn't crash startup; it just can't match anything.
                return string.Empty;
            }
        }
    }
}
