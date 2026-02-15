using System.Text.Json;

namespace AuthenticatorChooser;

internal sealed class Settings {

    private static readonly string SETTINGS_PATH = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        nameof(AuthenticatorChooser),
        "settings.json");

    public bool skipAllNonSecurityKeyOptions { get; set; }
    public bool logEnabled { get; set; }

    public static Settings Load() {
        try {
            if (!File.Exists(SETTINGS_PATH)) return new Settings();
            string json = File.ReadAllText(SETTINGS_PATH);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        } catch {
            return new Settings();
        }
    }

    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SETTINGS_PATH)!);
            File.WriteAllText(SETTINGS_PATH, JsonSerializer.Serialize(this));
        } catch {
            // ignore
        }
    }

}
