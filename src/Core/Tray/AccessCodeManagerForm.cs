using Core.Config;
using Core.Hosting;
using Core.Security;
using WinForms = System.Windows.Forms;

namespace Core.Tray;

internal sealed class AccessCodeManagerForm : WinForms.Form
{
    private sealed record DurationChoice(string Label, int Minutes)
    {
        public override string ToString() => Label;
    }

    private readonly RemoteDesktopHost m_Host;
    private readonly AppConfig m_Config;
    private readonly WinForms.DataGridView m_Grid = new();
    private readonly WinForms.ComboBox m_Access = new();
    private readonly WinForms.ComboBox m_Duration = new();
    private readonly WinForms.CheckBox m_UseAsDefault = new();
    private readonly WinForms.Timer m_RefreshTimer = new() { Interval = 1000 };

    public AccessCodeManagerForm(RemoteDesktopHost host, AppConfig config)
    {
        m_Host = host;
        m_Config = config;

        Text = "Guest Access Codes";
        Width = 760;
        Height = 470;
        MinimumSize = new System.Drawing.Size(680, 400);
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        ShowInTaskbar = false;

        BuildLayout();
        RefreshRows();
        m_RefreshTimer.Tick += (_, _) => RefreshRows();
        m_RefreshTimer.Start();
        FormClosed += (_, _) => m_RefreshTimer.Stop();
    }

    private void BuildLayout()
    {
        var createPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Top,
            Height = 72,
            Padding = new WinForms.Padding(10, 10, 10, 6),
            WrapContents = false
        };

        m_Access.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
        m_Access.Width = 120;
        m_Access.Items.AddRange(Enum.GetValues<GuestAccessLevel>().Cast<object>().ToArray());
        m_Access.SelectedItem = GuestAccessLevel.Control;

        m_Duration.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
        m_Duration.Width = 120;
        m_Duration.Items.AddRange(new object[]
        {
            new DurationChoice("15 minutes", 15),
            new DurationChoice("1 hour", 60),
            new DurationChoice("4 hours", 240),
            new DurationChoice("24 hours", 1440)
        });
        SelectDuration(m_Config.GuestInviteDefaultMinutes);

        m_UseAsDefault.Text = "Use this duration for tray shortcuts";
        m_UseAsDefault.AutoSize = true;
        m_UseAsDefault.Margin = new WinForms.Padding(10, 8, 10, 0);

        var create = new WinForms.Button { Text = "Create & Copy", AutoSize = true, Height = 30 };
        create.Click += (_, _) => CreateInvite();

        createPanel.Controls.AddRange(new WinForms.Control[]
        {
            FlowLabel("Access"), m_Access,
            FlowLabel("Expires"), m_Duration,
            create, m_UseAsDefault
        });

        m_Grid.Dock = WinForms.DockStyle.Fill;
        m_Grid.ReadOnly = true;
        m_Grid.AllowUserToAddRows = false;
        m_Grid.AllowUserToDeleteRows = false;
        m_Grid.AllowUserToResizeRows = false;
        m_Grid.MultiSelect = false;
        m_Grid.SelectionMode = WinForms.DataGridViewSelectionMode.FullRowSelect;
        m_Grid.AutoSizeColumnsMode = WinForms.DataGridViewAutoSizeColumnsMode.Fill;
        m_Grid.RowHeadersVisible = false;
        m_Grid.Columns.Add("Code", "Code");
        m_Grid.Columns.Add("Access", "Access");
        m_Grid.Columns.Add("Expires", "Expires");
        m_Grid.Columns.Add("Sessions", "Sessions");
        m_Grid.Columns.Add("Status", "Status");

        var actions = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Bottom,
            Height = 52,
            Padding = new WinForms.Padding(10, 8, 10, 8),
            FlowDirection = WinForms.FlowDirection.LeftToRight
        };
        var copy = new WinForms.Button { Text = "Copy Code", AutoSize = true };
        copy.Click += (_, _) => CopySelected();
        var revoke = new WinForms.Button { Text = "Revoke & Disconnect", AutoSize = true };
        revoke.Click += (_, _) => RevokeSelected();
        var revokeAll = new WinForms.Button { Text = "Revoke All", AutoSize = true };
        revokeAll.Click += (_, _) => RevokeAll();
        var close = new WinForms.Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        actions.Controls.AddRange(new WinForms.Control[] { copy, revoke, revokeAll, close });

        Controls.Add(m_Grid);
        Controls.Add(actions);
        Controls.Add(createPanel);
    }

    private void CreateInvite()
    {
        if (m_Access.SelectedItem is not GuestAccessLevel access
            || m_Duration.SelectedItem is not DurationChoice duration)
            return;

        if (access == GuestAccessLevel.Full
            && !PromptDialogs.Confirm(
                "Full guest access includes system keys and file transfer. Create this code?"))
            return;

        var invite = m_Host.CreateGuestInvite(access, TimeSpan.FromMinutes(duration.Minutes));
        if (m_UseAsDefault.Checked)
        {
            m_Config.GuestInviteDefaultMinutes = duration.Minutes;
            m_Config.Save();
        }

        Copy(invite.Code);
        RefreshRows(invite.Id);
    }

    private void RefreshRows(Guid? selectId = null)
    {
        Guid? selectedId = selectId ?? SelectedInvite()?.Id;
        var invites = m_Host.GuestInvites.Snapshot();

        m_Grid.Rows.Clear();
        foreach (var invite in invites)
        {
            string status = invite.IsRevoked ? "Revoked" : invite.IsExpired ? "Expired" : "Active";
            int rowIndex = m_Grid.Rows.Add(
                invite.Code,
                invite.AccessLevel,
                invite.ExpiresUtc.ToLocalTime().ToString("g"),
                m_Host.GuestSessionCount(invite.Id),
                status);
            var row = m_Grid.Rows[rowIndex];
            row.Tag = invite;
            if (!invite.IsActive) row.DefaultCellStyle.ForeColor = System.Drawing.Color.Gray;
            if (selectedId == invite.Id) row.Selected = true;
        }
    }

    private GuestInvite? SelectedInvite() =>
        m_Grid.SelectedRows.Count == 1 ? m_Grid.SelectedRows[0].Tag as GuestInvite : null;

    private void CopySelected()
    {
        var invite = SelectedInvite();
        if (invite is not null) Copy(invite.Code);
    }

    private void RevokeSelected()
    {
        var invite = SelectedInvite();
        if (invite is null || !invite.IsActive) return;
        m_Host.RevokeGuestInvite(invite.Id);
        RefreshRows(invite.Id);
    }

    private void RevokeAll()
    {
        if (!m_Host.GuestInvites.Snapshot().Any(invite => invite.IsActive)) return;
        if (!PromptDialogs.Confirm("Revoke every guest code and disconnect all guest sessions?")) return;
        m_Host.RevokeAllGuestInvites();
        RefreshRows();
    }

    private void SelectDuration(int minutes)
    {
        var match = m_Duration.Items.Cast<DurationChoice>().FirstOrDefault(item => item.Minutes == minutes);
        if (match is null)
        {
            match = new DurationChoice($"{minutes} minutes", minutes);
            m_Duration.Items.Add(match);
        }
        m_Duration.SelectedItem = match;
    }

    private static WinForms.Label FlowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new WinForms.Padding(8, 8, 4, 0)
    };

    private static void Copy(string code)
    {
        try { WinForms.Clipboard.SetText(code); }
        catch (Exception ex) { PromptDialogs.ShowError($"Could not copy the code.\n\n{ex.Message}"); }
    }
}
