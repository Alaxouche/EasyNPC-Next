using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Order.DI;
using Noggog;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Focus.Providers.Mutagen
{
    // Where the load order was read from - logged at startup, because "EasyNPC only sees vanilla" and "masters are
    // missing" reports are almost always the wrong one of these files.
    public record LoadOrderSources(string? PluginsFilePath, string? CreationClubFilePath);

    public interface ISetupStatics
    {
        public IReadOnlyCollection<ModKey> GetBaseMasters(GameRelease gameRelease);
        public IEnumerable<ILoadOrderListingGetter> GetLoadOrderListings(
            GameRelease gameRelease, DirectoryPath dataDirectory, bool throwOnMissingMasters = true);
        public LoadOrderSources GetLoadOrderSources(GameRelease gameRelease, DirectoryPath dataDirectory);
    }

    public class SetupStatics : ISetupStatics
    {
        private readonly string? pluginsFilePathOverride;

        public SetupStatics() : this(null) { }

        // An explicit plugins.txt replaces the game's own under %LocalAppData%: Linux/Proton users whose app and game run
        // in different prefixes, or anyone who wants to point straight at a Mod Organizer profile's plugins.txt.
        public SetupStatics(string? pluginsFilePathOverride)
        {
            this.pluginsFilePathOverride =
                string.IsNullOrWhiteSpace(pluginsFilePathOverride) ? null : pluginsFilePathOverride;
        }

        public IReadOnlyCollection<ModKey> GetBaseMasters(GameRelease gameRelease)
        {
            return Implicits.Get(gameRelease).BaseMasters;
        }

        public IEnumerable<ILoadOrderListingGetter> GetLoadOrderListings(
            GameRelease gameRelease, DirectoryPath dataDirectory, bool throwOnMissingMasters = true)
        {
            // This must be the *combined* load order (implicit base masters + Creation Club + plugins.txt), which is
            // what the old LoadOrder.GetListings returned. PluginListings.LoadOrderListings only returns a subset,
            // which caused most plugins (and every NPC overhaul depending on non-base masters) to be treated as
            // unloadable, so nothing was analyzed.
            //
            // Mutagen's default overload reads the Creation Club list (Skyrim.ccc) from whatever install it finds in
            // the registry, not from the game folder that owns *this* data directory. Stock Game / Wabbajack setups and
            // portable copies run the game from somewhere else, so the Creation Club plugins the game really loads were
            // missing from the load order - and every plugin that has one as a master (USSEP among them) was reported
            // as having missing masters and could not be loaded. Resolve the list from the right folder instead.
            var pluginsPath = GetPluginsFilePath(gameRelease, dataDirectory);
            if (pluginsPath is null)
                // No LocalAppData (e.g. an unusual Proton setup): nothing better to go on than Mutagen's own lookup.
                return LoadOrder.GetLoadOrderListings(gameRelease, dataDirectory, throwOnMissingMasters);
            return LoadOrder.GetLoadOrderListings(
                gameRelease, pluginsPath.Value, FindCreationClubListings(gameRelease, dataDirectory), dataDirectory,
                throwOnMissingMasters);
        }

        public LoadOrderSources GetLoadOrderSources(GameRelease gameRelease, DirectoryPath dataDirectory)
        {
            return new LoadOrderSources(
                GetPluginsFilePath(gameRelease, dataDirectory)?.Path,
                FindCreationClubListings(gameRelease, dataDirectory)?.Path);
        }

        private FilePath? GetPluginsFilePath(GameRelease gameRelease, DirectoryPath dataDirectory)
        {
            if (pluginsFilePathOverride is not null)
                return new FilePath(pluginsFilePathOverride);
            return new PluginListingsPathProvider(new DataDirectoryInjection(dataDirectory)).Get(gameRelease);
        }

        // The game reads its Creation Club list from the folder it is started from, which is the parent of the data
        // directory. The registry-located install is only a fallback, for a data directory that has no list next to it.
        // A missing list is fine (Mutagen then loads no Creation Club plugins beyond plugins.txt), so return null
        // rather than a path that doesn't exist.
        private static FilePath? FindCreationClubListings(GameRelease gameRelease, DirectoryPath dataDirectory)
        {
            var category = gameRelease.ToCategory();
            if (category is not (GameCategory.Skyrim or GameCategory.Fallout4))
                return null;
            var fileName = $"{category}.ccc";
            var candidates = new List<string>();
            if (dataDirectory.Directory is DirectoryPath gameFolder)
                candidates.Add(Path.Combine(gameFolder.Path, fileName));
            if (GameLocations.TryGetGameFolder(gameRelease, out var locatedFolder))
                candidates.Add(Path.Combine(locatedFolder.Path, fileName));
            var found = candidates.FirstOrDefault(File.Exists);
            return found is not null ? new FilePath(found) : (FilePath?)null;
        }
    }
}
