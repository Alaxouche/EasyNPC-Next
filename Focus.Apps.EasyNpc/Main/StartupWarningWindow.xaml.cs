using Focus.Apps.EasyNpc.Configuration;
using Ookii.Dialogs.Wpf;
using System;
using System.Windows;

namespace Focus.Apps.EasyNpc.Main
{
    /// <summary>
    /// Interaction logic for StartupWarningWindow.xaml
    /// </summary>
    public partial class StartupWarningWindow : Window
    {
        public StartupWarningWindow()
        {
            InitializeComponent();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        // Lets a user whose game can't be auto-detected finish setup from the dialog that told them so, instead of
        // being sent away to find a command-line flag. The path is only read at startup, so the app has to be started
        // again afterwards - which is also what mod manager users need to do anyway (relaunch through the manager).
        private void SelectGameFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not MissingGameDataContent content)
                return;
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Select your game's Data folder (the one containing Skyrim.esm)",
                UseDescriptionForTitle = true,
            };
            if (dialog.ShowDialog(this) != true)
                return;
            var problem = GameFolder.Validate(dialog.SelectedPath);
            if (!string.IsNullOrEmpty(problem))
            {
                content.IsSelectionSuccessful = false;
                content.SelectionResult = problem;
                return;
            }
            var dataFolder = GameFolder.ResolveDataFolder(dialog.SelectedPath)!;
            try
            {
                Settings.Default.GameDataDirectory = dataFolder;
                Settings.Default.Save();
            }
            catch (Exception ex)
            {
                content.IsSelectionSuccessful = false;
                content.SelectionResult = $"The setting could not be saved: {ex.Message}";
                return;
            }
            content.IsSelectionSuccessful = true;
            content.SelectionResult =
                $"Saved: {dataFolder}\nClose this window and start the app again (from your mod manager, if you use one).";
        }
    }
}
