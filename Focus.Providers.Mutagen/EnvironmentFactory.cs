using Focus.Environment;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Linq;

namespace Focus.Providers.Mutagen
{
    public interface IEnvironmentFactory
    {
        // This signature is temporary until more fine-grained interfaces can be defined and used everywhere.
        // IReadOnlyGameEnvironment does exist, but some code still requires the full IGameEnvironment.
        IGameEnvironment<ISkyrimMod, ISkyrimModGetter> CreateEnvironment();
    }

    public class EnvironmentFactory : IEnvironmentFactory
    {
        private readonly GameSelection game;
        private readonly IGameSetup setup;

        public EnvironmentFactory(IGameSetup setup, GameSelection game)
        {
            this.game = game;
            this.setup = setup;
        }

        public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> CreateEnvironment()
        {
            if (!setup.IsConfirmed)
                throw new InvalidOperationException(
                    "Attempted to create the game environment before settings were confirmed.");
            var loadOrderKeys = setup.AvailablePlugins
                .Where(p => setup.LoadOrderGraph.IsEnabled(p.FileName) && setup.LoadOrderGraph.CanLoad(p.FileName))
                .Select(p => ModKey.FromNameAndExtension(p.FileName))
                .ToArray();
            return GameEnvironmentBuilder<ISkyrimMod, ISkyrimModGetter>
                .Create(game.GameRelease)
                .WithTargetDataFolder(setup.DataDirectory)
                .WithLoadOrder(loadOrderKeys)
                .Build();
        }
    }
}
