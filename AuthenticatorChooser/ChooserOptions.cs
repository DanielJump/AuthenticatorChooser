using System.Diagnostics;

namespace AuthenticatorChooser;

public class ChooserOptions(bool skipAllNonSecurityKeyOptions, int? autoSubmitPinLength, string? pin) {

    public bool skipAllNonSecurityKeyOptions { get; set; } = skipAllNonSecurityKeyOptions;
    public int? autoSubmitPinLength { get; set; } = autoSubmitPinLength;
    public string? pin { get; set; } = pin;
    public Stopwatch overallStopwatch { get; } = new();

}
