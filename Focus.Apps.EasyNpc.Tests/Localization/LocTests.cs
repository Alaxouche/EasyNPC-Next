using Focus.Apps.EasyNpc.Localization;
using System.Globalization;
using System.Threading;
using Xunit;

namespace Focus.Apps.EasyNpc.Tests.Localization
{
    public class LocTests
    {
        [Theory]
        [InlineData("en", "Profile")]
        [InlineData("fr", "Profil")]
        [InlineData("zh-Hans", "配置")]
        public void Get_ReturnsTheTranslationForTheUiCulture(string culture, string expected)
        {
            var previous = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal(expected, Loc.Get("Nav.Profile"));
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = previous;
            }
        }

        [Fact]
        public void Get_UnknownKey_ReturnsTheKey()
        {
            Assert.Equal("No.Such.Key", Loc.Get("No.Such.Key"));
        }

        [Fact]
        public void Format_FillsPlaceholders()
        {
            var previous = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
                Assert.Equal("3 plugins sélectionnés ne peuvent pas être chargés.", Loc.Format("Loader.ManyUnloadable", 3));
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = previous;
            }
        }
    }
}
