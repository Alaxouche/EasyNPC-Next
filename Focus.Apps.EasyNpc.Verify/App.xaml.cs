using System;
using System.Linq;
using System.Windows;
using Autofac;
using CommandLine;
using Focus.Apps.EasyNpc;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Main;
using Focus.Apps.EasyNpc.Reports;
using Serilog;

namespace Focus.Apps.EasyNpc.Verify
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Parser.Default.ParseArguments<CommandLineOptions>(e.Args)
                .WithParsed(Start)
                .WithNotParsed(_ => Fail("The command line arguments could not be parsed."));
        }

        private void Start(CommandLineOptions options)
        {
            // This app only ever runs the post-build verification.
            options.PostBuild = true;
            if (!string.IsNullOrEmpty(options.ReportPath))
                Settings.Default.BuildReportPath = options.ReportPath;

            try
            {
                var startupInfo = StartupInfo.Detect();
                var container = AppContainer.Build(options, startupInfo);
                var logger = container.Resolve<ILogger>();
                var window = new MainWindow(logger, container)
                {
                    Title = "EasyNPC Next - Post-Build Verify",
                };
                var viewModel = container.Resolve<PostBuildReportViewModel>();
                window.DataContext = viewModel;
                _ = viewModel.UpdateReport();
                window.Show();
            }
            catch (Exception ex)
            {
                Fail(
                    "The verifier could not start. It needs the same game and mod setup as EasyNPC, so launch it the " +
                    $"same way (usually from your mod manager).\n\n{ex.Message}");
            }
        }

        private void Fail(string message)
        {
            MessageBox.Show(message, "EasyNPC Next Verify", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
