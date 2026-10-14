using Focus.Apps.EasyNpc.Configuration;
using Focus.Providers.Mutagen;
using Serilog;
using System;

namespace Focus.Apps.EasyNpc.Build
{
    public interface IPluginHeaderVersionProvider
    {
        // Header version for every plugin the build writes, or null for Mutagen's default.
        float? HeaderVersion { get; }
    }

    public class PluginHeaderVersionProvider : IPluginHeaderVersionProvider
    {
        public float? HeaderVersion => headerVersion.Value;

        private readonly Lazy<float?> headerVersion;

        public PluginHeaderVersionProvider(
            IAppSettings appSettings, GameSelection game, IGameSettings gameSettings, ILogger log)
        {
            headerVersion = new(() =>
            {
                var version = PluginHeaderVersions.Resolve(
                    appSettings.PluginHeaderVersion, game.GameRelease, gameSettings.DataDirectory, out var reason);
                log.Information(
                    "Generated plugins will use header version {headerVersion} ({reason}).",
                    version?.ToString("0.00") ?? "default", reason);
                return version;
            });
        }
    }
}
