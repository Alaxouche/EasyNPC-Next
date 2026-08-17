using Autofac;
using AutofacSerilogIntegration;
using Focus.Apps.EasyNpc.Configuration;
using Focus.Apps.EasyNpc.Debug;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;

namespace Focus.Apps.EasyNpc.Modules
{
    public class LoggingModule : Module
    {
        private const int MaxRetainedLogFiles = 10;

        public LogEventLevel Level { get; set; } = LogEventLevel.Information;
        public string LogFileName { get; set; } = "";

        protected override void Load(ContainerBuilder builder)
        {
            if (string.IsNullOrEmpty(LogFileName))
                throw new InvalidOperationException($"{nameof(LogFileName)} must be configured.");

            var loggingLevelSwitch = new LoggingLevelSwitch(Level);
            var logViewModelSink = new LogViewModelSink();
            var log = Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(loggingLevelSwitch)
                    .WriteTo.File(LogFileName,
                        buffered: true,
                        flushToDiskInterval: TimeSpan.FromMilliseconds(500),
                        outputTemplate:
                            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{ThreadId:D2}] [{Level:u3}] " +
                            "{Message:lj}{NewLine}{Exception}")
                    .WriteTo.Sink(logViewModelSink)
                    .Enrich.WithThreadId()
                    .CreateLogger();
            log.Information(
                "Initialized: {appName:l} version {version:l}, built on {buildDate}",
                AssemblyProperties.Name, AssemblyProperties.Version, AssemblyProperties.BuildTimestampUtc);
            if (Level <= LogEventLevel.Debug)
                log.Debug("Debug mode enabled");
            CleanUpOldLogs(log);

            builder.RegisterInstance(logViewModelSink).AsSelf();
            builder.RegisterType<LogViewModel>()
                .OnActivated(e => e.Context.Resolve<LogViewModelSink>().ViewModel = e.Instance)
                .SingleInstance();
            builder.RegisterLogger();
        }

        private void CleanUpOldLogs(ILogger log)
        {
            // Log files accumulate forever otherwise - one per session, and they are only ever read when something
            // went wrong recently. Keep the last few (the current session's file is always the newest).
            try
            {
                var logDirectory = Path.GetDirectoryName(Path.GetFullPath(LogFileName));
                if (string.IsNullOrEmpty(logDirectory))
                    return;
                var expiredLogFiles = Directory.GetFiles(logDirectory, "Log_*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(MaxRetainedLogFiles);
                foreach (var expiredLogFile in expiredLogFiles)
                {
                    try
                    {
                        File.Delete(expiredLogFile);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Probably held by another running instance; it will be cleaned up on a later run.
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to clean up old log files");
            }
        }
    }
}
