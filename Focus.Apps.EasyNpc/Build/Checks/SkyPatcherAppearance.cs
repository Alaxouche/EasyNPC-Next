using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build.Checks
{
    // SkyPatcher edits records at runtime, after every plugin has loaded, so an NPC appearance replacer written as a
    // SkyPatcher INI silently wins over the face chosen here - and over the merged FaceGen, which no longer matches the
    // head it gets. The merge can't see these edits in any plugin, so they have to be found in the INIs themselves.
    public class SkyPatcherAppearance : IGlobalBuildCheck
    {
        // NPC patcher operations that change what the NPC looks like (SkyPatcher 6.4.1 NPC grammar).
        private static readonly HashSet<string> AppearanceOperations = new(StringComparer.OrdinalIgnoreCase)
        {
            "copyVisualStyle", "setRandomVisualStyle", "addRandomVisualStyle", "race", "skin",
        };
        private static readonly HashSet<string> TemplateFlagOperations = new(StringComparer.OrdinalIgnoreCase)
        {
            "setTemplateFlags", "removeTemplateFlags",
        };

        private readonly IFileSystem fs;
        private readonly IGameSettings gameSettings;
        private readonly IModRepository modRepository;

        public SkyPatcherAppearance(IFileSystem fs, IGameSettings gameSettings, IModRepository modRepository)
        {
            this.fs = fs;
            this.gameSettings = gameSettings;
            this.modRepository = modRepository;
        }

        public IEnumerable<BuildWarning> Run(Profile profile, BuildSettings settings)
        {
            var npcDirectory = fs.Path.Combine(gameSettings.DataDirectory, "SKSE", "Plugins", "SkyPatcher", "npc");
            if (!fs.Directory.Exists(npcDirectory))
                return Enumerable.Empty<BuildWarning>();
            var customizedNpcs = profile.Npcs.Where(x => !x.FaceOption.IsBaseGame).ToList();
            var byKey = customizedNpcs.ToLookup(x => NpcKey(x.BasePluginName, x.LocalFormIdHex));
            var byEditorId = customizedNpcs
                .Where(x => !string.IsNullOrEmpty(x.EditorId))
                .ToLookup(x => x.EditorId, StringComparer.OrdinalIgnoreCase);
            var warnings = new List<BuildWarning>();
            foreach (var path in fs.Directory.EnumerateFiles(npcDirectory, "*.ini", SearchOption.AllDirectories))
            {
                IEnumerable<string> lines;
                try
                {
                    lines = fs.File.ReadAllLines(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                var operations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var affected = new HashSet<INpc>();
                var hasGroupFilter = false;
                foreach (var line in lines)
                {
                    var lineOperations = GetAppearanceOperations(line).ToList();
                    if (lineOperations.Count == 0)
                        continue;
                    operations.UnionWith(lineOperations);
                    var targets = GetNpcTargets(line);
                    if (targets is null)
                        hasGroupFilter = true;
                    else
                        foreach (var target in targets)
                            affected.UnionWith(target.Contains('|') ? byKey[target] : byEditorId[target]);
                }
                if (operations.Count == 0)
                    continue;
                var relativePath = fs.Path.GetRelativePath(gameSettings.DataDirectory, path);
                var modNames = modRepository.SearchForFiles(relativePath, false)
                    .Select(x => x.ModComponent.Name)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                warnings.Add(new BuildWarning(
                    BuildWarningId.SkyPatcherAppearance,
                    WarningMessages.SkyPatcherAppearance(
                        relativePath, modNames, operations, affected.Select(x => x.DescriptiveLabel).ToList(),
                        hasGroupFilter)));
            }
            return warnings;
        }

        private static IEnumerable<string> GetAppearanceOperations(string line)
        {
            foreach (var (key, value) in GetSegments(line))
            {
                if (AppearanceOperations.Contains(key))
                    yield return key;
                else if (TemplateFlagOperations.Contains(key) &&
                    value.Split(',').Any(x => x.Trim().Equals("traits", StringComparison.OrdinalIgnoreCase)))
                    yield return $"{key}=traits";
            }
        }

        // The NPCs a line names with filterByNpcs, normalized to "plugin|localid" or an EditorID. Null when the line
        // selects NPCs some other way (faction, race, keyword...) or has no filter at all, which means every NPC.
        private static List<string>? GetNpcTargets(string line)
        {
            var filter = GetSegments(line)
                .FirstOrDefault(x => x.Key.Equals("filterByNpcs", StringComparison.OrdinalIgnoreCase));
            if (filter.Key is null)
                return null;
            return filter.Value
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Select(NormalizeTarget)
                .ToList();
        }

        private static string NormalizeTarget(string target)
        {
            var separator = target.IndexOf('|');
            if (separator < 0)
                return target;
            var plugin = target[..separator].Trim();
            var formId = target[(separator + 1)..].Trim();
            if (!uint.TryParse(formId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return target;
            // Full FormIDs carry a load order prefix: the top byte for a regular plugin, "FExxx" for a light one.
            var localId = formId.Length == 8 && formId.StartsWith("FE", StringComparison.OrdinalIgnoreCase) ?
                value & 0xFFF : value & 0xFFFFFF;
            return NpcKey(plugin, localId.ToString("X6"));
        }

        private static string NpcKey(string pluginName, string localFormIdHex)
        {
            return $"{pluginName.ToLowerInvariant()}|{localFormIdHex.ToUpperInvariant().PadLeft(6, '0')}";
        }

        private static IEnumerable<(string Key, string Value)> GetSegments(string line)
        {
            var content = line;
            var comment = content.IndexOf(';');
            if (comment >= 0)
                content = content[..comment];
            foreach (var segment in content.Split(':'))
            {
                var equals = segment.IndexOf('=');
                if (equals <= 0)
                    continue;
                yield return (segment[..equals].Trim(), segment[(equals + 1)..].Trim());
            }
        }
    }
}
