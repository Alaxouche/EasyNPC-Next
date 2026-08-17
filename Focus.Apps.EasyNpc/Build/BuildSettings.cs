using Focus.Apps.EasyNpc.Profiles;

namespace Focus.Apps.EasyNpc.Build
{
    public class BuildSettings
    {
        public bool EnableArchiving { get; init; } = true;
        public bool EnableDewiggify { get; init; }
        // Writes a SPID "..._DISTR.ini" tagging modded-face NPCs with RSVIgnore. Off by default.
        public bool GenerateRsvExclusions { get; init; } = false;
        // When archiving, leave FaceGen (facegeom/facetint) as loose files instead of packing it into the BSA.
        public bool KeepFaceGenOutsideArchive { get; init; } = false;
        // EXPERIMENTAL. Forward an NPC's custom race from the face plugin (e.g. Project ja-Kha'jay) instead of keeping
        // the default race. The race and its dependencies are duplicated into the merge so it does not become a master.
        public bool ForwardCustomRaces { get; init; } = false;
        // EXPERIMENTAL. Split the merged plugin into several so no single one exceeds this many NPCs (0 = no split).
        // Dodges "Too Many Masters". Loose builds only for now. Each split carries its own copy of the records it needs.
        public int MaxNpcsPerPlugin { get; init; } = 0;
        public string OutputDirectory { get; init; }
        public string OutputModName { get; init; }
        public Profile Profile { get; init; }
        // 0 disables the per-file timeout during texture path extraction.
        public int TextureExtractionTimeoutSec { get; init; } = 0;

        public BuildSettings(Profile profile, string outputDirectory, string outputModName)
        {
            Profile = profile;
            OutputDirectory = outputDirectory;
            OutputModName = outputModName;
        }
    }
}