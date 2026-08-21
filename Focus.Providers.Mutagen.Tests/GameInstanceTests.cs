using Moq;
using Mutagen.Bethesda;
using Noggog;
using Xunit;

namespace Focus.Providers.Mutagen.Tests
{
    public class GameInstanceTests
    {
        delegate void TryGetDataFolderCallback(GameRelease gameRelease, out DirectoryPath path);

        private const string SteamDataPath = @"C:\Steam\steamapps\common\Skyrim Special Edition\Data";
        private const string GogDataPath = @"C:\GOG Galaxy\Games\Skyrim Special Edition\Data";

        private readonly IGameLocations gameLocations;
        private readonly Mock<IGameLocations> gameLocationsMock;

        public GameInstanceTests()
        {
            gameLocationsMock = new Mock<IGameLocations>();
            gameLocations = gameLocationsMock.Object;
        }

        [Fact]
        public void WhenInvalidGameId_ThrowsUnsupportedGame()
        {
            Assert.Throws<UnsupportedGameException>(() => GameInstance.FromGameId(gameLocations, "invalid"));
        }

        [Fact]
        public void WhenValidGameId_AndNoInstallDetected_AndDirectoryNotSpecified_ThrowsMissingGameData()
        {
            Assert.Throws<MissingGameDataException>(() => GameInstance.FromGameId(gameLocations, "SkyrimSE"));
        }

        [Fact]
        public void WhenValidGameId_AndNoInstallDetected_AndDirectorySpecified_UsesCustomDirectory()
        {
            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE", @"C:\custom\path");

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
            Assert.Equal(@"C:\custom\path", game.DataDirectory);
        }

        [Fact]
        public void WhenValidGameId_AndInstallDetected_AndDirectoryNotSpecified_UsesDetectedDirectory()
        {
            SetUpLocation(GameRelease.SkyrimSE, @"C:\path\to\game");

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE");

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
            Assert.Equal(@"C:\path\to\game", game.DataDirectory);
        }

        [Fact]
        public void WhenValidGameId_AndInstallDetected_AndDirectorySpecified_UsesCustomDirectory()
        {
            SetUpLocation(GameRelease.SkyrimSE, @"C:\path\to\game");

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE", @"C:\custom\path");

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
            Assert.Equal(@"C:\custom\path", game.DataDirectory);
        }

        // Which GameRelease an install resolves to isn't cosmetic: each edition keeps its own plugins.txt and
        // loadorder.txt under %LocalAppData%, so a GOG install read as the Steam edition loads the Steam load order.
        // That's the "it always defaults to the plugins.txt for the Steam Skyrim" report, which persisted even when
        // the user passed the correct -p path.

        [Fact]
        public void WhenDirectoryIsAGogInstall_ResolvesToTheGogRelease()
        {
            SetUpLocation(GameRelease.SkyrimSE, SteamDataPath);
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE", GogDataPath);

            Assert.Equal(GameRelease.SkyrimSEGog, game.GameRelease);
            Assert.Equal(GogDataPath, game.DataDirectory);
        }

        [Fact]
        public void WhenDirectoryIsTheGogGameFolder_ResolvesToTheGogRelease()
        {
            // Users routinely point "-p" at the game folder rather than its Data subfolder.
            SetUpLocation(GameRelease.SkyrimSE, SteamDataPath);
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(
                gameLocations, "SkyrimSE", @"C:\GOG Galaxy\Games\Skyrim Special Edition");

            Assert.Equal(GameRelease.SkyrimSEGog, game.GameRelease);
        }

        [Fact]
        public void WhenDirectoryIsASteamInstall_KeepsTheSteamRelease()
        {
            SetUpLocation(GameRelease.SkyrimSE, SteamDataPath);
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE", SteamDataPath);

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
        }

        [Fact]
        public void WhenDirectoryMatchesNoKnownInstall_KeepsTheRequestedRelease()
        {
            // A portable copy or a Linux/Proton prefix matches no detected install; the requested edition stands.
            SetUpLocation(GameRelease.SkyrimSE, SteamDataPath);
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE", @"D:\Games\Skyrim\Data");

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
            Assert.Equal(@"D:\Games\Skyrim\Data", game.DataDirectory);
        }

        [Fact]
        public void WhenGogGameIdIsExplicit_IsHonoredWithoutAnyDetection()
        {
            var game = GameInstance.FromGameId(gameLocations, "SkyrimSEGog", GogDataPath);

            Assert.Equal(GameRelease.SkyrimSEGog, game.GameRelease);
        }

        [Fact]
        public void WhenNoDirectory_AndOnlyGogInstalled_FallsBackToTheGogRelease()
        {
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE");

            Assert.Equal(GameRelease.SkyrimSEGog, game.GameRelease);
            Assert.Equal(GogDataPath, game.DataDirectory);
        }

        [Fact]
        public void WhenNoDirectory_AndBothInstalled_PrefersTheRequestedRelease()
        {
            SetUpLocation(GameRelease.SkyrimSE, SteamDataPath);
            SetUpLocation(GameRelease.SkyrimSEGog, GogDataPath);

            var game = GameInstance.FromGameId(gameLocations, "SkyrimSE");

            Assert.Equal(GameRelease.SkyrimSE, game.GameRelease);
            Assert.Equal(SteamDataPath, game.DataDirectory);
        }

        private void SetUpLocation(GameRelease release, string path)
        {
            gameLocationsMock.Setup(x => x.TryGetDataFolder(release, out It.Ref<DirectoryPath>.IsAny))
                .Callback(new TryGetDataFolderCallback((GameRelease gameRelease, out DirectoryPath outPath) =>
                {
                    outPath = path;
                }))
                .Returns(true);
        }
    }
}
