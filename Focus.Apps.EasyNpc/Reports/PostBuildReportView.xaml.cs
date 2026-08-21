using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Focus.Apps.EasyNpc.Reports
{
    public partial class PostBuildReportView : UserControl
    {
        public PostBuildReportView()
        {
            InitializeComponent();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PostBuildReportViewModel viewModel)
                return;
            try
            {
                Clipboard.SetText(viewModel.BuildTextReport());
            }
            catch (Exception ex)
            {
                // The clipboard can be locked by another process; that's not worth an error dialog on its own, but the
                // user still needs to know the copy didn't happen.
                MessageBox.Show(
                    $"The report could not be copied to the clipboard.\n\n{ex.Message}",
                    "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PostBuildReportViewModel viewModel)
                return;
            var dialog = new SaveFileDialog
            {
                FileName = $"EasyNPC-Verify-{DateTime.Now:yyyyMMdd-HHmm}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Save verification report",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;
            try
            {
                File.WriteAllText(dialog.FileName, viewModel.BuildTextReport());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The report could not be saved.\n\n{ex.Message}",
                    "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RerunButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PostBuildReportViewModel viewModel)
                _ = viewModel.UpdateReport();
        }
    }
}
