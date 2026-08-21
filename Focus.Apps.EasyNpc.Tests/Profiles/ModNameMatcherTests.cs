using Focus.Apps.EasyNpc.Profiles;
using System;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Profiles
{
    public class ModNameMatcherTests
    {
        [Theory]
        // A mod manager's install suffix (Nexus id + version segments) is not part of the mod's name. This is the
        // Vortex staging-folder shape that made every mugshot card fall back to a silhouette.
        [InlineData("Bijin_AIO-11395-1-0-1-1543256230", "bijin aio")]
        [InlineData("Bijin AIO-11395", "bijin aio")]
        // Separator styles must all agree.
        [InlineData("Bijin_AIO", "bijin aio")]
        [InlineData("Bijin-AIO", "bijin aio")]
        [InlineData("  Bijin   AIO  ", "bijin aio")]
        // Apostrophes disappear instead of splitting the word - packs and mod folders disagree about them constantly.
        [InlineData("Pandorable's NPCs", "pandorables npcs")]
        // A trailing version, with or without a "v".
        [InlineData("Fresh Faces SSE 1 2 1", "fresh faces sse")]
        [InlineData("Bijin NPCs v3", "bijin npcs")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("---", "")]
        public void Normalize_ReducesNameToComparableForm(string? input, string expected)
        {
            Assert.Equal(expected, ModNameMatcher.Normalize(input));
        }

        [Fact]
        public void Normalize_KeepsTokensThatDistinguishRealMods()
        {
            // "AIO" (all-in-one) marks a genuinely different mod from the base one, so it must survive normalization.
            Assert.NotEqual(ModNameMatcher.Normalize("Bijin AIO"), ModNameMatcher.Normalize("Bijin"));
            // A leading number is part of the name, not a version.
            Assert.NotEqual(ModNameMatcher.Normalize("2K Faces"), ModNameMatcher.Normalize("Faces"));
        }

        [Fact]
        public void FindMatch_MatchesModFolderToMugshotFolderAcrossNamingStyles()
        {
            var mugshotFolders = new[] { "Pandorable's NPCs", "Bijin AIO", "Fresh Faces - SSE" };

            Assert.Equal(
                "Bijin AIO", ModNameMatcher.FindMatch(mugshotFolders, new[] { "Bijin_AIO-11395-1-0-1-1543256230" }));
            Assert.Equal("Pandorable's NPCs", ModNameMatcher.FindMatch(mugshotFolders, new[] { "Pandorables_NPCs" }));
        }

        [Fact]
        public void FindMatch_UsesAnyOfTheModsNames()
        {
            var mugshotFolders = new[] { "Bijin AIO" };

            // A mod's own name may not match while one of its components' does (or the other way round).
            Assert.Equal(
                "Bijin AIO", ModNameMatcher.FindMatch(mugshotFolders, new[] { "Something Else", "Bijin-AIO" }));
        }

        [Fact]
        public void FindMatch_ReturnsNullWhenNothingMatches()
        {
            Assert.Null(ModNameMatcher.FindMatch(new[] { "Bijin AIO" }, new[] { "Pandorable's NPCs" }));
        }

        [Fact]
        public void FindMatch_ReturnsNullWhenAmbiguous()
        {
            // Two folders that normalize the same way: we can't tell which one the user meant, and putting the wrong
            // mod's face on a card is worse than showing no face at all.
            var mugshotFolders = new[] { "Bijin AIO", "Bijin_AIO" };

            Assert.Null(ModNameMatcher.FindMatch(mugshotFolders, new[] { "Bijin-AIO" }));
        }

        [Fact]
        public void FindMatch_IgnoresNamesThatNormalizeToNothing()
        {
            // An empty normalized name must never match an empty folder name; that would match everything.
            Assert.Null(ModNameMatcher.FindMatch(new[] { "---" }, new[] { "___" }));
        }
    }
}
