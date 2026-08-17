using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Debug;
using Serilog;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Focus.Apps.EasyNpc.Main
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string PlacementFilePath => Path.Combine(ProgramData.DirectoryPath, "window.json");

        private readonly IDisposable container;

        public MainWindow(ILogger logger, IDisposable container)
        {
            this.container = container;
            InitializeComponent();
            RestorePlacement();
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    logger.Error(e.ExceptionObject as Exception, "Exception was not handled");
                    var crashViewModel = new CrashViewModel(
                        ProgramData.DirectoryPath, Path.GetFileName(ProgramData.LogFileName));
                    var errorWindow = new ErrorWindow { DataContext = crashViewModel, Owner = this };
                    errorWindow.ShowDialog();
                }
                catch (Exception)
                {
                    // The ship is going down and we're out of lifeboats.
                }
                container.Dispose();
                Application.Current.Shutdown();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChromeFix.Install(this);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SavePlacement();
            container.Dispose();
            Application.Current.Shutdown();
        }

        private sealed class WindowPlacement
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool Maximized { get; set; }
        }

        // Restore the size, position and maximized state from the last session. Falls back to the XAML defaults if the
        // saved bounds are missing, too small, or off every screen (e.g. a monitor was unplugged).
        private void RestorePlacement()
        {
            try
            {
                if (!File.Exists(PlacementFilePath))
                    return;
                var p = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(PlacementFilePath));
                if (p is null || p.Width < MinWidth || p.Height < MinHeight)
                    return;
                Width = p.Width;
                Height = p.Height;
                var screen = new Rect(
                    SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var bounds = new Rect(p.Left, p.Top, p.Width, p.Height);
                bounds.Intersect(screen);
                if (bounds.Width >= 100 && bounds.Height >= 100)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = p.Left;
                    Top = p.Top;
                }
                if (p.Maximized)
                    WindowState = WindowState.Maximized;
            }
            catch
            {
                // A bad or unreadable placement file should never stop the app from opening.
            }
        }

        private void SavePlacement()
        {
            try
            {
                // RestoreBounds gives the normal (un-maximized) rectangle, so a maximized window still reopens at a
                // sensible size when the user restores it.
                var rect = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;
                var p = new WindowPlacement
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height,
                    Maximized = WindowState == WindowState.Maximized,
                };
                Directory.CreateDirectory(ProgramData.DirectoryPath);
                File.WriteAllText(PlacementFilePath, JsonSerializer.Serialize(p));
            }
            catch
            {
                // Not being able to remember the window size is not worth interrupting shutdown.
            }
        }
    }
}
