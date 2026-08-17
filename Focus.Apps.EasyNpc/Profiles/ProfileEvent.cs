using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace Focus.Apps.EasyNpc.Profiles
{
    public enum NpcProfileField { DefaultPlugin, FaceMod, FacePlugin };

    public record ProfileEvent : IRecordKey
    {
        public static ProfileEvent? Deserialize(string serialized)
        {
            try
            {
                return JsonConvert.DeserializeObject<ProfileEvent>(serialized);
            }
            catch (JsonException)
            {
                // A corrupted or truncated line (e.g. from a crash or power loss while writing the autosave) used to
                // make the entire app unusable until the profile log was deleted by hand (#212). One unreadable event
                // is not worth losing the whole profile over; skip it and keep the rest.
                return null;
            }
        }

        [JsonProperty("master")]
        [JsonRequired]
        public string BasePluginName { get; init; } = string.Empty;

        [JsonProperty("id")]
        [JsonRequired]
        public string LocalFormIdHex { get; init; } = string.Empty;

        [JsonProperty("time")]
        public DateTime Timestamp { get; init; }

        [JsonProperty("field")]
        [JsonConverter(typeof(StringEnumConverter))]
        public NpcProfileField Field { get; init; }

        [JsonProperty("oldValue")]
        public string? OldValue { get; init; }

        [JsonProperty("newValue")]
        public string? NewValue { get; init; }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
