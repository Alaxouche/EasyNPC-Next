using Mutagen.Bethesda;

namespace Focus.Providers.Mutagen
{
    public class GameSelection
    {
        public string GameName { get; private init; }
        public GameRelease GameRelease { get; private init; }

        public GameSelection(GameRelease gameRelease)
        {
            GameRelease = gameRelease;
            GameName = GetGameName(gameRelease);
        }

        protected static string GetGameName(GameRelease gameRelease) => gameRelease switch
        {
            GameRelease.EnderalLE => "Enderal Legendary Edition",
            GameRelease.EnderalSE => "Enderal Special Edition",
            GameRelease.Fallout4 => "Fallout 4",
            GameRelease.Fallout4VR => "Fallout 4 VR",
            GameRelease.Oblivion => "Oblivion",
            GameRelease.OblivionRE => "Oblivion Remastered",
            GameRelease.SkyrimLE => "Skyrim Legendary Edition",
            GameRelease.SkyrimSE => "Skyrim Special Edition",
            // A separate GameRelease, not cosmetic: it's what makes Mutagen read the GOG edition's own plugins.txt.
            GameRelease.SkyrimSEGog => "Skyrim Special Edition (GOG)",
            GameRelease.SkyrimVR => "Skyrim VR",
            GameRelease.Starfield => "Starfield",
            _ => "Unknown game"
        };
    }
}
