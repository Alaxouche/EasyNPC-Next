using System.Diagnostics;
using System.IO;
using System.Windows;
using Focus.Apps.EasyNpc.Configuration;
using ModernWpf.Controls;

namespace Focus.Apps.EasyNpc.Debug
{
    /// <summary>
    /// Interaction logic for LogPage.xaml
    /// </summary>
    public partial class LogPage : Page
    {
        protected LogViewModel Model => ((ILogContainer)DataContext)!.Log;

        public LogPage()
        {
            InitializeComponent();
        }

        private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenInShell(ProgramData.LogFileName);
        }

        private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenInShell(ProgramData.DirectoryPath);
        }

        private static void OpenInShell(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
