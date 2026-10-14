using System.Collections.Generic;

namespace Focus.Apps.EasyNpc.Build
{
    // Missing assets grouped by the mod whose meshes ask for them. A hundred bare texture paths read as a hundred
    // problems; "Men of Skyrim Refined: 12 textures its meshes reference but it doesn't ship" reads as one.
    public class MissingAssetGroup
    {
        public string ModName { get; init; } = string.Empty;
        public int Count { get; init; }
        public IReadOnlyList<string> Examples { get; init; } = new List<string>();
    }
}
