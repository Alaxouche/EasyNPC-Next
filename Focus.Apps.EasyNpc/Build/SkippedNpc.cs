namespace Focus.Apps.EasyNpc.Build
{
    // Describes an NPC that had to be left out of the merge, e.g. because its records could not be read or imported.
    // Skipped NPCs simply keep their regular load order behavior, but the user should be told about them.
    public record SkippedNpc(string Label, string PluginName, string Reason);
}
