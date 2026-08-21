using CommandLine;

namespace Focus.Apps.EasyNpc
{
    class CommandLineOptions
    {
        [Option('d', "debug")]
        public bool DebugMode { get; set; }

        [Option('i', "force-intro")]
        public bool ForceIntro { get; set; }

        // Empty means "not specified", so the saved game edition can take over. Without that distinction an explicit
        // "-g SkyrimSE" would be indistinguishable from no argument at all, and the two need different behavior.
        [Option('g', "game")]
        public string GameName { get; set; } = string.Empty;

        [Option('p', "game-path")]
        public string? GamePath { get; set; } = null;

        [Option("mo2-exe")]
        public string? ModOrganizerExecutablePath { get; set; } = null;

        [Option('z', "post-build")]
        public bool PostBuild { get; set; }

        [Option('r', "report-path")]
        public string? ReportPath { get; set; }

        [Option("vortex-manifest")]
        public string? VortexManifest { get; set; }
    }
}