using Focus.Apps.EasyNpc.Build.Pipeline;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Providers.Mutagen;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Build.Pipeline
{
    public class RecordImporterTests
    {
        private readonly SkyrimMod baseGameMod;
        private readonly Race defaultRace;
        private readonly SkyrimMod faceMod;
        private readonly RecordImporter importer;
        private readonly Race khajiitRace;
        private readonly SkyrimMod mergedMod;
        private readonly Race nordRace;

        public RecordImporterTests()
        {
            baseGameMod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
            defaultRace = baseGameMod.Races.AddNew("DefaultRace");
            khajiitRace = baseGameMod.Races.AddNew("KhajiitRace");
            nordRace = baseGameMod.Races.AddNew("NordRace");
            faceMod = new SkyrimMod(ModKey.FromNameAndExtension("FaceMod.esp"), SkyrimRelease.SkyrimSE);
            mergedMod = new SkyrimMod(ModKey.FromNameAndExtension("NPC Appearances Merged.esp"), SkyrimRelease.SkyrimSE);

            var loadOrder = new LoadOrder<IModListing<ISkyrimModGetter>>();
            loadOrder.Add(new ModListing<ISkyrimModGetter>(baseGameMod, enabled: true));
            loadOrder.Add(new ModListing<ISkyrimModGetter>(faceMod, enabled: true));
            var linkCache = loadOrder.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
            var env = new Mock<IMutableGameEnvironment<ISkyrimMod, ISkyrimModGetter>>();
            env.SetupGet(x => x.LoadOrder).Returns(loadOrder);
            env.SetupGet(x => x.LinkCache).Returns(linkCache);

            var appSettings = new Mock<IAppSettings>();
            appSettings.SetupGet(x => x.RaceTransformationKeys).Returns(Enumerable.Empty<IRecordKey>());
            var setupStatics = new Mock<ISetupStatics>();
            setupStatics
                .Setup(x => x.GetBaseMasters(GameRelease.SkyrimSE))
                .Returns(new[] { baseGameMod.ModKey });

            importer = new RecordImporter(
                env.Object, appSettings.Object, new GameSelection(GameRelease.SkyrimSE), setupStatics.Object,
                mergedMod, new LoggerConfiguration().CreateLogger());
        }

        [Fact]
        public void ImportWornArmor_WithNonMasterRace_RewritesAddonRaceToDefault()
        {
            var (armor, _) = AddFaceModSkin(addonRace: AddCustomRace());

            var mergedArmorKey = importer.Import(armor.FormKey.AsLinkGetter<IArmorGetter>(), x => x.Armors);

            var mergedAddon = Assert.Single(mergedMod.ArmorAddons);
            Assert.NotNull(mergedArmorKey);
            Assert.Equal(defaultRace.FormKey, mergedAddon.Race.FormKey);
            Assert.Empty(mergedAddon.AdditionalRaces);
        }

        [Fact]
        public void ImportWornArmor_WithMasterRaces_KeepsAddonRaces()
        {
            var (armor, addon) = AddFaceModSkin(addonRace: nordRace);
            addon.AdditionalRaces.Add(khajiitRace.FormKey);

            importer.Import(armor.FormKey.AsLinkGetter<IArmorGetter>(), x => x.Armors);

            var mergedAddon = Assert.Single(mergedMod.ArmorAddons);
            Assert.Equal(nordRace.FormKey, mergedAddon.Race.FormKey);
            Assert.Equal(new[] { khajiitRace.FormKey }, mergedAddon.AdditionalRaces.Select(x => x.FormKey));
        }

        [Fact]
        public void ImportWornArmor_WithMasterAddonReference_DoesNotClone()
        {
            var masterAddon = baseGameMod.ArmorAddons.AddNew("NakedTorso");
            masterAddon.Race.SetTo(defaultRace);
            var armor = faceMod.Armors.AddNew("CustomSkin");
            armor.Armature.Add(masterAddon.FormKey.AsLinkGetter<IArmorAddonGetter>());

            importer.Import(armor.FormKey.AsLinkGetter<IArmorGetter>(), x => x.Armors);

            var mergedArmor = Assert.Single(mergedMod.Armors);
            Assert.Equal(new[] { masterAddon.FormKey }, mergedArmor.Armature.Select(x => x.FormKey));
            Assert.Empty(mergedMod.ArmorAddons);
        }

        [Fact]
        public void AddArmorRace_WhenAddonSupportedOriginalRace_AddsActualRace()
        {
            var customRace = AddCustomRace();
            var (armor, _) = AddFaceModSkin(addonRace: customRace);
            var mergedArmorKey = importer.Import(armor.FormKey.AsLinkGetter<IArmorGetter>(), x => x.Armors);

            importer.AddArmorRace(
                mergedArmorKey!.Value.AsLinkGetter<IArmorGetter>(),
                customRace.FormKey.AsLinkGetter<IRaceGetter>(),
                khajiitRace.FormKey.AsLinkGetter<IRaceGetter>());

            var mergedAddon = Assert.Single(mergedMod.ArmorAddons);
            Assert.Contains(khajiitRace.FormKey, mergedAddon.AdditionalRaces.Select(x => x.FormKey));
        }

        [Fact]
        public void AddArmorRace_WhenAddonNeverSupportedOriginalRace_DoesNotAddActualRace()
        {
            var customRace = AddCustomRace();
            var otherCustomRace = AddCustomRace("OtherCustomRace");
            var (armor, _) = AddFaceModSkin(addonRace: customRace);
            var mergedArmorKey = importer.Import(armor.FormKey.AsLinkGetter<IArmorGetter>(), x => x.Armors);

            importer.AddArmorRace(
                mergedArmorKey!.Value.AsLinkGetter<IArmorGetter>(),
                otherCustomRace.FormKey.AsLinkGetter<IRaceGetter>(),
                khajiitRace.FormKey.AsLinkGetter<IRaceGetter>());

            var mergedAddon = Assert.Single(mergedMod.ArmorAddons);
            Assert.DoesNotContain(khajiitRace.FormKey, mergedAddon.AdditionalRaces.Select(x => x.FormKey));
        }

        [Fact]
        public void AddHeadPartRace_AddsRaceToClonedValidRaces_IncludingExtraParts()
        {
            var extraPartRaces = faceMod.FormLists.AddNew("ExtraPartRaces");
            var extraPart = faceMod.HeadParts.AddNew("ExtraPart");
            extraPart.ValidRaces.SetTo(extraPartRaces);
            var mainPartRaces = faceMod.FormLists.AddNew("MainPartRaces");
            var mainPart = faceMod.HeadParts.AddNew("MainPart");
            mainPart.ValidRaces.SetTo(mainPartRaces);
            mainPart.ExtraParts.Add(extraPart.FormKey.AsLinkGetter<IHeadPartGetter>());
            var mergedPartKey = importer.Import(mainPart.FormKey.AsLinkGetter<IHeadPartGetter>(), x => x.HeadParts);

            importer.AddHeadPartRace(
                mergedPartKey!.Value.AsLinkGetter<IHeadPartGetter>(),
                khajiitRace.FormKey.AsLinkGetter<IRaceGetter>());

            Assert.Equal(2, mergedMod.HeadParts.Count);
            Assert.All(mergedMod.HeadParts, headPart =>
            {
                var validRaces = mergedMod.FormLists.Single(x => x.FormKey == headPart.ValidRaces.FormKey);
                Assert.Equal(new[] { khajiitRace.FormKey }, validRaces.Items.Select(x => x.FormKey));
            });
        }

        private Race AddCustomRace(string editorId = "CustomRace")
        {
            return faceMod.Races.AddNew(editorId);
        }

        private (Armor, ArmorAddon) AddFaceModSkin(Race addonRace)
        {
            var addon = faceMod.ArmorAddons.AddNew("CustomSkinAddon");
            addon.Race.SetTo(addonRace);
            var armor = faceMod.Armors.AddNew("CustomSkin");
            armor.Armature.Add(addon.FormKey.AsLinkGetter<IArmorAddonGetter>());
            return (armor, addon);
        }
    }
}
