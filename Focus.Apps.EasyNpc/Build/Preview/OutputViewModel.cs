using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using Focus.Analysis.Execution;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.GameData.Files;
using Focus.Apps.EasyNpc.Profiles;
using Focus.Apps.EasyNpc.Reports;
using PropertyChanged;
using Serilog;

namespace Focus.Apps.EasyNpc.Build.Preview
{
    [AddINotifyPropertyChangedInterface]
    public class OutputViewModel
    {
        public delegate OutputViewModel Factory(Profile profile, LoadOrderAnalysis analysis);

        private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars()
            .Where(c => c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
            .Append('?') // Why isn't it included?
            .ToArray();
        private static readonly Regex InvalidPathRegex =
            new("[" + Regex.Escape(new string(InvalidPathChars)) + "]", RegexOptions.Compiled);

        public IObservable<BuildSettings> BuildSettings { get; private init; }

        public bool EnableArchiving
        {
            get => enableArchiving.Value;
            set => enableArchiving.OnNext(value);
        }

        public bool EnableDewiggify
        {
            get => enableDewiggify.Value;
            set => enableDewiggify.OnNext(value);
        }

        public bool GenerateRsvExclusions
        {
            get => generateRsvExclusions.Value;
            set => generateRsvExclusions.OnNext(value);
        }

        public bool KeepFaceGenOutsideArchive
        {
            get => keepFaceGenOutsideArchive.Value;
            set => keepFaceGenOutsideArchive.OnNext(value);
        }

        public bool ForwardCustomRaces
        {
            get => forwardCustomRaces.Value;
            set => forwardCustomRaces.OnNext(value);
        }

        public int MaxNpcsPerPlugin
        {
            get => maxNpcsPerPlugin.Value;
            set => maxNpcsPerPlugin.OnNext(value);
        }

        // Restricts the merge to NPCs you actually customized (plus template-based NPCs, which the merge repairs
        // regardless). Answers the recurring "I only want to patch about 20 NPCs, not all of them" request.
        public bool CustomizedNpcsOnly
        {
            get => customizedNpcsOnly.Value;
            set => customizedNpcsOnly.OnNext(value);
        }

        // How many NPCs each setting would actually merge, so the trade-off is visible before building rather than
        // discovered afterwards in xEdit.
        public int TotalNpcCount => profile.Count;
        [DependsOn(nameof(CustomizedNpcsOnly))]
        public int IncludedNpcCount => CustomizedNpcsOnly ?
            profile.Npcs.Count(x => x.IsCustomized || x.IsTemplated) : profile.Count;

        public bool IsExistingMod { get; private set; }
        public bool IsValidDirectory { get; private set; }

        public string ModName
        {
            get => modName.Value;
            set => modName.OnNext(value);
        }

        public int TexturePathExtractionTimeoutSec
        {
            get => texturePathExtractionTimeoutSec.Value;
            set => texturePathExtractionTimeoutSec.OnNext(value);
        }

        public IObservable<ErrorLevel> OverallErrorLevel => overallErrorLevel;
        public string PluginName => FileStructure.MergeFileName;

        [DependsOn(nameof(IsExistingMod), nameof(IsValidDirectory))]
        public IEnumerable<SummaryItem> SummaryItems => new SummaryItem[]
        {
            IsValidDirectory ?
                new(SummaryItemCategory.StatusOk, "Output path is valid") :
                new(SummaryItemCategory.StatusError, "Invalid output path"),
            IsExistingMod ?
                new(SummaryItemCategory.StatusError, "Output already exists") :
                new(SummaryItemCategory.StatusOk, "Output directory is empty"),
        };

        private readonly BehaviorSubject<bool> enableArchiving = new(true);
        private readonly BehaviorSubject<bool> enableDewiggify = new(false);
        private readonly BehaviorSubject<bool> generateRsvExclusions = new(false);
        private readonly BehaviorSubject<bool> keepFaceGenOutsideArchive = new(false);
        private readonly BehaviorSubject<bool> forwardCustomRaces = new(false);
        private readonly BehaviorSubject<int> maxNpcsPerPlugin = new(0);
        private readonly BehaviorSubject<bool> customizedNpcsOnly = new(false);
        private readonly IFileSystem fs;
        private readonly BehaviorSubject<string> modName = new($"NPC Merge {DateTime.Now:yyyy-MM-dd}");
        private readonly BehaviorSubject<ErrorLevel> overallErrorLevel = new(ErrorLevel.None);
        private readonly Profile profile;
        private readonly BehaviorSubject<int> texturePathExtractionTimeoutSec = new(0);

        public OutputViewModel(IObservableModSettings modSettings, IFileSystem fs, ILogger log, Profile profile)
        {
            this.fs = fs;
            this.profile = profile;
            BuildSettings = Observable
                .CombineLatest(
                    modSettings.RootDirectoryObservable, modName, enableArchiving, enableDewiggify,
                    texturePathExtractionTimeoutSec, generateRsvExclusions, keepFaceGenOutsideArchive,
                    forwardCustomRaces, maxNpcsPerPlugin, customizedNpcsOnly, GetBuildSettings);
            BuildSettings.SubscribeSafe(log, settings =>
            {
                IsExistingMod = IsOutputDirectoryNonEmpty(settings);
                IsValidDirectory = IsOutputDirectoryValid(settings);
                overallErrorLevel.OnNext(GetErrorLevel());
            });
        }

        private BuildSettings GetBuildSettings(
            string modRootDirectory, string outputModName, bool enableArchiving,
            bool enableDewiggify, int texturePathExtractionTimeoutSec, bool generateRsvExclusions,
            bool keepFaceGenOutsideArchive, bool forwardCustomRaces, int maxNpcsPerPlugin, bool customizedNpcsOnly)
        {
            outputModName = outputModName ?? "";
            var outputDirectory = Path.Combine(modRootDirectory, outputModName);
            return new BuildSettings(profile, outputDirectory, outputModName)
            {
                EnableArchiving = enableArchiving,
                EnableDewiggify = enableDewiggify,
                GenerateRsvExclusions = generateRsvExclusions,
                KeepFaceGenOutsideArchive = keepFaceGenOutsideArchive,
                ForwardCustomRaces = forwardCustomRaces,
                MaxNpcsPerPlugin = maxNpcsPerPlugin,
                NpcInclusion = customizedNpcsOnly ? NpcInclusion.CustomizedOnly : NpcInclusion.All,
                TextureExtractionTimeoutSec = texturePathExtractionTimeoutSec,
            };
        }

        private ErrorLevel GetErrorLevel()
        {
            if (!IsValidDirectory)
                return ErrorLevel.Fatal;
            if (IsExistingMod)
                return ErrorLevel.Warning;
            return ErrorLevel.None;
        }

        private bool IsOutputDirectoryNonEmpty(BuildSettings settings)
        {
            return fs.Directory.Exists(settings.OutputDirectory) &&
                fs.Directory.EnumerateFiles(settings.OutputDirectory).Any();
        }

        private bool IsOutputDirectoryValid(BuildSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OutputModName))
                return false;
            try
            {
                if (!fs.Path.IsPathRooted(settings.OutputDirectory))
                    return false;
                var root = fs.Path.GetPathRoot(settings.OutputDirectory);
                return !InvalidPathRegex.IsMatch(settings.OutputDirectory[root.Length..]);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
