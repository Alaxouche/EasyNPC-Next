using Mutagen.Bethesda;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Focus.Providers.Mutagen.Tests
{
    // Uses a real temporary folder: the Creation Club list is resolved by Mutagen through the physical file system.
    public class SetupStaticsTests : IDisposable
    {
        private readonly string gameFolder;
        private readonly string dataFolder;

        public SetupStaticsTests()
        {
            gameFolder = Path.Combine(Path.GetTempPath(), "EasyNpcSetupStatics_" + Guid.NewGuid().ToString("N"));
            dataFolder = Path.Combine(gameFolder, "Data");
            Directory.CreateDirectory(dataFolder);
        }

        public void Dispose()
        {
            try { Directory.Delete(gameFolder, true); } catch (IOException) { }
        }

        [Fact]
        public void GetLoadOrderListings_ReadsCreationClubListFromTheDataFoldersOwnGameFolder()
        {
            // A Stock Game / Wabbajack copy runs the game from its own folder, with its own Skyrim.ccc. Reading the
            // list from the registry-located install instead dropped every Creation Club plugin that install doesn't
            // list, and with them every plugin that has one as a master (e.g. USSEP -> "missing masters").
            const string ccPlugin = "ccZZZSSE999-OnlyInThisInstall.esl";
            File.WriteAllText(Path.Combine(gameFolder, "Skyrim.ccc"), ccPlugin + "\r\n");
            File.WriteAllBytes(Path.Combine(dataFolder, ccPlugin), Array.Empty<byte>());

            var listings = new SetupStatics().GetLoadOrderListings(GameRelease.SkyrimSE, dataFolder, false);

            Assert.Contains(listings, x => x.ModKey.FileName.String.Equals(ccPlugin, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void GetLoadOrderListings_WithoutCreationClubListNextToData_DoesNotThrow()
        {
            var listings = new SetupStatics().GetLoadOrderListings(GameRelease.SkyrimSE, dataFolder, false).ToList();

            Assert.DoesNotContain(listings, x => x.ModKey.FileName.String.StartsWith("ccZZZ"));
        }
    }
}
