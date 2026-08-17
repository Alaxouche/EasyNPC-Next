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

        private void RerunButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PostBuildReportViewModel viewModel)
                _ = viewModel.UpdateReport();
        }
    }
}
