using WinForms = System.Windows.Forms;

namespace Core.Tray;

public sealed record PasswordChange(string CurrentPassword, string NewPassword);

public static class PromptDialogs
{
    public static PasswordChange? ShowChangePassword()
    {
        using var form = new WinForms.Form
        {
            Text = "Change Password",
            Width = 390,
            Height = 245,
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
            StartPosition = WinForms.FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var current = PasswordBox(62);
        var next = PasswordBox(112);
        var confirm = PasswordBox(162);
        form.Controls.AddRange(new WinForms.Control[]
        {
            Label("Current password", 15, 42),
            Label("New password", 15, 92),
            Label("Confirm password", 15, 142),
            current,
            next,
            confirm
        });

        var ok = new WinForms.Button
        {
            Text = "Change",
            DialogResult = WinForms.DialogResult.OK,
            Left = 205,
            Top = 177,
            Width = 75
        };
        var cancel = new WinForms.Button
        {
            Text = "Cancel",
            DialogResult = WinForms.DialogResult.Cancel,
            Left = 290,
            Top = 177,
            Width = 75
        };
        form.Controls.AddRange(new WinForms.Control[] { ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != WinForms.DialogResult.OK) return null;
        if (next.Text.Length < 12)
        {
            ShowWarning("The new password must be at least 12 characters.");
            return null;
        }
        if (!string.Equals(next.Text, confirm.Text, StringComparison.Ordinal))
        {
            ShowWarning("The new passwords do not match.");
            return null;
        }

        return new(current.Text, next.Text);
    }

    public static void ShowInfo(string message) => WinForms.MessageBox.Show(
        message, "RemoteDesktopLAN", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);

    public static void ShowWarning(string message) => WinForms.MessageBox.Show(
        message, "RemoteDesktopLAN", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);

    public static void ShowError(string message) => WinForms.MessageBox.Show(
        message, "RemoteDesktopLAN", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);

    public static bool Confirm(string message) => WinForms.MessageBox.Show(
        message, "RemoteDesktopLAN", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning)
        == WinForms.DialogResult.Yes;

    private static WinForms.Label Label(string text, int left, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Left = left,
        Top = top
    };

    private static WinForms.TextBox PasswordBox(int top) => new()
    {
        Left = 140,
        Top = top - 4,
        Width = 225,
        UseSystemPasswordChar = true
    };
}
