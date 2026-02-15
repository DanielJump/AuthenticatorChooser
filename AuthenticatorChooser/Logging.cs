using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace AuthenticatorChooser;

internal static class Logging {

    private static readonly SimpleLayout MESSAGE_FORMAT = new(
        " ${level:format=FirstCharacter:lowercase=true} | ${date:format=yyyy-MM-dd HH\\:mm\\:ss.fff} | ${logger:shortName=true:padding=-25} | ${message:withException=true:exceptionSeparator=\n}");

    private static readonly LogLevel LOG_LEVEL = LogLevel.Debug;

    public static string LogFilePath { get; private set; } = null!;
    public static bool IsFileLoggingEnabled { get; private set; }

    public static void initialize(bool enableFileAppender, string? logFilename) {
        LogFilePath = logFilename != null ? Environment.ExpandEnvironmentVariables(logFilename) : Path.Combine(Path.GetTempPath(), Path.ChangeExtension(nameof(AuthenticatorChooser), ".log"));

        LoggingConfiguration logConfig = new();

        if (enableFileAppender) {
            appendSeparatorIfLogExists();
            IsFileLoggingEnabled = true;
            logConfig.AddRule(LOG_LEVEL, LogLevel.Fatal, createFileTarget());
        }

        logConfig.AddRule(LOG_LEVEL, LogLevel.Fatal, new ConsoleTarget("consoleAppender") {
            Layout                 = MESSAGE_FORMAT,
            DetectConsoleAvailable = true
        });

        LogManager.Configuration = logConfig;
    }

    public static void setFileLoggingEnabled(bool enabled) {
        var config = LogManager.Configuration!;

        if (enabled && !IsFileLoggingEnabled) {
            appendSeparatorIfLogExists();
            config.AddRule(LOG_LEVEL, LogLevel.Fatal, createFileTarget());
        } else if (!enabled && IsFileLoggingEnabled) {
            foreach (var rule in config.LoggingRules.Where(r => r.Targets.Any(t => t.Name == "fileAppender")).ToList()) {
                config.LoggingRules.Remove(rule);
            }
        }

        IsFileLoggingEnabled = enabled;
        LogManager.ReconfigExistingLoggers();
    }

    public static void clearLogFile() {
        try {
            if (File.Exists(LogFilePath)) {
                File.WriteAllText(LogFilePath, string.Empty);
            }
        } catch {
            // ignore
        }
    }

    private static void appendSeparatorIfLogExists() {
        try {
            if (File.Exists(LogFilePath)) {
                File.AppendAllText(LogFilePath, Environment.NewLine);
            }
        } catch {
            // ignore
        }
    }

    private static FileTarget createFileTarget() => new("fileAppender") {
        Layout   = MESSAGE_FORMAT,
        FileName = LogFilePath
    };

}
