using System.Windows;
using System.Windows.Controls;

namespace Focus.Apps.EasyNpc.Profiles
{
    /// <summary>
    /// Interaction logic for Mugshot.xaml
    /// </summary>
    public partial class MugshotViewer : UserControl
    {
        public MugshotViewer()
        {
            InitializeComponent();
        }

        private void NexusLink_Click(object sender, RoutedEventArgs e)
        {
            // Handle it so the click opens the mod page without also driving the card's double-click "apply" path.
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is MugshotViewModel vm && !string.IsNullOrEmpty(vm.NexusUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(vm.NexusUrl) { UseShellExecute = true });
                }
                catch
                {
                    // No browser or a blocked URL: nothing useful to do.
                }
            }
        }
    }
}
