using System.Security.Cryptography;
using System.Text;

namespace AuthenticatorChooser;

internal static class PinStorage {

    private static readonly string PIN_FILE_PATH = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        nameof(AuthenticatorChooser),
        "pin.dat");

    public static string? Load() {
        try {
            if (!File.Exists(PIN_FILE_PATH)) return null;
            byte[] encrypted = File.ReadAllBytes(PIN_FILE_PATH);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        } catch {
            return null;
        }
    }

    public static void Save(string pin) {
        Directory.CreateDirectory(Path.GetDirectoryName(PIN_FILE_PATH)!);
        byte[] plaintext = Encoding.UTF8.GetBytes(pin);
        byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PIN_FILE_PATH, encrypted);
    }

    public static void Clear() {
        try {
            File.Delete(PIN_FILE_PATH);
        } catch {
            // ignore if file doesn't exist or can't be deleted
        }
    }

}
