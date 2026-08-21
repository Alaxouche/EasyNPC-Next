using Autofac;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Modules;
using Serilog.Events;
using System;
using System.Collections.Generic;

namespace Focus.Apps.EasyNpc.Main
{
    static class AppContainer
    {
        // The value CommandLineOptions falls back to when "-g" isn't specified. An explicit "-g" always wins; this
        // default doesn't, so the saved edition (e.g. the GOG release) can take over.
        private const string DefaultGameName = "SkyrimSE";

        public static IContainer Build(CommandLineOptions options, StartupInfo startupInfo)
        {
            var builder = new ContainerBuilder();
            builder
                .RegisterModule(new LoggingModule
                {
                    Level = options.DebugMode ? LogEventLevel.Debug : LogEventLevel.Information,
                    LogFileName = ProgramData.LogFileName,
                })
                .RegisterModule<SystemModule>()
                .RegisterModule<ConfigurationModule>()
                .RegisterModule<MessagingModule>()
                .RegisterModule(GetModManagerModule(options, startupInfo))
                .RegisterModule(new MutagenModule
                {
                    BlacklistedPluginNames = options.PostBuild ?
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            FileStructure.MergeFileName,
                        },
                    // Command line first (mod managers often pass "-p"), then the saved setting, then auto-detection.
                    // The setting is what lets a user whose install can't be auto-detected - Linux/Proton, a portable
                    // copy, a store we don't recognize - point the app at the game themselves instead of being stuck
                    // on the "game not found" dialog.
                    DataDirectory = !string.IsNullOrEmpty(options.GamePath) ?
                        options.GamePath : NullIfEmpty(Settings.Default.GameDataDirectory),
                    // Same precedence for the edition: "-g" first, then the saved setting, then the default. The
                    // edition decides which plugins.txt is read, so a GOG install read as the Steam edition silently
                    // loads the wrong load order.
                    GameId = FirstNonEmpty(options.GameName, Settings.Default.GameRelease, DefaultGameName),
                })
                .RegisterModule(new ProfilesModule
                {
                    AutosavePath = ProgramData.ProfileLogFileName,
                })
                .RegisterModule<BuildModule>()
                .RegisterModule<PostBuildModule>()
                .RegisterModule<MaintenanceModule>()
                .RegisterModule<MainModule>();
            return builder.Build();
        }

        private static Module GetModManagerModule(CommandLineOptions options, StartupInfo startupInfo)
        {
            // Vortex manifest is a command-line option, so it automatically overrides detection-based mechanisms.
            if (!string.IsNullOrEmpty(options.VortexManifest))
                return new VortexModule { BootstrapFilePath = options.VortexManifest };
            return startupInfo.Launcher switch
            {
                ModManager.ModOrganizer => new ModOrganizerModule
                {
                    ExecutablePath = !string.IsNullOrEmpty(options.ModOrganizerExecutablePath) ?
                                           options.ModOrganizerExecutablePath : startupInfo.ParentProcessPath,
                },
                _ => new UnknownModManagerModule(),
            };
        }

        // MutagenModule treats null as "detect automatically"; an empty string would be taken as a real (invalid) path.
        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return Array.Find(values, x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }
    }
}
