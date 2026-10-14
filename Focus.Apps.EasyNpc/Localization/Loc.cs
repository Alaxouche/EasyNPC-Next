using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Windows.Markup;

namespace Focus.Apps.EasyNpc.Localization
{
    // UI text lookup. English is the neutral resource; other languages are satellite resources (Strings.fr.resx, ...).
    // A missing translation falls back to English, and a missing key to the key itself, so a gap shows up as an
    // untranslated line rather than an empty one.
    public static class Loc
    {
        // Setting value, culture name, and the language's own name for the picker.
        public static readonly IReadOnlyList<(string Value, string Name)> Languages = new[]
        {
            ("System", "System"),
            ("en", "English"),
            ("fr", "Français"),
            ("zh-Hans", "简体中文"),
        };

        private static readonly ResourceManager Resources =
            new("Focus.Apps.EasyNpc.Localization.Strings", typeof(Loc).Assembly);

        // Call once at startup, before any window is created. Changing it later only affects text read afterwards,
        // which is why the setting takes effect on the next start.
        public static void Apply(string? language)
        {
            if (string.IsNullOrWhiteSpace(language) || language == "System")
                return;
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Unknown value in the settings file: keep the system language.
            }
        }

        public static string Get(string key)
        {
            return Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }
    }

    // {loc:Tr Key} in XAML.
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        [ConstructorArgument("key")]
        public string Key { get; set; } = string.Empty;

        public TrExtension() { }

        public TrExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Loc.Get(Key);
        }
    }
}
