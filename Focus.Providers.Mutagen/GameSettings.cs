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

        // Archives in *priority* order - the winner first - which is what every consumer of this property assumes:
        // the file providers take the first archive that has a file, and the post-build report names the first one as
        // the asset's source. The game loads base-game ini archives first and then each plugin's "<name>.bsa" and
        // "<name> - Textures.bsa" in load order, and a later-loaded archive wins, so priority order is that sequence
        // reversed. Building it in game order and handing it to a first-wins consumer meant EasyNPC read every
        // duplicated asset from the *lowest* priority archive - usually vanilla instead of the overhaul that was
        // supposed to provide it.
        //
        // Built by hand because Mutagen 0.53's archive sort throws for ordinary plugin BSAs.
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

                // Load order, lowest priority first...
                foreach (var iniListing in iniListings)
                    Add(iniListing.String);
                foreach (var listing in env.LoadOrder.ListedOrder)
                {
                    var name = listing.ModKey.Name;
                    Add($"{name}{extension}");
                    Add($"{name} - Textures{extension}");
                }
                ordered.Reverse();
                // ...then any other BSAs present (non-standard names). The game doesn't load these at all unless the
                // mod manager forces them, so they stay at the bottom and never override a real archive.
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
