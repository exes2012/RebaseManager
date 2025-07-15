using Serilog;
using System;
using System.IO;

namespace RebaseProjectWithTemplate.Infrastructure
{
    public static class LogHelper
    {
        private static ILogger _logger;

        public static void Initialize()
        {
            var loggerPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(loggerPath, "Anguleris Technologies", "RebaseProjectWithTemplate", "Logs", "log-{Date}.txt");
            _logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .WriteTo.RollingFile(path)
                .CreateLogger();
        }

        private static ILogger Logger
        {
            get
            {
                // If the plugin is not installed, initialize the debug logger
                if (_logger == null) Initialize();
                return _logger;
            }
        }

        public static void Information(string message) => Logger?.Information(message);
        public static void Error(string message) => Logger?.Error(message);
        public static void Warning(string message) => Logger?.Warning(message);
        public static void Debug(string message) => Logger?.Debug(message);
    }
}
