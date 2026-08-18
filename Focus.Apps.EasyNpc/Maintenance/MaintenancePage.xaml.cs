using Focus.Apps.EasyNpc.Profiles;
using System;
using System.Threading.Tasks;
using System.Windows;

using TKey = Mutagen.Bethesda.Plugins.FormKey;

namespace Focus.Apps.EasyNpc.Maintenance
{
    /// <summary>
    /// Interaction logic for MaintenancePage.xaml
    /// </summary>
    public partial class MaintenancePage : ModernWpf.Controls.Page
    {
        protected MaintenanceViewModel Model => ((IMaintenanceContainer)DataContext)!.Maintenance;

        public MaintenancePage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Model?.Refresh();
        }

        private void DeleteLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            Task.Run(() => model?.DeleteOldLogFiles());
        }

        private void FindUnusedModsButton_Click(object sender, RoutedEventArgs e)
        {
            // Runs on the UI thread: it updates a bound collection, and the lookups are in-memory (fast).
            Model?.FindUnusedAppearanceMods();
        }

        private async void EvolveToLoadOrderButton_Click(object sender, RoutedEventArgs e)
        {
            await PreviewAndReset(NpcProfileField.DefaultPlugin, "Default Plugin", m => m.ResetNpcDefaults());
        }

        private async void ResetFacesButton_Click(object sender, RoutedEventArgs e)
        {
            await PreviewAndReset(NpcProfileField.FacePlugin, "Face Selection", m => m.ResetNpcFaces());
        }

        // Non-destructive reset: compute and show exactly what would change, then only apply if the user confirms.
        private async Task PreviewAndReset(NpcProfileField field, string label, Action<MaintenanceViewModel> apply)
        {
            var model = Model;
            if (model is null)
                return;
            var owner = Window.GetWindow(this);
            var preview = await Task.Run(() => model.PreviewReset(field));
            if (preview.ChangeCount == 0)
            {
                MessageBox.Show(
                    owner, $"No NPCs would have their {label} changed. Nothing to do.",
                    $"Reset {label}", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var sample = string.Join(System.Environment.NewLine, preview.SampleChanges);
            var remaining = preview.ChangeCount - preview.SampleChanges.Count;
            var more = remaining > 0 ? $"{System.Environment.NewLine}...and {remaining} more." : "";
            var confirm = MessageBox.Show(
                owner,
                $"{preview.ChangeCount} of {preview.Considered} NPC(s) would have their {label} changed to match the " +
                $"current load order:{System.Environment.NewLine}{System.Environment.NewLine}{sample}{more}{System.Environment.NewLine}" +
                $"{System.Environment.NewLine}Apply these changes?",
                $"Reset {label} - preview", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            await Task.Run(() => apply(model));
        }

        private void TrimAutosaveButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            Task.Run(() => model?.TrimAutoSave());
        }
    }
}
