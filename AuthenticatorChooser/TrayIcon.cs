using NLog;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AuthenticatorChooser;

internal sealed class TrayIcon: IDisposable {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(TrayIcon).FullName!);

    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem skipItem;
    private readonly ToolStripMenuItem pinSetItem;
    private readonly ToolStripMenuItem pinClearItem;
    private readonly ToolStripMenuItem logEnableItem;
    private readonly ToolStripMenuItem logOpenItem;
    private readonly ToolStripMenuItem logClearItem;
    private readonly ChooserOptions options;

    public TrayIcon(ChooserOptions options) {
        this.options = options;

        skipItem = new ToolStripMenuItem("Skip all non-security-key options") {
            Checked      = options.skipAllNonSecurityKeyOptions,
            CheckOnClick = true
        };
        skipItem.CheckedChanged += onSkipToggled;

        pinSetItem = new ToolStripMenuItem("Set...") {
            Checked = options.pin != null
        };
        pinSetItem.Click += onPinSet;

        pinClearItem = new ToolStripMenuItem("Clear") {
            Enabled = options.pin != null
        };
        pinClearItem.Click += onPinClear;

        ToolStripMenuItem pinSubmenu = new("PIN") {
            DropDownItems = { pinSetItem, pinClearItem }
        };

        logEnableItem = new ToolStripMenuItem("Enable") {
            Checked      = Logging.IsFileLoggingEnabled,
            CheckOnClick = true
        };
        logEnableItem.CheckedChanged += onLogToggled;

        logOpenItem = new ToolStripMenuItem("Open") {
            Enabled = Logging.IsFileLoggingEnabled
        };
        logOpenItem.Click += onOpenLog;

        logClearItem = new ToolStripMenuItem("Clear") {
            Enabled = Logging.IsFileLoggingEnabled
        };
        logClearItem.Click += onClearLog;

        ToolStripMenuItem logSubmenu = new("Log") {
            DropDownItems = { logEnableItem, logOpenItem, logClearItem }
        };

        ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => Application.Exit();

        ContextMenuStrip menu = new() {
            Items = {
                skipItem,
                pinSubmenu,
                new ToolStripSeparator(),
                logSubmenu,
                new ToolStripSeparator(),
                exitItem
            }
        };

        notifyIcon = new NotifyIcon {
            Icon             = loadIcon(),
            Text             = nameof(AuthenticatorChooser),
            Visible          = true,
            ContextMenuStrip = menu
        };
    }

    internal void showSetPinDialog() {
        using PinInputDialog dialog = new();
        if (dialog.ShowDialog() != DialogResult.OK) return;

        string pin = dialog.Pin;
        PinStorage.Save(pin);
        options.pin = pin;
        options.autoSubmitPinLength = pin.Length;
        pinSetItem.Checked = true;
        pinClearItem.Enabled = true;
        LOGGER.Info("PIN saved ({0} characters)", pin.Length);
        Startup.updateAutostartIfRegistered(options);
    }

    private void onSkipToggled(object? sender, EventArgs e) {
        options.skipAllNonSecurityKeyOptions = skipItem.Checked;
        LOGGER.Info("Skip all non-security-key options toggled to {0}", skipItem.Checked);
        saveSettings();
        Startup.updateAutostartIfRegistered(options);
    }

    private void onPinSet(object? sender, EventArgs e) {
        if (Startup.isElevated()) {
            showSetPinDialog();
        } else {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName        = Environment.ProcessPath!,
                    Arguments       = Startup.buildCommandArgs(options, includeSetPin: true),
                    Verb            = "runas",
                    UseShellExecute = true
                });
                Application.Exit();
            } catch (Win32Exception) {
                // UAC denied, stay running
            }
        }
    }

    private void onPinClear(object? sender, EventArgs e) {
        PinStorage.Clear();
        options.pin = null;
        options.autoSubmitPinLength = null;
        pinSetItem.Checked = false;
        pinClearItem.Enabled = false;
        LOGGER.Info("PIN cleared");
        Startup.updateAutostartIfRegistered(options);
    }

    private void onLogToggled(object? sender, EventArgs e) {
        bool enabled = logEnableItem.Checked;
        Logging.setFileLoggingEnabled(enabled);
        logOpenItem.Enabled = enabled;
        logClearItem.Enabled = enabled;
        LOGGER.Info("File logging {0}", enabled ? "enabled" : "disabled");
        saveSettings();
        Startup.updateAutostartIfRegistered(options);
    }

    private static void onOpenLog(object? sender, EventArgs e) {
        try {
            Process.Start(new ProcessStartInfo(Logging.LogFilePath) { UseShellExecute = true });
        } catch {
            // ignore if file doesn't exist
        }
    }

    private static void onClearLog(object? sender, EventArgs e) {
        Logging.clearLogFile();
    }

    private void saveSettings() {
        new Settings {
            skipAllNonSecurityKeyOptions = options.skipAllNonSecurityKeyOptions,
            logEnabled                   = Logging.IsFileLoggingEnabled
        }.Save();
    }

    private static Icon loadIcon() {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuthenticatorChooser.YubiKey.ico")!;
        return new Icon(stream);
    }

    public void Dispose() {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

}
