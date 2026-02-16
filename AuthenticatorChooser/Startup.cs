using AuthenticatorChooser.WindowOpening;
using AuthenticatorChooser.Windows11;
using ManagedWinapi.Windows;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Conventions;
using Microsoft.Win32;
using NLog;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

// ReSharper disable ClassNeverInstantiated.Global - it's actually instantiated by McMaster.Extensions.CommandLineUtils
// ReSharper disable UnassignedGetOnlyAutoProperty - it's actually assigned by McMaster.Extensions.CommandLineUtils

namespace AuthenticatorChooser;

public class Startup {

    private const string PROGRAM_NAME  = nameof(AuthenticatorChooser);
    private const int    MIN_PIN_LENGTH = 4;

    private static readonly string                  PROGRAM_VERSION = Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3);
    private static readonly CancellationTokenSource EXITING_TRIGGER = new();
    public static readonly  CancellationToken       EXITING         = EXITING_TRIGGER.Token;

    private static Logger? logger;

    // #15
    [Option("--skip-all-non-security-key-options", CommandOptionType.NoValue)]
    public bool skipAllNonSecurityKeyOptions { get; }

    // #30
    [Option("--autosubmit-pin-length", CommandOptionType.SingleValue)]
    public int? autosubmitPinLength { get; }

    [Option("--autostart-on-logon", CommandOptionType.NoValue)]
    public bool autostartOnLogon { get; }

    [Option("-l|--log", CommandOptionType.SingleOrNoValue)]
    public (bool enabled, string? filename) log { get; }

    [Option("--prompt-for-pin", CommandOptionType.NoValue)]
    public bool promptForPin { get; }

    [Option(DefaultHelpOptionConvention.DefaultHelpTemplate, CommandOptionType.NoValue)]
    public bool help { get; }

    [STAThread]
    public static int Main(string[] args) {
        try {
            using var app = new CommandLineApplication<Startup> {
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw
            };
            app.Conventions.UseDefaultConventions();
            return app.Execute(args);
        } catch (CommandParsingException e) {
            MessageBox.Show(e.Message, $"{PROGRAM_NAME} {PROGRAM_VERSION}", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // ReSharper disable once UnusedMember.Global - it's actually invoked by McMaster.Extensions.CommandLineUtils
    // ReSharper disable once InconsistentNaming - it must be named this, as dictated by McMaster.Extensions.CommandLineUtils, it's not my choice
    public int OnExecute() {
        Application.SetCompatibleTextRenderingDefault(false);
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Logging.initialize(log.enabled, log.filename);
        logger = LogManager.GetLogger(typeof(Startup).FullName!);

        try {
            if (help) {
                showUsage();
                return 0;
            }

            if (autostartOnLogon) {
                registerAsStartupProgram();
            }

            if (PinStorage.Load() != null && !isElevated()) {
                logger.Info("Not running as administrator, attempting to relaunch elevated");
                if (tryRelaunchElevated(buildCommandArgsFromOptions())) {
                    return 0;
                }
                logger.Warn("UAC elevation denied, continuing without administrator privileges");
            }

            string mutexName = $@"Local\{PROGRAM_NAME}_{WindowsIdentity.GetCurrent().User?.Value}";
            using Mutex singleInstanceLock = new(false, mutexName);
            bool acquired;
            try {
                acquired = singleInstanceLock.WaitOne(promptForPin ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
            } catch (AbandonedMutexException) {
                acquired = true;
            }
            if (!acquired) {
                logger.Warn("Another instance of {program} is already running for this user, this instance is exiting now.", PROGRAM_NAME);
                return 2;
            }

            try {
                logger.Info("{name} {version} starting{elevated}", PROGRAM_NAME, PROGRAM_VERSION, isElevated() ? " (elevated)" : "");
                OsVersion os = OsVersion.getCurrent();
                logger.Info("Operating system is {name} {marketingVersion} {version} {arch}", os.name, os.marketingVersion, os.version, os.architecture);
                logger.Info("{Locales are} {locales}", I18N.LOCALE_NAMES.Count == 1 ? "Locale is" : "Locales are", string.Join(", ", I18N.LOCALE_NAMES));

                Settings savedSettings = Settings.Load();

                if (!log.enabled && savedSettings.logEnabled) {
                    Logging.setFileLoggingEnabled(true);
                }

                bool effectiveSkipAll = skipAllNonSecurityKeyOptions || savedSettings.skipAllNonSecurityKeyOptions;

                string? pin = null;
                string? storedPin = PinStorage.Load();
                if (storedPin != null) {
                    if (storedPin.Length < MIN_PIN_LENGTH) {
                        logger.Error("Stored PIN is less than {0} characters long, ignoring it", MIN_PIN_LENGTH);
                    } else {
                        pin = storedPin;
                        logger.Info("PIN loaded from encrypted storage ({0} characters)", pin.Length);
                    }
                }

                int? effectiveAutoSubmitPinLength = pin != null ? pin.Length : autosubmitPinLength;

                ChooserOptions chooserOptions = new(effectiveSkipAll, effectiveAutoSubmitPinLength, pin);

                using WindowOpeningListener windowOpeningListener = new WindowOpeningListenerImpl();
                WindowsSecurityKeyChooser   securityKeyChooser    = new(chooserOptions);

                windowOpeningListener.windowOpened += (_, window) => securityKeyChooser.chooseUsbSecurityKey(window);

                foreach (SystemWindow fidoPromptWindow in SystemWindow.FilterToplevelWindows(securityKeyChooser.isFidoPromptWindow)) {
                    securityKeyChooser.chooseUsbSecurityKey(fidoPromptWindow);
                }

                logger.Info("Waiting for Windows Security FIDO dialog boxes to open");

                _ = I18N.getStrings(I18N.Key.SMARTPHONE); // ensure localization is loaded eagerly

                Console.CancelKeyPress += (_, args) => {
                    args.Cancel = true;
                    EXITING_TRIGGER.Cancel();
                    Application.Exit();
                };

                SystemEvents.SessionEnding += onWindowsLogoff;

                using TrayIcon trayIcon = new(chooserOptions);

                if (promptForPin) {
                    trayIcon.showSetPinDialog();
                }

                Application.Run();
            } finally {
                singleInstanceLock.ReleaseMutex();
            }

            return 0;
        } catch (Exception e) when (e is not OutOfMemoryException) {
            logger.Error(e, "Uncaught exception");
            MessageBox.Show($"Uncaught exception: {e}", PROGRAM_NAME, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        } finally {
            LogManager.Shutdown();
        }
    }

    internal static bool isElevated() {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static string buildCommandArgs(ChooserOptions options, bool includePromptForPin = false) {
        StringBuilder args = new();
        if (options.skipAllNonSecurityKeyOptions) args.Append(" --skip-all-non-security-key-options");
        if (Logging.IsFileLoggingEnabled) args.Append(" --log");
        if (includePromptForPin) args.Append(" --prompt-for-pin");
        return args.ToString().TrimStart();
    }

    internal static void updateAutostartIfRegistered(ChooserOptions options) {
        object? existing = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", PROGRAM_NAME, null);
        if (existing == null) return;

        StringBuilder cmd = new();
        cmd.Append('"').Append(Environment.ProcessPath).Append('"');
        if (options.skipAllNonSecurityKeyOptions) cmd.Append(" --skip-all-non-security-key-options");
        if (Logging.IsFileLoggingEnabled) cmd.Append(" --log");
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", PROGRAM_NAME, cmd.ToString());
    }

    private static bool tryRelaunchElevated(string arguments) {
        try {
            Process.Start(new ProcessStartInfo {
                FileName        = Environment.ProcessPath!,
                Arguments       = arguments,
                Verb            = "runas",
                UseShellExecute = true
            });
            return true;
        } catch (Win32Exception) {
            return false;
        }
    }

    private string buildCommandArgsFromOptions() {
        StringBuilder args = new();
        if (skipAllNonSecurityKeyOptions) args.Append(" --skip-all-non-security-key-options");
        if (autosubmitPinLength.HasValue) args.Append($" --autosubmit-pin-length={autosubmitPinLength.Value}");
        if (log.enabled) args.Append(log.filename != null ? $" --log={log.filename}" : " --log");
        return args.ToString().TrimStart();
    }

    private void registerAsStartupProgram() {
        StringBuilder autostartCommand = new();
        autostartCommand.Append('"').Append(Environment.ProcessPath).Append('"');
        if (skipAllNonSecurityKeyOptions) {
            autostartCommand.Append(' ').Append("--skip-all-non-security-key-options");
        }
        if (log.enabled) {
            autostartCommand.Append(' ').Append("--log");
        }
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", PROGRAM_NAME, autostartCommand.ToString());
        MessageBox.Show($"{PROGRAM_NAME} is now running in the background, and will also start automatically each time you log in to Windows.", PROGRAM_NAME, MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void showUsage() {
        string processFilename = Path.GetFileName(Environment.ProcessPath)!;
        MessageBox.Show(
            $"""
             {processFilename}
                 Runs this program in the background, waiting for FIDO credential dialog boxes to open and choosing the Security Key option each time.

             {processFilename} --autostart-on-logon
                 Registers this program to start automatically every time the current user logs on to Windows, and also leaves it running in the background like the first example.

             {processFilename} --skip-all-non-security-key-options
                 Forces this program to choose the Security Key option even if there are other valid options, such as an already-paired phone or Windows Hello PIN or biometrics. By default, without this option, it will only choose the Security Key if the sole other option is pairing a new phone. This is an aggressive behavior, so if it skips an option you need, remember that you can hold Shift when the FIDO prompt appears to temporarily disable this program and manually choose a different option.

             {processFilename} --autosubmit-pin-length=$num
                 When Windows prompts you for the FIDO PIN for your USB security key, automatically submit the dialog once you have typed a PIN that is $num characters long (minimum 4), instead of you manually pressing Enter. Remember that enough consecutive incorrect submissions (8 on YubiKeys) will permanently block the security key until you reset it and lose all its FIDO credentials, so type with care. This will neither autosubmit PINs when registering a new FIDO credential, changing your PIN, or entering a Windows Hello PIN (which Windows autosubmits without this program's help).

             {processFilename} --log[=$filename]
                 Runs this program in the background like the first example, and logs debug messages to a text file. If you don't specify $filename, it goes to {Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? "%TEMP%", PROGRAM_NAME + ".log")}.

             {processFilename} --help
                 Shows this usage.

             For more information, see https://github.com/Aldaviva/{PROGRAM_NAME}.
             Press Ctrl+C to copy this message.
             """, $"{PROGRAM_NAME} {PROGRAM_VERSION} usage", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void onWindowsLogoff(object sender, SessionEndingEventArgs args) {
        logger?.Info("Exiting due to Windows session ending for {0}", args.Reason);
        SystemEvents.SessionEnding -= onWindowsLogoff;
        Application.Exit();
    }

}
