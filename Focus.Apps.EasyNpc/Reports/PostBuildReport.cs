using Focus.Apps.EasyNpc.GameData.Files;
using Focus.ModManagers;
using System.Collections.Generic;

namespace Focus.Apps.EasyNpc.Reports
{
    public enum PluginState { Missing, Enabled, Disabled, Unloadable }

    public class MergeArchiveInfo
    {
        public string? ArchiveName { get; init; }
        public PluginState ArchiveState => RequiresDummyPlugin ? DummyPluginState : PluginState.Enabled;
        public string? DummyPluginName { get; init; }
        public PluginState DummyPluginState { get; init; }
        public bool HasMissingArchive => string.IsNullOrEmpty(ArchiveName);
        public bool HasPluginError => RequiresDummyPlugin &&
            (string.IsNullOrEmpty(DummyPluginName) || DummyPluginState != PluginState.Enabled);
        public bool IsReadable { get; init; }
        public bool RequiresDummyPlugin { get; init; }
    }

    public class AssetSource
    {
        public string? ArchiveName { get; init; }
        public ModComponentInfo ModComponent { get; init; } = ModComponentInfo.Invalid;
        public string RelativePath { get; init; } = string.Empty;
    }

    public class NpcConsistencyInfo : IRecordKey
    {
        public string BasePluginName { get; init; } = string.Empty;
        public string EditorId { get; init; } = string.Empty;
        public string? FaceGenArchivePath { get; init; }
        public string? FaceTintArchivePath { get; init; }
        public string? FaceGenLoosePath { get; init; }
        public string? FaceTintLoosePath { get; init; }
        public bool HasConsistentFaceTint { get; init; }
        public bool HasConsistentHeadParts { get; init; }
        public bool HasFaceGenArchive => !string.IsNullOrEmpty(FaceGenArchivePath);
        public bool HasFaceGenLoose => !string.IsNullOrEmpty(FaceGenLoosePath);
        // The merge shipped a FaceGen for this NPC, but a different mod's copy is the one the game will load (or the
        // merged copy can't be found at all). This is the case the user can act on: change mod priority, disable the
        // overriding mod, or extract the merged files loose.
        public bool HasFaceGenOverrideConflict { get; init; }
        public bool HasFaceTintArchive => !string.IsNullOrEmpty(FaceTintArchivePath);
        public bool HasFaceTintLoose => !string.IsNullOrEmpty(FaceTintLoosePath);
        // The merge left this NPC's face alone (its face plugin is also its default plugin, so no FaceGen was written),
        // and the FaceGen it inherits doesn't match its head parts. This condition exists with or without EasyNPC - it
        // comes from the source mods - so it's reported for information only, never as something to "fix" by
        // disabling mods.
        public bool HasInheritedFaceGenMismatch => !IsFaceGenProvidedByMerge && !HasConsistentHeadParts;
        // Everything about this NPC's FaceGen that the merge is actually responsible for.
        public bool HasMergeFaceGenIssue =>
            HasFaceGenOverrideConflict || (IsFaceGenProvidedByMerge && !HasConsistentHeadParts);
        // True when the build wrote a FaceGen (mesh/tint) for this NPC into the output mod.
        public bool IsFaceGenProvidedByMerge { get; init; }
        public string LocalFormIdHex { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public AssetSource? WinningFaceGenSource { get; init; }
        public AssetSource? WinningFaceTintSource { get; init; }
        public string WinningPluginName { get; init; } = string.Empty;
        public AssetSource? WinningPluginSource { get; init; }
    }

    public class PostBuildReport
    {
        public IReadOnlyList<ModComponentInfo> ActiveMergeComponents { get; init; } =
            new List<ModComponentInfo>().AsReadOnly();
        public IReadOnlyList<MergeArchiveInfo> Archives { get; init; } = new List<MergeArchiveInfo>().AsReadOnly();
        public IReadOnlyList<string> MainPluginMissingMasters { get; init; } = new List<string>().AsReadOnly();
        public string MainPluginName { get; init; } = FileStructure.MergeFileName;
        public PluginState MainPluginState { get; init; }
        public IReadOnlyList<NpcConsistencyInfo> Npcs { get; init; } = new List<NpcConsistencyInfo>().AsReadOnly();
    }
}
