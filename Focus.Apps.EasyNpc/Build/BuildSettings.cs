using Focus.Apps.EasyNpc.Profiles;
using System.Collections.Generic;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build
{
    /// <summary>
    /// Which NPCs a build writes into the merged plugin.
    /// </summary>
    public enum NpcInclusion
    {
        /// <summary>Every NPC in the profile. The safe default, and what every previous version did.</summary>
        All,

        /// <summary>
        /// Only NPCs whose appearance you actually chose (their Default or Face plugin isn't simply the winning
        /// override), plus template-based NPCs, which the merge repairs whether or not you changed them.
        /// </summary>
        CustomizedOnly,
    }

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
        // EXPERIMENTAL. Restricts the merge to the NPCs you actually customized, for a much smaller patch with far
        // fewer masters. Off by default: a full merge is what makes EasyNPC's conflict resolution complete.
        public NpcInclusion NpcInclusion { get; init; } = NpcInclusion.All;
        public string OutputDirectory { get; init; }
        public string OutputModName { get; init; }
        public Profile Profile { get; init; }

        /// <summary>
        /// The NPCs this build will actually write. Every stage of the pipeline must read this rather than
        /// <see cref="Profile"/> directly, otherwise a reduced build would still copy FaceGen (or write an RSV
        /// exclusion) for NPCs that never made it into the plugin.
        /// </summary>
        public IEnumerable<INpc> IncludedNpcs => NpcInclusion switch
        {
            NpcInclusion.CustomizedOnly => Profile.Npcs.Where(x => x.IsCustomized || x.IsTemplated),
            _ => Profile.Npcs,
        };

        public int IncludedNpcCount => NpcInclusion == NpcInclusion.All ? Profile.Count : IncludedNpcs.Count();
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