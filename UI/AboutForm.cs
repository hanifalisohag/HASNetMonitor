using System.Diagnostics;
using System.Drawing;
using HASNetMonitor.Core;

namespace HASNetMonitor.UI;

/// <summary>
/// About box: the product name, its version, the author and a way to write to them.
/// Everything shown comes from <see cref="AppInfo"/> so the dialog cannot disagree with
/// the version stamped on the exe.
/// </summary>
internal sealed class AboutForm : Form
{
    private readonly Icon? _appIcon;

    public AboutForm()
    {
        // The window shows the application mark rather than the stock WinForms icon.
        // The form does not own an Icon it is given, so it is kept here to dispose.
        _appIcon = AppIcon.Load();
        if (_appIcon is not null)
        {
            Icon = _appIcon;
        }

        Text = $"About {AppInfo.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 16, 18, 12);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var mark = new PictureBox
        {
            Image = LoadMark(64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
            AccessibleName = "Application icon",
        };

        var name = new Label
        {
            Text = AppInfo.Name,
            Font = new Font("Segoe UI Semibold", 14f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        };

        var version = new Label
        {
            Text = $"Version {AppInfo.Version}",
            ForeColor = Color.FromArgb(90, 90, 90),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        var author = new Label
        {
            Text = $"Author: {AppInfo.Author}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        var contact = new LinkLabel
        {
            Text = $"Contact: {AppInfo.ContactEmail}",
            AutoSize = true,
            LinkColor = Color.FromArgb(0, 90, 158),
            ActiveLinkColor = Color.FromArgb(0, 60, 110),
            Margin = new Padding(0, 0, 0, 12),
        };

        // The whole "Contact: address" line is the link, so the label reads naturally
        // while a click opens a mail draft.
        contact.LinkClicked += (_, _) => WriteToAuthor();

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(88, 28),
            Anchor = AnchorStyles.Right,
        };

        var details = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(14, 0, 0, 0),
        };
        details.Controls.Add(name, 0, 0);
        details.Controls.Add(version, 0, 1);
        details.Controls.Add(author, 0, 2);
        details.Controls.Add(contact, 0, 3);

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };
        header.Controls.Add(mark);
        header.Controls.Add(details);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(ok);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
    }

    /// <summary>The shared application mark as a bitmap, or null when it cannot be read.</summary>
    private static Image? LoadMark(int edge)
    {
        try
        {
            using Icon? icon = AppIcon.LoadForSize(edge);
            return icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            // A missing or unreadable mark must not stop the About box from opening.
            return null;
        }
    }

    /// <summary>All text the dialog shows, for tests and accessibility checks.</summary>
    public string AllText()
    {
        var text = new List<string> { Text };
        Collect(this, text);
        return string.Join("\n", text);

        static void Collect(Control control, List<string> into)
        {
            foreach (Control child in control.Controls)
            {
                if (!string.IsNullOrEmpty(child.Text))
                {
                    into.Add(child.Text);
                }

                Collect(child, into);
            }
        }
    }

    /// <summary>Opens a mail draft to the author. A missing mail client is not an error.</summary>
    private static void WriteToAuthor()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{AppInfo.ContactEmail}") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No default mail handler: the address is on screen to copy instead.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Control control in Controls)
            {
                ReleaseMark(control);
            }

            _appIcon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void ReleaseMark(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (child is PictureBox picture)
            {
                Image? image = picture.Image;
                picture.Image = null;
                image?.Dispose();
            }

            ReleaseMark(child);
        }
    }
}
