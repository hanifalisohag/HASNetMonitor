using System;
using System.Drawing;
using System.Windows.Forms;

namespace HASNetMonitor.UI;

/// <summary>
/// The small box that asks what to call a saved preset. WinForms has nothing built in, and
/// InputBox brings a Visual Basic dependency in, so the dialog is a form with one text field.
/// </summary>
internal sealed class PresetNamePrompt : Form
{
    private readonly TextBox _name;
    private readonly Label _problem;
    private readonly Button _okButton;

    private PresetNamePrompt(string suggestion, Func<string, string?> validate)
    {
        Text = "Save preset";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;

        var label = new Label
        {
            Text = "Name for this preset:",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 6),
        };

        _name = new TextBox
        {
            Text = suggestion,
            Width = 260,
            AccessibleName = "Preset name",
        };

        // Selects the whole suggestion, so typing replaces it.
        _name.SelectAll();

        _problem = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(260, 0),
            ForeColor = Color.Firebrick,
            Margin = new Padding(3, 4, 3, 3),
        };

        _okButton = new Button
        {
            Text = "Save",
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
        };

        AcceptButton = _okButton;
        CancelButton = cancel;

        // Checked before the dialog closes, so a name already in use is refused with a
        // message instead of silently creating a second entry the picker cannot tell apart.
        // The OK result is set by hand rather than on the button, because a Cancel event
        // cannot be raised from the form.
        _okButton.Click += (_, _) =>
        {
            string? complaint = validate(_name.Text);
            if (complaint is not null)
            {
                _problem.Text = complaint;
                _name.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_okButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };
        layout.Controls.Add(label);
        layout.Controls.Add(_name);
        layout.Controls.Add(_problem);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        ClientSize = new Size(300, layout.PreferredSize.Height);
    }

    /// <summary>
    /// Asks for a name. Returns the trimmed name, or null when the user cancelled. The
    /// validator returns a message to show and keep the dialog open, or null to accept.
    /// </summary>
    public static string? Ask(IWin32Window? owner, string suggestion, Func<string, string?> validate)
    {
        using var dialog = new PresetNamePrompt(suggestion, validate);

        DialogResult result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == DialogResult.OK ? dialog._name.Text.Trim() : null;
    }
}
