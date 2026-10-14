using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build
{
    public class BuildReport
    {
        private static readonly JsonSerializer Serializer = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy(),
            },
            Formatting = Formatting.Indented,
        };

        // NPCs whose chosen face ships a broken (empty, corrupt or geometry-less) FaceGen mesh, which renders as an
        // invisible face in game.
        public IReadOnlyList<SkippedNpc> BrokenFaceGenNpcs { get; init; } = Array.Empty<SkippedNpc>();
        [JsonIgnore]
        public bool HasBrokenFaceGen => BrokenFaceGenNpcs.Count > 0;
        [JsonIgnore]
        public bool HasSkippedNpcs => SkippedNpcs.Count > 0;
        // Merged NPCs whose written record and copied FaceGen no longer describe the same head. This is a bug in the
        // merge itself: left alone, the game drives a baked shape with the wrong head part's morph data, which can
        // crash during facial animation.
        public IReadOnlyList<SkippedNpc> FaceGenMismatchNpcs { get; init; } = Array.Empty<SkippedNpc>();
        [JsonIgnore]
        public bool HasFaceGenMismatches => FaceGenMismatchNpcs.Count > 0;
        [JsonIgnore]
        public bool HasMissingAssets => MissingAssetGroups.Count > 0;
        [JsonIgnore]
        public int MissingAssetCount => MissingAssetGroups.Sum(x => x.Count);
        public IReadOnlyList<MissingAssetGroup> MissingAssetGroups { get; init; } = Array.Empty<MissingAssetGroup>();
        public int MergedNpcCount { get; init; }
        public string ModName { get; init; } = string.Empty;
        public IReadOnlyList<SkippedNpc> SkippedNpcs { get; init; } = Array.Empty<SkippedNpc>();

        public void SaveToFile(string fileName)
        {
            using var fs = File.Create(fileName);
            SaveToStream(fs);
        }

        public void SaveToStream(Stream stream)
        {
            using var streamWriter = new StreamWriter(stream);
            Serializer.Serialize(streamWriter, this);
        }
    }
}