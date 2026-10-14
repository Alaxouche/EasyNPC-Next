using System;
using System.Collections.Generic;

namespace Focus.Apps.EasyNpc.Configuration
{
    public interface IAppSettings
    {
        string BuildReportPath { get; }
        IEnumerable<BuildWarningSuppression> BuildWarningWhitelist { get; }
        string DefaultModRootDirectory { get; }
        // Fetches missing mugshots from the online NPC Face Finder API. On by default.
        bool EnableOnlineMugshots { get; }
        // Explicit game data folder (empty = auto-detect). The "-p" command line option takes priority over it.
        string GameDataDirectory { get; }
        // Mutagen GameRelease name, e.g. "SkyrimSE" or "SkyrimSEGog" (empty = auto-detect).
        string GameRelease { get; }
        // Experimental: includes child NPCs in the profile so child overhauls can be merged. Off by default.
        bool IncludeChildNpcs { get; }
        // UI language: "System", or a culture name such as "en", "fr", "zh-Hans".
        string Language { get; }
        // Plugin header version written into generated plugins: "Auto", "1.70" or "1.71".
        string PluginHeaderVersion { get; }
        // Explicit plugins.txt to read the load order from (empty = the game's own under %LocalAppData%).
        string PluginsFilePath { get; }
        IEnumerable<MugshotRedirect> MugshotRedirects { get; }
        string MugshotsDirectory { get; }
        IEnumerable<IRecordKey> RaceTransformationKeys { get; }
        string StaticAssetsPath { get; }
        // "System", "Light" or "Dark". Controls the app color theme.
        string Theme { get; }
        // Hard-link merged textures/meshes to the source files when on the same drive, instead of copying them.
        bool UseHardLinks { get; }
        bool UseModManagerForModDirectory { get; }
    }

    public interface IMutableAppSettings
    {
        IReadOnlyList<BuildWarningSuppression> BuildWarningWhitelist { get; set; }
        string DefaultModRootDirectory { get; set; }
        bool EnableOnlineMugshots { get; set; }
        string GameDataDirectory { get; set; }
        string GameRelease { get; set; }
        bool IncludeChildNpcs { get; set; }
        string Language { get; set; }
        IReadOnlyList<MugshotRedirect> MugshotRedirects { get; set; }
        string MugshotsDirectory { get; set; }
        string PluginHeaderVersion { get; set; }
        string PluginsFilePath { get; set; }
        string Theme { get; set; }
        bool UseHardLinks { get; set; }
        bool UseModManagerForModDirectory { get; set; }

        void Save();
    }

    public interface IObservableAppSettings : IAppSettings
    {
        IObservable<IReadOnlyList<BuildWarningSuppression>> BuildWarningWhitelistObservable { get; }
        IObservable<string> DefaultModRootDirectoryObservable { get; }
        IObservable<bool> EnableOnlineMugshotsObservable { get; }
        IObservable<string> GameDataDirectoryObservable { get; }
        IObservable<string> GameReleaseObservable { get; }
        IObservable<bool> IncludeChildNpcsObservable { get; }
        IObservable<IReadOnlyList<MugshotRedirect>> MugshotRedirectsObservable { get; }
        IObservable<string> MugshotsDirectoryObservable { get; }
        IObservable<string> ThemeObservable { get; }
        IObservable<bool> UseModManagerForModDirectoryObservable { get; }
    }
}