using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Profiles;
using Focus.ModManagers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Focus.Apps.EasyNpc.Build.Checks
{
    // Building into a new output mod while an earlier merge is still enabled is the most common way to end up with
    // FaceGen "conflicts" in Verify and wrong faces in game: the old merge's FaceGen, loose by default, beats the new
    // merge's copy - and a loose file wins over an archived one no matter how the mods are ordered.
    public class PreviousMerges : IGlobalBuildCheck
    {
        private readonly IModRepository modRepository;

        public PreviousMerges(IModRepository modRepository)
        {
            this.modRepository = modRepository;
        }

        public IEnumerable<BuildWarning> Run(Profile profile, BuildSettings settings)
        {
            var outputDirectory = NormalizeDirectory(settings.OutputDirectory);
            return modRepository
                .SearchForFiles(FileStructure.MergeFileName, false)
                .Select(x => x.ModComponent)
                .Where(c => c.IsEnabled)
                // Building into the existing merge replaces it, which is fine.
                .Where(c => !string.Equals(
                    NormalizeDirectory(c.Path), outputDirectory, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new BuildWarning(
                    BuildWarningId.PreviousMergeEnabled, WarningMessages.PreviousMergeEnabled(name)));
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path;
            }
        }
    }
}
