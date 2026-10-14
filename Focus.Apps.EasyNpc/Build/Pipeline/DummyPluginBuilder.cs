using Focus.Apps.EasyNpc.Build;
using Focus.Providers.Mutagen;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System.IO;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    // TODO: This doesn't quite seem to fit in any of our upstream libs. However, it's not really EasyNPC specific,
    // either. Find a better home for it.

    public interface IDummyPluginBuilder
    {
        void CreateDummyPlugin(string path);
    }

    public class DummyPluginBuilder : IDummyPluginBuilder
    {
        private readonly GameSelection game;
        private readonly IPluginHeaderVersionProvider headerVersionProvider;

        public DummyPluginBuilder(GameSelection game, IPluginHeaderVersionProvider headerVersionProvider)
        {
            this.game = game;
            this.headerVersionProvider = headerVersionProvider;
        }

        public void CreateDummyPlugin(string path)
        {
            var modKey = ModKey.FromNameAndExtension(Path.GetFileName(path));
            // These are ESL-flagged, and a 1.71 ESL is exactly what crashes an older game version.
            var dummyMod = new SkyrimMod(
                modKey, game.GameRelease.ToSkyrimRelease(), headerVersionProvider.HeaderVersion);
            dummyMod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small;
            dummyMod.BeginWrite.ToPath(path).WithNoLoadOrder().Write();
        }
    }
}
