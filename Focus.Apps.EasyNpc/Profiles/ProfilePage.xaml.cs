using System.Windows;
using System.Windows.Input;

namespace Focus.Apps.EasyNpc.Profiles
{
    /// <summary>
    /// Interaction logic for ProfilePage.xaml
    /// </summary>
    public partial class ProfilePage : ModernWpf.Controls.Page
    {
        protected ProfileViewModel Model => ((IProfileContainer)DataContext)!.Profile;

        public ProfilePage()
        {
            InitializeComponent();
        }

        private void BatchAutoAssign_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var toChange = Model.CountFilteredFacesToAutoAssign();
            var total = Model.CountFilteredNpcs();
            if (toChange == 0)
            {
                MessageBox.Show(
                    owner,
                    $"All {total} NPC(s) currently shown by the filter already use their recommended face. " +
                    "Nothing to change.",
                    "Batch: auto-assign faces", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var confirm = MessageBox.Show(
                owner,
                $"Auto-assign the recommended face to the {total} NPC(s) currently shown by the filter?\n\n" +
                $"{toChange} of them would change; the rest already match. This uses the same recommendation as " +
                "\"Reset Face Selections\" (the last plugin that modifies each NPC's face, resolved to its source), " +
                "and does not touch the Default (behavior) plugin.\n\n" +
                "Tip: narrow the list first with the filters to control which NPCs are affected.",
                "Batch: auto-assign faces", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            var applied = Model.AutoAssignFilteredFaces();
            MessageBox.Show(
                owner, $"Auto-assigned the recommended face for {applied} NPC(s).",
                "Batch: auto-assign faces", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatchApplyFace_Click(object sender, RoutedEventArgs e)
        {
            var modName = Model.SelectedFaceModName;
            if (string.IsNullOrEmpty(modName))
                return;
            var count = Model.CountFilteredNpcs();
            var owner = Window.GetWindow(this);
            var confirm = MessageBox.Show(
                owner,
                $"Apply the face from \"{modName}\" to all {count} NPC(s) currently shown by the filter, " +
                "wherever that mod provides a face?\n\n" +
                "Tip: narrow the list first with the filters (for example \"Provided in\") to control which NPCs " +
                "are affected.",
                "Batch: apply face", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            var applied = Model.BatchApplySelectedFaceToFiltered();
            MessageBox.Show(
                owner, $"Applied \"{modName}\" as the face source for {applied} NPC(s).",
                "Batch: apply face", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatchSetDefault_Click(object sender, RoutedEventArgs e)
        {
            var pluginName = Model.SelectedDefaultPluginName;
            if (string.IsNullOrEmpty(pluginName))
                return;
            var count = Model.CountFilteredNpcs();
            var owner = Window.GetWindow(this);
            var confirm = MessageBox.Show(
                owner,
                $"Set \"{pluginName}\" as the Default Plugin (stats and behavior source) for all {count} NPC(s) " +
                "currently shown by the filter, wherever that plugin provides the NPC?\n\n" +
                "Tip: narrow the list first with the filters (for example \"Provided in\") to control which NPCs " +
                "are affected.",
                "Batch: set default plugin", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            var applied = Model.BatchSetDefaultToFiltered();
            MessageBox.Show(
                owner, $"Set \"{pluginName}\" as the default plugin for {applied} NPC(s).",
                "Batch: set default plugin", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatchResetVanilla_Click(object sender, RoutedEventArgs e)
        {
            var count = Model.CountFilteredNpcs();
            var owner = Window.GetWindow(this);
            var confirm = MessageBox.Show(
                owner,
                $"Reset all {count} NPC(s) currently shown by the filter back to their vanilla (base game) face?",
                "Batch: reset to vanilla", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;
            var applied = Model.BatchResetFilteredToVanilla();
            MessageBox.Show(
                owner, $"Reset {applied} NPC(s) to their vanilla face.",
                "Batch: reset to vanilla", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            Model.LoadFromFile(Window.GetWindow(this));
        }

        private void MugshotListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            if ((sender as FrameworkElement)?.DataContext is MugshotViewModel mugshot)
                Model.SelectedNpc?.TrySetFaceMod(mugshot, out _);
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            Model.SaveToFile(Window.GetWindow(this));
        }

        private void SetDefaultOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)e.Source).Tag is NpcOptionViewModel option)
                option.IsDefaultSource = true;
        }

        private void SetFaceOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)e.Source).Tag is NpcOptionViewModel option)
                option.IsFaceSource = true;
        }
    }
}
