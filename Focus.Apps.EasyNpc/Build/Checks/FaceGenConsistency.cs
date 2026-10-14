using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build.Checks
{
    public class FaceGenConsistency : INpcBuildCheck
    {
        private readonly IModRepository modRepository;

        public FaceGenConsistency(IModRepository modRepository)
        {
            this.modRepository = modRepository;
        }

        public IEnumerable<BuildWarning> Run(INpc npc, BuildSettings settings)
        {
            if (!npc.CanCustomizeFace)
                yield break;
            if (npc.FaceGenOverride is not null)
                // Eventually, we'll want to open up the override file and actually check the nodes. This check is
                // temporarily out of scope, which means reporting a generic warning.
                yield return new BuildWarning(
                    new RecordKey(npc),
                    BuildWarningId.FaceGenOverride,
                    WarningMessages.FaceGenOverride(npc.EditorId, npc.Name, npc.FaceGenOverride.Name));

            // Waste of time to check anything pointing to vanilla (except with FaceGen overrides, as above)
            if (npc.FaceOption.IsBaseGame)
                yield break;

            // A FaceGen is required whenever the head parts we are about to write differ from the ones baked into the
            // FaceGen the game will actually resolve for this NPC.
            //
            // Comparing only against the *default* option missed the most damaging case: when the chosen face is also
            // the load order winner but ships no FaceGen of its own, nothing gets copied and nothing was flagged - yet
            // the game still resolves the original (usually vanilla) FaceGen, whose shapes are named after the original
            // head parts. The overhaul's head part is then not found in it and the head does not render at all. So
            // compare against the master record too, since that is what the fallback FaceGen was built from.
            var faceHeadParts = npc.FaceOption.Analysis.MainHeadParts.ToHashSet();
            var masterHeadParts = npc.Options
                .FirstOrDefault(x => string.Equals(
                    x.PluginName, npc.BasePluginName, StringComparison.CurrentCultureIgnoreCase))
                ?.Analysis.MainHeadParts;
            var requiresFaceGen =
                !faceHeadParts.SetEquals(npc.DefaultOption.Analysis.MainHeadParts) ||
                (masterHeadParts is not null && !faceHeadParts.SetEquals(masterHeadParts));
            if (!requiresFaceGen)
                yield break;
            // When the chosen face inherits its traits, the appearance - head parts and FaceGen alike - belongs to
            // the template, and the overhaul ships no FaceGen under this NPC's own name on purpose. The build copies
            // the template's FaceGen for it, so that is the one to look for; checking this NPC's own name reported
            // every such NPC as missing a FaceGen it was never supposed to have.
            var faceSource = settings.Profile.TryResolveTemplate(npc, out var template) ? template : npc;
            var pluginComponents = modRepository
                .SearchForFiles(faceSource.FaceOption.PluginName, false)
                .Select(x => x.ModComponent)
                .Distinct();
            var faceGenPath = FileStructure.GetFaceMeshFileName(faceSource);
            var faceGenComponents = pluginComponents
                .Where(c => modRepository.ContainsFile(new[] { c }, faceGenPath, true))
                .ToList();
            if (faceGenComponents.Count == 0)
                yield return new BuildWarning(
                    npc.FaceOption.PluginName,
                    new RecordKey(npc),
                    BuildWarningId.MissingFaceGen,
                    WarningMessages.MissingFaceGen(
                        npc.EditorId, npc.Name, npc.FaceOption.PluginName, pluginComponents.Select(x => x.Name)));
            else if (faceGenComponents.Count > 1)
                yield return new BuildWarning(
                    npc.FaceOption.PluginName,
                    new RecordKey(npc),
                    BuildWarningId.MultipleFaceGen,
                    WarningMessages.MultipleFaceGen(npc.EditorId, npc.Name, faceGenComponents.Select(x => x.Name)));
        }
    }
}
