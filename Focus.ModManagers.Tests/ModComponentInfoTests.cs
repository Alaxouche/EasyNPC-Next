using System.Collections.Generic;
using Xunit;

namespace Focus.ModManagers.Tests
{
    /// <summary>
    /// Components are compared by identity, not by reference.
    /// </summary>
    /// <remarks>
    /// The post-build report compares a component it built itself (the synthetic "Vanilla" component) against one that
    /// came out of the mod repository. With reference equality those could never be equal, so correct results were
    /// silently classified as conflicts.
    /// </remarks>
    public class ModComponentInfoTests
    {
        private static readonly ModLocatorKey Key = new("1234", "Some Mod");

        [Fact]
        public void SeparateInstancesWithTheSameIdentityAreEqual()
        {
            var a = new ModComponentInfo(Key, "component", "Some Mod", @"C:\mods\Some Mod");
            var b = new ModComponentInfo(new ModLocatorKey("1234", "Some Mod"), "component", "Some Mod", @"C:\mods\Some Mod");

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void EqualityIgnoresCaseAndTheEnabledFlag()
        {
            var a = new ModComponentInfo(Key, "component", "Some Mod", @"C:\mods\Some Mod", isEnabled: true);
            var b = new ModComponentInfo(Key, "COMPONENT", "Some Mod", @"c:\MODS\some mod", isEnabled: false);

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ComponentsInDifferentDirectoriesAreNotEqual()
        {
            // Two components of the same mod - they must stay distinct, or per-component file lookups would collapse.
            var a = new ModComponentInfo(Key, "main", "Some Mod", @"C:\mods\Some Mod");
            var b = new ModComponentInfo(Key, "patch", "Some Mod", @"C:\mods\Some Mod Patch");

            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }

        [Fact]
        public void ComponentsOfDifferentModsAreNotEqual()
        {
            var a = new ModComponentInfo(Key, "component", "Some Mod", @"C:\mods\Some Mod");
            var b = new ModComponentInfo(
                new ModLocatorKey("5678", "Other Mod"), "component", "Other Mod", @"C:\mods\Some Mod");

            Assert.NotEqual(a, b);
        }

        [Fact]
        public void NullComparisonsAreHandled()
        {
            var a = new ModComponentInfo(Key, "component", "Some Mod", @"C:\mods\Some Mod");
            ModComponentInfo? nothing = null;

            Assert.False(a == nothing);
            Assert.True(a != nothing);
            Assert.False(a.Equals(null));
            Assert.True(nothing == null);
        }

        [Fact]
        public void CanBeUsedAsASetMember()
        {
            // FaceGenCopyTask puts components in a HashSet and looks up search results by membership, so value
            // equality has to hold there too.
            var set = new HashSet<ModComponentInfo>
            {
                new(Key, "component", "Some Mod", @"C:\mods\Some Mod"),
            };

            Assert.Contains(new ModComponentInfo(Key, "component", "Some Mod", @"C:\mods\Some Mod"), set);
        }
    }
}
