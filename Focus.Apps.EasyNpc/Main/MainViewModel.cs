using Focus.Apps.EasyNpc.Build;
using Focus.Apps.EasyNpc.Build.Preview;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Debug;
using Focus.Apps.EasyNpc.Maintenance;
using Focus.Apps.EasyNpc.Messages;
using Focus.Apps.EasyNpc.Profiles;
using Focus.Apps.EasyNpc.Reports;
using PropertyChanged;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Focus.Apps.EasyNpc.Main
{
    [AddINotifyPropertyChangedInterface]
    public class MainViewModel :
        IProfileContainer, IBuildContainer, IMaintenanceContainer, ISettingsContainer, ILogContainer
    {
        public class NavigationMenuItem
        {
            // Shown under the page title in the header.
            public string Description { get; private init; }
            public FontAwesome5.EFontAwesomeIcon Icon { get; private init; }
            public string Name { get; private init; }
            // Stupid hack for https://github.com/Kinnara/ModernWpf/issues/389
            // Only workaround seems to be to use a two-way binding, but changing the type isn't actually supported.
            public Type PageType { get => pageType; set { } }
            public object ViewModel { get; private init; }

            private readonly Type pageType;

            public NavigationMenuItem(
                string name, object viewModel, Type pageType,
                FontAwesome5.EFontAwesomeIcon icon = FontAwesome5.EFontAwesomeIcon.None,
                string description = "")
            {
                this.pageType = pageType;

                Description = description;
                Icon = icon;
                Name = name;
                ViewModel = viewModel;
            }
        }

        public delegate MainViewModel Factory(bool isFirstLaunch);

        [AllowNull] // Not accessed until after load
        public BuildViewModel Build { get; private set; }
        public object Content { get; private set; }
        public LoaderViewModel Loader { get; private init; }
        public LogViewModel Log { get; private init; }
        public ILogger Logger { get; private init; }
        [AllowNull] // Not accessed until after load
        public MaintenanceViewModel Maintenance { get; private set; }
        // Utility pages, shown in the pane footer.
        public IReadOnlyList<NavigationMenuItem> FooterNavigationMenuItems { get; private set; } =
            new List<NavigationMenuItem>().AsReadOnly();
        public IReadOnlyList<NavigationMenuItem> NavigationMenuItems { get; private set; } =
            new List<NavigationMenuItem>().AsReadOnly();
        [AllowNull] // Not accessed until after load
        public ProfileViewModel Profile { get; private set; }
        public NavigationMenuItem? SelectedNavigationMenuItem { get; set; }
        public SettingsViewModel Settings { get; private init; }
        [AllowNull] // Not accessed until after load
        public StartupReportViewModel StartupReport { get; private set; }

        public bool IsSettingsNavigationItemSelected
        {
            get { return SelectedNavigationMenuItem == settingsNavigationMenuItem; }
            set { SelectedNavigationMenuItem = settingsNavigationMenuItem; }
        }

        private readonly NavigationMenuItem settingsNavigationMenuItem;

        public MainViewModel(
            LoaderViewModel loader,
            LogViewModel log,
            SettingsViewModel settings,
            StartupReportViewModel.Factory startupReportFactory,
            ProfileViewModel.Factory profileFactory,
            BuildViewModel.Factory buildFactory,
            BuildPreviewViewModel.Factory buildPreviewFactory,
            MaintenanceViewModel.Factory maintenanceFactory,
            IMessageBus messageBus,
            ILogger logger,
            bool isFirstLaunch)
        {
            Loader = loader;
            Log = log;
            Logger = logger;
            Settings = settings;

            Settings.IsWelcomeScreen = isFirstLaunch;
            Settings.WelcomeAcked += (sender, e) =>
            {
                Content = Loader;
            };

            settingsNavigationMenuItem =
                new("Settings", Settings, typeof(SettingsPage), FontAwesome5.EFontAwesomeIcon.Solid_Cog,
                    "Configure how EasyNPC Next finds your mods and builds output");

            Content = isFirstLaunch ? Settings : Loader;

            Loader.Loaded += async () =>
            {
                var loadOrderAnalysis = await Loader.Tasks!.LoadOrderAnalysis;
                var profileModel = await Loader.Tasks!.Profile.ConfigureAwait(false);
                Profile = profileFactory(profileModel);
                StartupReport = startupReportFactory(profileModel, loadOrderAnalysis);
                var preview = buildPreviewFactory(profileModel, loadOrderAnalysis);
                Build = buildFactory(preview);
                Maintenance = maintenanceFactory(profileModel);

                NavigationMenuItems = new List<NavigationMenuItem>
                {
                    new NavigationMenuItem(
                        "Profile", Profile, typeof(ProfilePage), FontAwesome5.EFontAwesomeIcon.Solid_Users,
                        "Choose each NPC's appearance"),
                    new NavigationMenuItem(
                        "Build", Build, typeof(BuildPage), FontAwesome5.EFontAwesomeIcon.Solid_Hammer,
                        "Review your choices and generate the merged plugin"),
                    new NavigationMenuItem(
                        "Maintenance", Maintenance, typeof(MaintenancePage),
                        FontAwesome5.EFontAwesomeIcon.Solid_Wrench,
                        "Sync and fix up your saved NPC choices"),
                }.AsReadOnly();
                FooterNavigationMenuItems = new List<NavigationMenuItem>
                {
                    new NavigationMenuItem(
                        "Log", Log, typeof(LogPage), FontAwesome5.EFontAwesomeIcon.Solid_FileAlt,
                        "Diagnostic output for troubleshooting"),
                }.AsReadOnly();
                SelectedNavigationMenuItem = NavigationMenuItems[0];

                Content = StartupReport.HasErrors ? StartupReport : this;
            };

            messageBus.Subscribe<NavigateToPage>(message =>
            {
                if (message.Page == MainPage.Settings)
                    IsSettingsNavigationItemSelected = true;
                else
                {
                    var item = GetPageNavigationItem(message.Page);
                    if (item is not null)
                        SelectedNavigationMenuItem = item;
                }
            });

            messageBus.Subscribe<JumpToNpc>(message =>
            {
                var found = Profile.SelectNpc(message.Key);
                if (found)
                    messageBus.Send(new NavigateToPage(MainPage.Profile));
            });
        }

        public void DismissStartupErrors()
        {
            Content = this;
        }

        private NavigationMenuItem? GetPageNavigationItem(MainPage page)
        {
            var viewModel = GetPageViewModel(page);
            return viewModel is not null ?
                NavigationMenuItems.Concat(FooterNavigationMenuItems).SingleOrDefault(x => x.ViewModel == viewModel) :
                null;
        }

        private object? GetPageViewModel(MainPage page) => page switch
        {
            MainPage.Profile => Profile,
            MainPage.Build => Build,
            MainPage.Maintenance => Maintenance,
            MainPage.Log => Log,
            _ => null
        };
    }
}