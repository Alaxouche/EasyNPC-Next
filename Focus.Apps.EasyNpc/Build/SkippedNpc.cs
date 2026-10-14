namespace Focus.Apps.EasyNpc.Build
{
    // Describes an NPC that had to be left out of the merge, e.g. because its records could not be read or imported.
    // Skipped NPCs simply keep their regular load order behavior, but the user should be told about them.
    public record SkippedNpc(string Label, string PluginName, string Reason)
    {
        // "LocalFormId:Plugin", so the report can jump to the NPC on the Profile page. Null for entries that aren't
        // tied to one NPC.
        public string? Key { get; init; }
        public bool HasKey => !string.IsNullOrEmpty(Key);

        public SkippedNpc(IRecordKey key, string label, string pluginName, string reason)
            : this(label, pluginName, reason)
        {
            Key = new RecordKey(key).ToString();
        }
    }
}
