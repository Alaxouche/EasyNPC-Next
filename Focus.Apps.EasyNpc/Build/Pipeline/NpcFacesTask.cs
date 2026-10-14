using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Mutagen;
using Focus.Files;
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
            // NPCs the merge actually writes a record for AND whose record does not inherit its traits, i.e. the ones
            // the game will look up a FaceGen for under their own name. Nothing else may be given a FaceGen: a FaceGen
            // with no record behind it gets paired with whatever record the load order provides, and a face baked for
            // one set of head parts driven by another set's morph data crashes the game during facial animation.
            public IReadOnlySet<IRecordKey> FaceGenNpcs { get; private init; }

            public Result(IReadOnlyList<SkippedNpc> skipped, IReadOnlySet<IRecordKey>? faceGenNpcs = null)
            {
                Skipped = skipped;
                FaceGenNpcs = faceGenNpcs ?? new HashSet<IRecordKey>(RecordKeyComparer.Default);
            }
        }

        public delegate NpcFacesTask Factory(PatchInitializationTask.Result patch, NpcDefaultsTask.Result defaults);

        private readonly NpcDefaultsTask.Result defaults;
        private readonly IReadOnlyGameEnvironment<ISkyrimModGetter> env;
        private readonly IFaceGenEditor faceGenEditor;
        private readonly IFileProvider fileProvider;
        private readonly ILogger log;
        private readonly IModRepository modRepository;
        private readonly PatchInitializationTask.Result patch;

        public NpcFacesTask(
            IReadOnlyGameEnvironment<ISkyrimModGetter> env, IFaceGenEditor faceGenEditor, IFileProvider fileProvider,
            ILogger log, IModRepository modRepository, PatchInitializationTask.Result patch,
            NpcDefaultsTask.Result defaults)
        {
            this.defaults = defaults;
            this.env = env;
            this.faceGenEditor = faceGenEditor;
            this.fileProvider = fileProvider;
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
                var faceGenNpcs = new HashSet<IRecordKey>(RecordKeyComparer.Default);
                foreach (var (unresolvedModel, record) in defaults.Npcs)
                {
                    log.Debug("Applying visual attributes for {npcLabel}", unresolvedModel.DescriptiveLabel);
                    NextItem(unresolvedModel.DescriptiveLabel);
                    try
                    {
                        var faceModel = ApplyFace(settings, unresolvedModel, record);
                        // Writing head parts the resolved FaceGen doesn't contain guarantees an invisible head, so
                        // leave the NPC out of the merge instead of shipping a record that cannot render.
                        var unrenderable = faceModel is not null ?
                            FindUnrenderableHeadParts(unresolvedModel, faceModel, record) : null;
                        if (unrenderable is not null)
                        {
                            log.Warning(
                                "{npcLabel} would get head parts from {pluginName} that are not in the FaceGen the " +
                                "game resolves for it ({missingParts}), which renders as an invisible head. No " +
                                "FaceGen is available from that plugin either, so this NPC is being excluded from " +
                                "the merge. Pick a different face for it, or fix the source mod.",
                                unresolvedModel.DescriptiveLabel, unresolvedModel.FaceOption.PluginName, unrenderable);
                            patch.Mod.Npcs.RecordCache.Remove(record.FormKey);
                            skipped.Add(new(
                                unresolvedModel, unresolvedModel.DescriptiveLabel,
                                unresolvedModel.FaceOption.PluginName,
                                $"The chosen face provides no FaceGen for this NPC, and its head parts " +
                                $"({unrenderable}) are missing from the FaceGen the game would use, which would " +
                                $"leave the NPC with an invisible head."));
                        }
                        // A record that still inherits its traits takes its whole appearance, FaceGen included, from
                        // its template, so it must not be given one of its own.
                        else if (!record.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
                            faceGenNpcs.Add(new RecordKey(unresolvedModel));
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
                            unresolvedModel, unresolvedModel.DescriptiveLabel, unresolvedModel.FaceOption.PluginName,
                            ex.Message));
                    }
                }
                // Pull any forwarded races (custom-race NPCs and children) into the merge so their plugins do not become
                // masters. No-op when nothing was forwarded.
                patch.Importer.MergeInForwardedRaceDependencies();
                return new Result(skipped, faceGenNpcs);
            });
        }

        // Returns the profile entry the face was actually taken from - the NPC itself, or the template it was
        // redirected to - or null when nothing was applied.
        private Profiles.INpc? ApplyFace(BuildSettings settings, Profiles.INpc unresolvedModel, Npc record)
        {
            if (!settings.Profile.TryResolveTemplate(unresolvedModel, out var model))
            {
                log.Warning(
                    "Unable to find the template {targetKey} for NPC {npcLabel} in the current profile. " +
                    "Traits cannot be copied and this character may be bugged in game.",
                    unresolvedModel.DefaultOption.Analysis.TemplateInfo?.Key, unresolvedModel.DescriptiveLabel);
                return null;
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
            return model;
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

        // Skyrim builds a merged NPC's head by looking up each head part on the record, by editor ID, among the shapes
        // of the FaceGen the load order resolves. A head part it cannot find there is not drawn - lose the head part
        // and the NPC has no face at all, only the mouth the FaceGen still carries.
        //
        // That is fine whenever the merge ships the FaceGen itself, because it then comes from the same plugin as the
        // head parts. The dangerous case is an overhaul that rewrites an NPC's head parts but ships no FaceGen for it:
        // nothing is copied, the game falls back to the original (usually vanilla) FaceGen, and the custom head part
        // is simply absent from it. Whole classes of generic NPCs - forsworn, thalmor, bandits, hags - go faceless
        // this way. Returns the offending head part names when there is no FaceGen to be had and the NPC is therefore
        // not worth merging; null when the record renders, or when the mismatch is something the user can still fix.
        private string? FindUnrenderableHeadParts(Profiles.INpc npc, Profiles.INpc faceModel, Npc record)
        {
            if (record.HeadParts.Count == 0)
                return null;
            // Still inheriting traits: the game takes the head - and the FaceGen - from the template, so the record's
            // own head parts and FaceGen path don't decide anything here.
            if (record.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
                return null;
            // The merge ships its own FaceGen: head parts and FaceGen come from the same plugin.
            if (WillCopyFaceGen(npc, faceModel))
                return null;
            // A face that doesn't touch the head parts is always consistent with whatever FaceGen is already in use.
            // This keeps the FaceGen reads below down to the NPCs an overhaul actually restyled.
            var faceHeadParts = faceModel.FaceOption.Analysis.MainHeadParts.ToHashSet();
            var masterHeadParts = faceModel.Options
                .FirstOrDefault(x => string.Equals(
                    x.PluginName, faceModel.BasePluginName, StringComparison.CurrentCultureIgnoreCase))
                ?.Analysis.MainHeadParts;
            if (masterHeadParts is not null && faceHeadParts.SetEquals(masterHeadParts))
                return null;
            var faceGenPath = FileStructure.GetFaceMeshFileName(npc);
            if (!fileProvider.Exists(faceGenPath))
                // No FaceGen anywhere: the game assembles the head from the head part meshes on their own. Unmorphed
                // and not what the overhaul intended, but it does render, so it is not this task's problem.
                return null;
            IReadOnlySet<string> shapeNames;
            try
            {
                shapeNames = faceGenEditor
                    .GetHeadPartNames(faceGenPath, fileProvider.ReadBytes(faceGenPath).ToArray())
                    .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Couldn't read the FaceGen at {faceGenPath} to check it against the head parts of " +
                    "{npcLabel}.", faceGenPath, npc.DescriptiveLabel);
                return null;
            }
            if (shapeNames.Count == 0)
                // Unreadable, or not a FaceGen at all. Broken FaceGens are reported separately; don't drop the NPC on
                // the strength of a file we couldn't parse.
                return null;
            var missing = record.HeadParts
                .Select(x => patch.Importer.TryResolve<IHeadPartGetter>(x.FormKey))
                .NotNull()
                // "No beard" / "no scar" placeholder parts carry no model and are never baked into a FaceGen.
                .Where(x =>
                    !string.IsNullOrEmpty(x.EditorID) && !string.IsNullOrEmpty(x.Model?.File.PathOrDefault()))
                .Select(x => x.EditorID!)
                .Where(x => !shapeNames.Contains(x))
                .ToList();
            if (missing.Count == 0)
                return null;
            if (HasFaceGen(faceModel))
            {
                // The face plugin does have a FaceGen for this NPC; it just isn't the one the load order resolves,
                // because something later overrides that path (a mesh-processing output mod shipping a stale copy is
                // the usual culprit). That is a mod priority problem the user can fix, and the NPC is no worse off
                // inside the merge than outside it, so keep it and say what is wrong.
                log.Warning(
                    "The FaceGen the game resolves for {npcLabel} does not contain {missingParts}, so this NPC will " +
                    "render without a face. {pluginName} does provide a FaceGen for it, so a later mod is overriding " +
                    "{faceGenPath} with a stale copy - check mod priorities.",
                    npc.DescriptiveLabel, string.Join(", ", missing), npc.FaceOption.PluginName, faceGenPath);
                return null;
            }
            return string.Join(", ", missing);
        }

        // Whether the build will copy a FaceGen into the merge for this NPC. Mirrors the filter in FaceGenCopyTask:
        // the file has to exist, and either the face differs from what the load order already resolves or the NPC was
        // redirected to a template (whose FaceGen is then saved under this NPC's own name).
        private bool WillCopyFaceGen(Profiles.INpc npc, Profiles.INpc faceModel)
        {
            var isCopied =
                faceModel != npc ||
                npc.FaceGenOverride is not null ||
                (!npc.FaceOption.IsBaseGame && npc.FaceOption.PluginName != npc.DefaultOption.PluginName);
            return isCopied && HasFaceGen(faceModel);
        }

        // True when a FaceGen mesh is actually available from the chosen face (or its FaceGen override). Takes the
        // model the face was actually read from, which for a template-based NPC is the template, not the NPC itself.
        // Mirrors the availability check in FaceGenCopyTask.
        private bool HasFaceGen(Profiles.INpc faceModel)
        {
            var components = (faceModel.FaceGenOverride is not null ?
                faceModel.FaceGenOverride.Components :
                modRepository.SearchForFiles(faceModel.FaceOption.PluginName, false).Select(r => r.ModComponent))
                .ToHashSet();
            var faceGenPath = FileStructure.GetFaceMeshFileName(faceModel);
            return modRepository.SearchForFiles(faceGenPath, true).Any(r => components.Contains(r.ModComponent));
        }
    }
}
