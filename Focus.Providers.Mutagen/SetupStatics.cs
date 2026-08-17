using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using System.Collections.Generic;

namespace Focus.Providers.Mutagen
{
    public interface ISetupStatics
    {
        public IReadOnlyCollection<ModKey> GetBaseMasters(GameRelease gameRelease);
        public IEnumerable<ILoadOrderListingGetter> GetLoadOrderListings(
            GameRelease gameRelease, DirectoryPath dataDirectory, bool throwOnMissingMasters = true);
    }

    public class SetupStatics : ISetupStatics
    {
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
            return LoadOrder.GetLoadOrderListings(gameRelease, dataDirectory, throwOnMissingMasters);
        }
    }
}
