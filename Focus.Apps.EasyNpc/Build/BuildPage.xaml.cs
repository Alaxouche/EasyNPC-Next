using Focus.Apps.EasyNpc.Configuration;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Focus.Apps.EasyNpc.Build
{
    /// <summary>
    /// Interaction logic for BuildPage.xaml
    /// </summary>
    public partial class BuildPage : ModernWpf.Controls.Page
    {
        protected BuildViewModel Model => ((IBuildContainer)DataContext)!.Build;

        public BuildPage()
        {
            InitializeComponent();
        }

        private void HyperlinkButton_Click(object sender, RoutedEventArgs e)
        {
            Model.OpenBuildOutput();
        }

        private void LogLink_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(ProgramData.LogFileName))
                return;
            var psi = new ProcessStartInfo() { FileName = ProgramData.LogFileName, UseShellExecute = true };
            Process.Start(psi);
        }

        private void BuildPreviewView_BuildClick(object sender, System.EventArgs e)
        {
            Model.BeginBuild();
        }
    }
}
