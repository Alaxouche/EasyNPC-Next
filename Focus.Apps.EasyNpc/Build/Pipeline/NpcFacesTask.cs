using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Mutagen;
using Focus.ModManagers;
using Focus.Providers.Mutagen;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Focus.Apps.EasyNpc.Build.Pipeline
{
    public class NpcFacesTask : BuildTask<NpcFacesTask.Result>
    {
        public class Result
        {
            public IReadOnlyList<SkippedNpc> Skipped { get; private init; }

            public Result(IReadOnlyList<SkippedNpc> skipped)
            {
                Skipped = skipped;
            }
        }

        public delegate NpcFacesTask Factory(PatchInitializationTask.Result patch, NpcDefaultsTask.Result defaults);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly IReadOnlyGameEnvironment<ISkyrimModGetter> env;
        private readonly ILogger log;
        private readonly IModRepository modRepository;
        private readonly PatchInitializationTask.Result patch;

        public NpcFacesTask(
            IReadOnlyGameEnvironment<ISkyrimModGetter> env, ILogger log, IModRepository modRepository,
            PatchInitializationTask.Result patch, NpcDefaultsTask.Result defaults)
        {
            this.defaults = defaults;
            this.env = env;
            this.log = log;
            this.modRepository = modRepository;
            this.patch = patch;
        }

        protected override Task<Result> Run(BuildSettings settings)
        {
            return Task.Run(() =>
            {
                ItemCount.OnNext(defaults.Npcs.Count);
                var skipped = new List<SkippedNpc>();
                foreach (var (unresolvedModel, record) in defaults.Npcs)
                {
                    log.Debug("Applying visual attributes for {npcLabel}", unresolvedModel.DescriptiveLabel);
                    NextItem(unresolvedModel.DescriptiveLabel);
                    try
                    {
                        ApplyFace(settings, unresolvedModel, record);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A single broken or unreadable record used to fail the entire build with a generic error.
                        // Removing the half-patched override reverts the NPC to its regular load order behavior, which
                        // is far less disruptive; the log has the details for following up on the specific NPC.
                        log.Error(ex,
                            "Failed to apply visual attributes for {npcLabel}. This NPC will be excluded from the " +
                            "merge.",
                            unresolvedModel.DescriptiveLabel);
                        patch.Mod.Npcs.RecordCache.Remove(record.FormKey);
                        skipped.Add(new(
                            unresolvedModel.DescriptiveLabel, unresolvedModel.FaceOption.PluginName, ex.Message));
                    }
                }
                // Pull any forwarded races (custom-race NPCs and children) into the merge so their plugins do not become
                // masters. No-op when nothing was forwarded.
                patch.Importer.MergeInForwardedRaceDependencies();
                return new Result(skipped);
            });
        }

        private void ApplyFace(BuildSettings settings, Profiles.INpc unresolvedModel, Npc record)
        {
            if (!settings.Profile.TryResolveTemplate(unresolvedModel, out var model))
            {
                log.Warning(
                    "Unable to find the template {targetKey} for NPC {npcLabel} in the current profile. " +
                    "Traits cannot be copied and this character may be bugged in game.",
                    unresolvedModel.DefaultOption.Analysis.TemplateInfo?.Key, unresolvedModel.DescriptiveLabel);
                return;
            }
            if (model != unresolvedModel)
                log.Information(
                    "Redirected NPC {npcLabel} to template {templateNpcLabel}",
                    unresolvedModel.DescriptiveLabel, model.DescriptiveLabel);
            var faceModKey = ModKey.FromNameAndExtension(model.FaceOption.PluginName);
            var faceMod = env.LoadOrder.GetIfEnabled(faceModKey).Mod;
            var faceNpcRecord = env.GetModNpc(faceModKey, model.ToFormKey());
            if (faceNpcRecord.Race.FormKey != record.Race.FormKey)
            {
                if (settings.ForwardCustomRaces)
                {
                    // Keep the face plugin's race (e.g. a custom Khajiit race). ForwardRace records the source so it can
                    // be merged in later; the head parts and worn armor below then pick up this race.
                    record.Race.SetTo(patch.Importer.ForwardRace(faceNpcRecord.Race));
                    log.Information(
                        "Forwarding race from face plugin {pluginName} for {npcLabel}.",
                        model.FaceOption.PluginName, unresolvedModel.DescriptiveLabel);
                }
                else
                    log.Information(
                        "Face plugin {pluginName} uses a different race for {npcLabel}. The default race will " +
                        "be kept, and visual attributes will be ported to it.",
                        model.FaceOption.PluginName, unresolvedModel.DescriptiveLabel);
            }
            // A child must keep its child race and child skin, or it ends up with an adult body. The race is already on
            // the record; ForwardRace additionally merges it in when it comes from a non-master mod (a custom child-race
            // mod like RS Children), so that mod does not become a master. Vanilla child races are masters and kept as-is.
            if (unresolvedModel.DefaultOption.Analysis.IsChild)
                record.Race.SetTo(patch.Importer.ForwardRace(record.Race));
            log.Debug("Importing shallow overrides from {pluginName}", model.FaceOption.PluginName);
            // "Deep copy" doesn't copy dependencies, so we only do this for non-referential attributes.
            record.DeepCopyIn(faceNpcRecord, new Npc.TranslationMask(defaultOn: false)
            {
                FaceMorph = true,
                FaceParts = true,
                TextureLighting = true,
                TintLayers = true,
                // Height and weight might not be entirely safe to copy without carrying over body type (WNAM),
                // but serious problems should be extremely rare. Regardless of whether or not we copy the
                // height/weight, we'll still end up with an NPC whose body is a hybrid of the modded NPC and
                // the default body.
                Height = true,
                Weight = true,
            });
            // We will respect the "Opposite gender animations" flag from the overhaul mod. If an overhaul
            // decides to make an NPC look more feminine (or masculine), then it probably wants the animations
            // to reflect that, and this would be consistent with both their intent and the intent of the user.
            if (faceNpcRecord.Configuration.Flags.HasFlag(NpcConfiguration.Flag.OppositeGenderAnims))
                record.Configuration.Flags |= NpcConfiguration.Flag.OppositeGenderAnims;
            else
                record.Configuration.Flags &= ~NpcConfiguration.Flag.OppositeGenderAnims;
            log.Debug("Importing head parts from {pluginName}", model.FaceOption.PluginName);
            record.HeadParts.Clear();
            // We don't use head parts from the record anymore; instead, use the "full list" provided by the
            // analysis engine at startup. This ensures that the facegen can't be broken by race edits, etc.
            foreach (var headPartKey in model.FaceOption.Analysis.MainHeadParts)
            {
                var sourceHeadPart = headPartKey.ToFormKey().AsLinkGetter<IHeadPartGetter>();
                var mergedHeadPart = patch.Importer.Import(sourceHeadPart, x => x.HeadParts);
                if (mergedHeadPart.HasValue)
                    record.HeadParts.Add(mergedHeadPart.Value);
            }
            foreach (var headPart in record.HeadParts)
                patch.Importer.AddHeadPartRace(headPart, record.Race);
            log.Debug("Importing hair color from {pluginName}", model.FaceOption.PluginName);
            record.HairColor.SetTo(patch.Importer.Import(faceNpcRecord.HairColor, x => x.Colors));
            log.Debug("Importing face texture from {pluginName}", model.FaceOption.PluginName);
            record.HeadTexture.SetTo(patch.Importer.Import(faceNpcRecord.HeadTexture, x => x.TextureSets));
            log.Debug("Importing worn armor from {pluginName}", model.FaceOption.PluginName);
            // Like head parts, we want to use the "effective" skin here, in case it was changed by a race edit.
            var skinKey = model.FaceOption.Analysis.SkinKey?.ToFormKey() ?? FormKey.Null;
            record.WornArmor.SetTo(patch.Importer.Import(skinKey.AsLinkGetter<IArmorGetter>(), x => x.Armors));
            patch.Importer.AddArmorRace(record.WornArmor, faceNpcRecord.Race, record.Race);
            ClearInheritedTraitsIfOwnFace(unresolvedModel, record, faceNpcRecord);
        }

        // A merged NPC has its own head parts and FaceGen, so if it still inherits "Traits" from a template the game
        // loads the template's FaceGen instead and the head goes invisible (common on guards after an overhaul). When
        // the face is the NPC's own, drop the flag and backfill race/voice in case they only lived on the template.
        private void ClearInheritedTraitsIfOwnFace(
            Profiles.INpc unresolvedModel, Npc record, INpcGetter faceNpcRecord)
        {
            var faceInheritsTraits = unresolvedModel.FaceOption.Analysis.TemplateInfo?.InheritsTraits == true;
            if (faceInheritsTraits ||
                !record.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
                return;
            // Only break inheritance when a FaceGen is actually available. Some overhauls (often female bandit/guard/
            // hunter edits) give an NPC head parts but ship no per-NPC FaceGen. Clearing the flag there would leave the
            // NPC with head parts but no FaceGen = invisible face, so keep inheriting the template's FaceGen instead.
            if (!HasFaceGen(unresolvedModel))
            {
                log.Warning(
                    "{npcLabel} would get its own face from {pluginName}, but no FaceGen was found for it; keeping " +
                    "template inheritance so the head still renders.",
                    unresolvedModel.DescriptiveLabel, unresolvedModel.FaceOption.PluginName);
                return;
            }
            if (record.Race.IsNull && !faceNpcRecord.Race.IsNull)
                record.Race.SetTo(faceNpcRecord.Race.FormKey);
            if (record.Voice.IsNull && !faceNpcRecord.Voice.IsNull)
                record.Voice.SetTo(faceNpcRecord.Voice.FormKey);
            record.Configuration.TemplateFlags &= ~NpcConfiguration.TemplateFlag.Traits;
            // Only drop the template link if nothing else is still inherited from it (stats, factions, etc.).
            if (record.Configuration.TemplateFlags == 0)
                record.Template.Clear();
        }

        // True when a FaceGen mesh is actually available for this NPC from the chosen face (or its FaceGen override).
        // Mirrors the availability check in FaceGenCopyTask.
        private bool HasFaceGen(Profiles.INpc npc)
        {
            var components = (npc.FaceGenOverride is not null ?
                npc.FaceGenOverride.Components :
                modRepository.SearchForFiles(npc.FaceOption.PluginName, false).Select(r => r.ModComponent))
                .ToHashSet();
            var faceGenPath = FileStructure.GetFaceMeshFileName(npc);
            return modRepository.SearchForFiles(faceGenPath, true).Any(r => components.Contains(r.ModComponent));
        }
    }
}
