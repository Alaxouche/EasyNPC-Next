using Focus.Apps.EasyNpc.Profiles;
using System;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Profiles
{
    public class ProfileEventTests
    {
        [Fact]
        public void Deserialize_WithValidLine_RoundTrips()
        {
            var original = new ProfileEvent
            {
                BasePluginName = "Skyrim.esm",
                LocalFormIdHex = "01A696",
                Timestamp = new DateTime(2022, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                Field = NpcProfileField.FacePlugin,
                OldValue = "Old.esp",
                NewValue = "New.esp",
            };

            var deserialized = ProfileEvent.Deserialize(original.Serialize());

            Assert.Equal(original, deserialized);
        }

        [Theory]
        [InlineData("{\"master\"malformed junk")]
        [InlineData("{\"master\": \"Skyrim.esm\", \"id\": \"01A6")]
        [InlineData("not json at all")]
        [InlineData("{}")] // Missing [JsonRequired] fields
        public void Deserialize_WithCorruptLine_ReturnsNull(string line)
        {
            Assert.Null(ProfileEvent.Deserialize(line));
        }
    }
}
