using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Focus.Apps.EasyNpc.Profiles
{
    /// <summary>
    /// Matches mod names to mugshot pack folder names when they don't match character-for-character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mugshot pack folder is named after the mod as it appears on Nexus ("Bijin AIO"). The name the app knows a mod
    /// by is whatever the mod manager recorded, which frequently isn't that: Vortex stores mods under their downloaded
    /// archive name ("Bijin_AIO-11395-1-0-1-1543256230"), and users rename folders freely. When the two don't match
    /// exactly, the gallery falls back to a grey silhouette even though the pack is installed - the "all my mods load
    /// but no mugshots appear" report.
    /// </para>
    /// <para>
    /// Matching is deliberately exact-after-normalization rather than fuzzy. Showing one mod's face under another
    /// mod's name is worse than showing no face at all, so the rules here only remove noise that is unambiguously not
    /// part of a mod's name: the Nexus id and version tail a mod manager appends, separator characters, and a trailing
    /// version number. Nothing is dropped that could distinguish two real mods (notably "AIO", which does).
    /// </para>
    /// </remarks>
    public static class ModNameMatcher
    {
        // "Bijin_AIO-11395-1-0-1-1543256230" -> the "-11395-1-0-1-1543256230" tail. The first number is a Nexus mod id
        // (3+ digits), and everything after it is version/timestamp segments a mod manager appended.
        private static readonly Regex InstallSuffixRegex =
            new(@"[-_]\d{3,}(?:[-_]\d+)*$", RegexOptions.Compiled);

        // A trailing version, with or without a "v": "Fresh Faces 1 2 1", "Bijin NPCs v3".
        private static readonly Regex TrailingVersionRegex =
            new(@"\s+v?\d+(?:\s+\d+)*$", RegexOptions.Compiled);

        /// <summary>
        /// Reduces a mod or mugshot folder name to a comparable form. Returns an empty string for names that carry no
        /// letters or digits, which must never be treated as a match.
        /// </summary>
        public static string Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            var working = InstallSuffixRegex.Replace(name.Trim(), string.Empty);
            var builder = new StringBuilder(working.Length);
            foreach (var c in working)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (c == '\'' || c == '’')
                    // Apostrophes vanish rather than splitting a word: mugshot packs and mod folders disagree about
                    // them constantly ("Pandorable's NPCs" vs "Pandorables_NPCs"), and "pandorable s" would match
                    // neither.
                    continue;
                else
                    // Every other separator collapses to a space, so "Bijin_AIO", "Bijin-AIO" and "Bijin AIO" agree.
                    builder.Append(' ');
            }
            var collapsed = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            collapsed = TrailingVersionRegex.Replace(collapsed, string.Empty);
            return collapsed;
        }

        /// <summary>
        /// Finds the single mugshot folder matching any of a mod's names, or null when there is no match or the match
        /// would be ambiguous.
        /// </summary>
        /// <param name="availableMugshotModNames">Folder names that have a mugshot for the NPC being looked at.</param>
        /// <param name="modNames">Every name the mod is known by (its own, plus its components').</param>
        public static string? FindMatch(IEnumerable<string> availableMugshotModNames, IEnumerable<string> modNames)
        {
            var normalizedModNames = modNames
                .Select(Normalize)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            if (normalizedModNames.Count == 0)
                return null;
            var matches = availableMugshotModNames
                .Where(x => normalizedModNames.Contains(Normalize(x)))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(2)
                .ToList();
            // Two different folders normalizing to the same name means we can't tell which one the user meant. Falling
            // back to the silhouette is the honest answer; guessing would put the wrong face on the card.
            return matches.Count == 1 ? matches[0] : null;
        }
    }
}
