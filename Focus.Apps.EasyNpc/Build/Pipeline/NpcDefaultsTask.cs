using Focus.Apps.EasyNpc.Mutagen;
using Focus.Providers.Mutagen;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NpcRecord = Mutagen.Bethesda.Skyrim.Npc;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class NpcDefaultsTask : BuildTask<NpcDefaultsTask.Result>
    {
        public class Result
        {
            public IReadOnlyList<(Profiles.INpc Model, NpcRecord Record)> Npcs { get; private init; }
            public IReadOnlyList<SkippedNpc> Skipped { get; private init; }

            public Result(
                IReadOnlyList<(Profiles.INpc model, NpcRecord record)> npcs, IReadOnlyList<SkippedNpc> skipped)
            {
                Npcs = npcs;
                Skipped = skipped;
            }
        }

        public delegate NpcDefaultsTask Factory(PatchInitializationTask.Result patch);

        private readonly IReadOnlyGameEnvironment<ISkyrimModGetter> env;
        private readonly ILogger log;
        private readonly PatchInitializationTask.Result patch;

        public NpcDefaultsTask(
            IReadOnlyGameEnvironment<ISkyrimModGetter> env, ILogger log, PatchInitializationTask.Result patch)
        {
            this.env = env;
            this.log = log;
            this.patch = patch;
        }

        protected override Task<Result> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                ItemCount.OnNext(settings.Profile.Count);
                var npcs = new List<(Profiles.INpc model, NpcRecord record)>();
                var skipped = new List<SkippedNpc>();
                foreach (var npc in settings.Profile.Npcs)
                {
                    log.Debug("Importing default attributes for {npcLabel}", npc.DescriptiveLabel);
                    NextItem(npc.DescriptiveLabel);
                    if (npc.HasUnmodifiedFaceTemplate)
                    {
                        log.Information(
                            "Skipping {npcLabel} because all overrides use the same template.", npc.DescriptiveLabel);
                        continue;
                    }
                    try
                    {
                        var defaultModKey = ModKey.FromNameAndExtension(npc.DefaultOption.PluginName);
                        var defaultNpc = env.GetModNpc(defaultModKey, npc.ToFormKey());
                        var mergedNpcRecord = patch.Mod.Npcs.GetOrAddAsOverride(defaultNpc);
                        npcs.Add((npc, mergedNpcRecord));
                        patch.Importer.AddMaster(npc.DefaultOption.PluginName);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A single broken or unreadable record used to fail the entire build with a generic error;
                        // leaving the NPC out of the merge (it keeps its regular load order behavior) is far less
                        // disruptive. The log has the details for following up on the specific NPC.
                        log.Error(ex,
                            "Failed to import default record for {npcLabel} from {pluginName}. This NPC will be " +
                            "excluded from the merge.",
                            npc.DescriptiveLabel, npc.DefaultOption.PluginName);
                        skipped.Add(new(npc.DescriptiveLabel, npc.DefaultOption.PluginName, ex.Message));
                    }
                }
                return new Result(npcs, skipped);
            });
        }
    }
}
