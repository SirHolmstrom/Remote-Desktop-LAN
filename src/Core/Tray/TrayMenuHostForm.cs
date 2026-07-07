using WinForms = System.Windows.Forms;

namespace Core.Tray;

/// <summary>
/// Invisible top-level owner used to give a left-clicked tray menu proper Windows
/// foreground ownership. Without an owner, clicks outside a manually shown menu do
/// not reliably dismiss it.
/// </summary>
internal sealed class TrayMenuHostForm : WinForms.Form
{
    private const int WsExToolWindow = 0x00000080;

    public TrayMenuHostForm()
    {
        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.Manual;
        Location = new System.Drawing.Point(-32000, -32000);
        Size = new System.Drawing.Size(1, 1);
        Opacity = 0;
    }

    protected override bool ShowWithoutActivation => true;

    protected override WinForms.CreateParams CreateParams
    {
        get
        {
            WinForms.CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }
}
