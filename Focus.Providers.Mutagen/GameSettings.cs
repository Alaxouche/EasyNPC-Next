using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace Focus.Providers.Mutagen
{
    public static class GameSettings
    {
        public static GameSettings<TModGetter> From<TModGetter>(
            IReadOnlyGameEnvironment<TModGetter> env, GameSelection game, IFileSystem fs)
            where TModGetter : class, IModGetter
        {
            return new GameSettings<TModGetter>(env, ArchiveStatics.Instance, game, fs);
        }
    }

    public class GameSettings<TModGetter> : IGameSettings
        where TModGetter : class, IModGetter
    {
        private readonly IArchiveStatics archive;
        private readonly IReadOnlyGameEnvironment<TModGetter> env;
        private readonly IFileSystem fs;
        private readonly GameSelection game;
        private readonly HashSet<FileName> iniListings;

        public GameSettings(
            IReadOnlyGameEnvironment<TModGetter> env, IArchiveStatics archive, GameSelection game, IFileSystem fs)
        {
            this.archive = archive;
            this.env = env;
            this.fs = fs;
            this.game = game;
            this.iniListings = archive.GetIniListings(game.GameRelease).ToHashSet();
        }

        // Archives in load order, built by hand: Mutagen 0.53's archive sort throws for ordinary plugin BSAs. Matches
        // the game's rule: base-game ini archives first, then each plugin's "<name>.bsa" and "<name> - Textures.bsa".
        public IEnumerable<string> ArchiveOrder
        {
            get
            {
                // Enumerate DataDirectory (what the providers read), not env.DataFolderPath, which can be the game root.
                var dataFolder = DataDirectory;
                var extension = archive.GetExtension(game.GameRelease);
                var existingArchives = fs.Directory.Exists(dataFolder)
                    ? fs.Directory.EnumerateFiles(dataFolder, "*" + extension)
                        .Select(x => fs.Path.GetFileName(x))
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase)
                    : new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                void Add(string archiveName)
                {
                    if (existingArchives.Contains(archiveName) && seen.Add(archiveName))
                        ordered.Add(archiveName);
                }

                foreach (var iniListing in iniListings)
                    Add(iniListing.String);
                foreach (var listing in env.LoadOrder.ListedOrder)
                {
                    var name = listing.ModKey.Name;
                    Add($"{name}{extension}");
                    Add($"{name} - Textures{extension}");
                }
                // Include any other BSAs present (non-standard names), appended last so they don't override the above.
                foreach (var archiveName in existingArchives)
                    Add(archiveName);
                return ordered;
            }
        }
        public string DataDirectory => env.GetRealDataDirectory();
        public IEnumerable<string> PluginLoadOrder => env.LoadOrder.ListedOrder.Select(x => x.ModKey.FileName.String);

        public bool IsBaseGameArchive(string archiveName)
        {
            return iniListings.Contains(archiveName);
        }
    }
}
