using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Focus.Apps.EasyNpc.Configuration
{
    /// <summary>
    /// Validation for a user-supplied game folder.
    /// </summary>
    /// <remarks>
    /// Pointing the app at the wrong folder used to surface as an unexplained failure on the *next* launch, which is a
    /// miserable way to find out. Checking here means the user is told immediately, while the folder picker is still
    /// fresh in their mind.
    /// </remarks>
    public static class GameFolder
    {
        // Any one of these means we're looking at a Bethesda game's data folder. Skyrim.esm covers SE/LE/VR/GOG and
        // Enderal (which ships alongside Skyrim's masters); the others let the same check work if this is ever pointed
        // at another supported game.
        private static readonly string[] MasterFileNames = new[]
        {
            "Skyrim.esm", "Fallout4.esm", "Oblivion.esm", "Starfield.esm",
        };

        /// <summary>
        /// Returns the data folder for a user-supplied path: either the path itself, or its "Data" subfolder when the
        /// user picked the game root instead. Returns null when neither looks like a game data folder.
        /// </summary>
        public static string? ResolveDataFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return null;
            if (ContainsMasterFile(path))
                return path;
            var dataSubfolder = Path.Combine(path, "Data");
            return Directory.Exists(dataSubfolder) && ContainsMasterFile(dataSubfolder) ? dataSubfolder : null;
        }

        /// <summary>
        /// Returns a human-readable problem with the given path, or an empty string when it's usable (including when
        /// it's empty, which means "detect the game automatically").
        /// </summary>
        public static string Validate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            if (!Directory.Exists(path))
                return "This folder doesn't exist.";
            if (ResolveDataFolder(path) is null)
                return
                    $"No game master file ({string.Join(", ", MasterFileNames)}) was found here. " +
                    "Choose your game's Data folder, or the game folder that contains it.";
            return string.Empty;
        }

        private static bool ContainsMasterFile(string directory)
        {
            try
            {
                return MasterFileNames.Any(x => File.Exists(Path.Combine(directory, x)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder can't be validated, but shouldn't crash the settings page either.
                return false;
            }
        }
    }

    /// <summary>
    /// One selectable game distribution. The <see cref="Id"/> is a Mutagen <c>GameRelease</c> name, or empty for
    /// automatic detection.
    /// </summary>
    public class GameEditionViewModel
    {
        public static readonly IReadOnlyList<GameEditionViewModel> All = new List<GameEditionViewModel>
        {
            new(string.Empty, "Detect automatically"),
            new("SkyrimSE", "Skyrim Special Edition (Steam)"),
            new("SkyrimSEGog", "Skyrim Special Edition (GOG)"),
            new("SkyrimVR", "Skyrim VR"),
            new("EnderalSE", "Enderal Special Edition"),
        }.AsReadOnly();

        public string Id { get; }
        public string Name { get; }

        public GameEditionViewModel(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
