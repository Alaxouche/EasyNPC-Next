using System.Collections.Generic;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build.Preview
{
    // Alerts that share a cause and a plugin: "MissingFaceGen from Karura's Ordinary People Refined - Extended.esp,
    // 124 NPCs" reads as one problem with one fix, where 124 separate lines read as 124 problems.
    public class WarningGroup
    {
        public bool CanOpenInProfile => HasPlugin && HasNpcs;
        public int Count => Warnings.Count;
        public bool HasNpcs => NpcKeys.Count > 0;
        public bool HasPlugin => !string.IsNullOrEmpty(PluginName);
        public BuildWarningId? Id { get; init; }
        public IReadOnlyList<IRecordKey> NpcKeys { get; init; } = new List<IRecordKey>();
        public string? PluginName { get; init; }
        public BuildWarningSeverity Severity { get; init; }
        public string Title => HasPlugin ? $"{IdName} - {PluginName}" : IdName;
        public IReadOnlyList<BuildWarning> Warnings { get; init; } = new List<BuildWarning>();

        private string IdName => Id?.ToString() ?? "Other";

        public bool Contains(BuildWarning warning)
        {
            return warning.Id == Id && string.Equals(warning.PluginName, PluginName);
        }

        public static IReadOnlyList<WarningGroup> FromWarnings(IEnumerable<BuildWarning> warnings)
        {
            return warnings
                .GroupBy(x => (x.Id, x.PluginName))
                .Select(g => new WarningGroup
                {
                    Id = g.Key.Id,
                    PluginName = g.Key.PluginName,
                    Severity = g.Max(x => x.Severity),
                    Warnings = g.ToList(),
                    NpcKeys = g.Select(x => x.RecordKey).OfType<IRecordKey>().ToList(),
                })
                .OrderByDescending(x => x.Severity)
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.Title)
                .ToList();
        }
    }
}
