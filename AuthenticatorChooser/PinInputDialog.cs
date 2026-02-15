using System.Windows.Forms;

namespace AuthenticatorChooser;

internal sealed class PinInputDialog: Form {

    private const int MIN_PIN_LENGTH = 4;

    private readonly TextBox pinTextBox;
    private readonly Button okButton;

    public string Pin => pinTextBox.Text;

    public PinInputDialog() {
        Text            = nameof(AuthenticatorChooser);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new System.Drawing.Size(300, 100);
        ShowInTaskbar   = false;

        Label label = new() {
            Text     = "Enter your FIDO PIN:",
            Location = new System.Drawing.Point(12, 12),
            AutoSize = true
        };

        pinTextBox = new TextBox {
            Location              = new System.Drawing.Point(12, 36),
            Size                  = new System.Drawing.Size(242, 23),
            UseSystemPasswordChar = true
        };
        pinTextBox.TextChanged += (_, _) => okButton!.Enabled = pinTextBox.Text.Length >= MIN_PIN_LENGTH;

        Button revealButton = new() {
            Text      = "\ud83d\udc41",
            Location  = new System.Drawing.Point(256, 35),
            Size      = new System.Drawing.Size(26, 25),
            FlatStyle = FlatStyle.Flat,
            TabStop   = false
        };
        revealButton.FlatAppearance.BorderSize = 0;
        revealButton.MouseDown += (_, _) => pinTextBox.UseSystemPasswordChar = false;
        revealButton.MouseUp   += (_, _) => pinTextBox.UseSystemPasswordChar = true;

        okButton = new Button {
            Text         = "OK",
            DialogResult = DialogResult.OK,
            Location     = new System.Drawing.Point(126, 66),
            Size         = new System.Drawing.Size(75, 23),
            Enabled      = false
        };

        Button cancelButton = new() {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location     = new System.Drawing.Point(207, 66),
            Size         = new System.Drawing.Size(75, 23)
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(label);
        Controls.Add(pinTextBox);
        Controls.Add(revealButton);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
    }

}
