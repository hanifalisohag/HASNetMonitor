using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Interop;

namespace HASNetMonitor.UI;

/// <summary>
/// Owns the widget, the tray icon and the menu. Runs for the whole session; nothing
/// here is per-frame, so the UI thread stays idle while the network is quiet.
/// </summary>
internal sealed class MonitorHost : ApplicationContext
{
    private readonly MonitorConfig _config;
    private readonly string _configPath;
    private readonly TaskbarWidget _widget;
    private readonly ContextMenuStrip _trayMenu;
    private NotifyIcon? _trayIcon;
    private Icon? _trayMark;
    private SettingsForm? _settingsForm;
    private bool _disposed;

    public MonitorHost()
    {
        _configPath = MonitorConfig.DefaultConfigPath;
        bool firstRun = !File.Exists(_configPath);

        _config = MonitorConfig.Load(_configPath, MonitorConfig.LegacyConfigPath);
        if (firstRun)
        {
            // Load already yields the initial preset when nothing exists (or the user's
            // old colours when a legacy config was found), so this just puts that config
            // on disk: a real file the user can edit straight away. Copying the preset
            // over it here would throw away migrated legacy settings.
            _config.TrySave(_configPath);
        }

        _widget = new TaskbarWidget(_config);
        _widget.CaptionChanged += UpdateCaption;

        // The tray icon's menu is owned by the widget so the popup never gets a taskbar
        // button of its own; the widget must exist first to hand over its handle. The
        // widget itself shows no menu: a right-click on it does nothing.
        _trayMenu = BuildMenu(_widget.WindowHandle);

        _trayIcon = CreateTrayIcon();

        // The config is the source of truth: the Run entry follows it on every start,
        // which also repairs it after a Windows update cleans the key.
        ApplyStartup(_config.StartWithWindows);

        _widget.ForceRedraw();
    }

    public void Exit()
    {
        ExitThread();
    }

    /// <summary>
    /// Narrows the tray tooltip to the one-line caption the widget just sampled:
    /// the traffic figures followed by the statistics. The tray text length is
    /// capped by the shell at about 127 characters, so it is truncated honestly.
    /// </summary>
    private void UpdateCaption(string caption)
    {
        if (_trayIcon is null || string.IsNullOrEmpty(caption))
        {
            return;
        }

        // NotifyIcon.Text is capped at 63 characters on .NET Framework and throws
        // ArgumentException above it. The caption grows with every chip, so a four-chip
        // line overflows it - and because this runs from the sample path, the throw
        // skipped the render and froze the whole widget. Fit it to the real limit.
        string text = TaskbarWidget.FitTrayText(caption);

        try
        {
            if (_trayIcon.Text != text)
            {
                _trayIcon.Text = text;
            }
        }
        catch (ArgumentException)
        {
            // A tooltip is not worth failing the sample over.
        }
    }


    private ContextMenuStrip BuildMenu(IntPtr owner)
    {
        var menu = new OwnedContextMenuStrip(owner);

        var settings = new ToolStripMenuItem("Settings\u2026");
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        // One-click switches for the two options people change most often.
        var look = new ToolStripMenuItem("Look");
        foreach (string style in WidgetStyles.All)
        {
            string key = style;
            var item = new ToolStripMenuItem(WidgetStyles.Describe(key)) { Tag = "style:" + key };
            item.Click += (_, _) => ApplyLook(config => config.Style = key);
            look.DropDownItems.Add(item);
        }

        look.DropDownItems.Add(new ToolStripSeparator());

        var downloadOnTop = new ToolStripMenuItem("Download on top, upload below")
        {
            Tag = "order:" + StackOrder.DownloadFirst,
        };
        downloadOnTop.Click += (_, _) => ApplyLook(config => config.SortOrder = StackOrder.DownloadFirst);
        look.DropDownItems.Add(downloadOnTop);

        var uploadOnTop = new ToolStripMenuItem("Upload on top, download below")
        {
            Tag = "order:" + StackOrder.UploadFirst,
        };
        uploadOnTop.Click += (_, _) => ApplyLook(config => config.SortOrder = StackOrder.UploadFirst);
        look.DropDownItems.Add(uploadOnTop);

        look.DropDownItems.Add(new ToolStripSeparator());

        var bold = new ToolStripMenuItem("Bold text") { Tag = "bold" };
        bold.Click += (_, _) => ApplyLook(config => config.FontBold = !config.FontBold);
        look.DropDownItems.Add(bold);

        var italic = new ToolStripMenuItem("Italic text") { Tag = "italic" };
        italic.Click += (_, _) => ApplyLook(config => config.FontItalic = !config.FontItalic);
        look.DropDownItems.Add(italic);

        menu.Items.Add(look);

        var position = new ToolStripMenuItem("Position");
        foreach ((string label, int dx, int dy) in new[]
        {
            ("Nudge left", -4, 0),
            ("Nudge right", 4, 0),
            ("Nudge up", 0, -2),
            ("Nudge down", 0, 2),
        })
        {
            var item = new ToolStripMenuItem(label) { Tag = "nudge" };
            int offsetX = dx;
            int offsetY = dy;
            item.Click += (_, _) => ApplyLook(config =>
            {
                config.NudgeXPx = Numeric.Clamp(config.NudgeXPx + offsetX, -MonitorConfig.MaxNudgePx, MonitorConfig.MaxNudgePx);
                config.NudgeYPx = Numeric.Clamp(config.NudgeYPx + offsetY, -MonitorConfig.MaxNudgePx, MonitorConfig.MaxNudgePx);
            });
            position.DropDownItems.Add(item);
        }

        position.DropDownItems.Add(new ToolStripSeparator());
        var resetPosition = new ToolStripMenuItem("Reset position") { Tag = "nudge-reset" };
        resetPosition.Click += (_, _) => ApplyLook(config =>
        {
            config.NudgeXPx = 0;
            config.NudgeYPx = 0;
        });
        position.DropDownItems.Add(resetPosition);
        menu.Items.Add(position);

        var rescale = new ToolStripMenuItem("Reset bar scaling");
        rescale.Click += (_, _) => _widget.ResetScale();
        menu.Items.Add(rescale);

        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startup.Click += (_, _) => ApplyStartup(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());

        var about = new ToolStripMenuItem("About\u2026");
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Exit();
        menu.Items.Add(exit);

        menu.Opening += (_, _) =>
        {
            startup.Checked = _config.StartWithWindows;

            // Settings stays enabled while its dialog is open: choosing it again must
            // bring the dialog back to the front, not be a dead entry. The label says
            // which of the two it will do.
            settings.Text = _settingsForm is null ? "Settings\u2026" : "Show settings";
            settings.Enabled = true;

            foreach (ToolStripItem item in look.DropDownItems)
            {
                if (item is not ToolStripMenuItem entry)
                {
                    continue;
                }

                entry.Checked = entry.Tag switch
                {
                    "bold" => _config.FontBold,
                    "italic" => _config.FontItalic,
                    "order:DownloadFirst" => _config.SortOrder == StackOrder.DownloadFirst,
                    "order:UploadFirst" => _config.SortOrder == StackOrder.UploadFirst,
                    string tag => tag.StartsWith("style:", StringComparison.Ordinal)
                        && _config.Style == tag.Substring("style:".Length),
                    _ => false,
                };
            }
        };

        return menu;
    }

    /// <summary>
    /// Applies one of the tray-menu look switches: change the live config, save it and
    /// redraw at once.
    /// </summary>
    private void ApplyLook(Action<MonitorConfig> change)
    {
        if (_disposed)
        {
            return;
        }

        change(_config);
        _config.Normalize();
        _config.TrySave(_configPath);
        _widget.ApplyConfig();
    }

    private void ShowSettings()
    {
        if (_disposed)
        {
            return;
        }

        // Already open: bring it back to the front instead of ignoring the click. The
        // dialog can end up behind other windows, and the tray menu must always be able
        // to raise it again.
        if (_settingsForm is { } open)
        {
            BringToFront(open);
            return;
        }

        var form = new SettingsForm(_config.Clone());
        _settingsForm = form;
        try
        {
            // Keep the pre-dialog state so Cancel can put the widget back exactly as
            // it was, even though every change was already applied live.
            MonitorConfig original = _config.Clone();

            form.LiveChanged += (_, live) => ApplyLive(live, original);

            if (form.ShowDialog() != DialogResult.OK || form.Result is not { } updated)
            {
                _config.CopyFrom(original);
                _widget.ApplyConfig();

                // The autostart entry was written live like every other setting, so
                // cancelling has to put the registry back as well.
                SetStartupEntry(original.StartWithWindows);
                return;
            }

            _config.CopyFrom(updated);
            _config.TrySave(_configPath);
            _widget.ApplyConfig();
            ApplyStartup(_config.StartWithWindows);
        }
        finally
        {
            _settingsForm = null;
            form.Dispose();
        }
    }

    /// <summary>
    /// Raises an already-open dialog above whatever is covering it. A minimised window
    /// is restored first, because activating a minimised window leaves it minimised.
    /// </summary>
    private static void BringToFront(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
        form.BringToFront();

        // Activate alone can be refused when another application owns the foreground, so
        // ask the shell directly. The dialog is a normal window, so this is the one place
        // a foreground call is wanted: it is what actually raises the buried dialog.
        NativeMethods.SetForegroundWindow(form.Handle);
    }

    /// <summary>
    /// Shows a change on the real widget immediately, including the autostart entry:
    /// every control in the dialog applies as it is touched, so the dialog needs no
    /// preview of its own. Nothing is written to config.json until the dialog is
    /// accepted, and Cancel restores the registry entry along with the widget.
    /// </summary>
    private void ApplyLive(MonitorConfig live, MonitorConfig original)
    {
        MonitorConfig applied = live.Clone();

        _config.CopyFrom(applied);
        _widget.ApplyConfig();

        if (applied.StartWithWindows != original.StartWithWindows)
        {
            SetStartupEntry(applied.StartWithWindows);
        }
    }

    private void ApplyStartup(bool enabled)
    {
        if (SetStartupEntry(enabled))
        {
            _config.StartWithWindows = enabled;
            _config.TrySave(_configPath);
        }
    }

    /// <summary>
    /// Writes or removes the per-user <c>Run</c> entry and reports whether it worked.
    /// Kept apart from <see cref="ApplyStartup"/> so the dialog can apply the setting
    /// live without saving <c>config.json</c> before the user accepts.
    /// </summary>
    private static bool SetStartupEntry(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Locked-down profile: the widget keeps working, it just will not autostart.
            return false;
        }
    }

    private void ShowAbout()
    {
        if (_disposed || _settingsForm is not null)
        {
            return;
        }

        using var form = new AboutForm();
        form.ShowDialog();
    }

    private NotifyIcon? CreateTrayIcon()
    {
        try
        {
            _trayMark = CreateTrayMark(SystemInformation.SmallIconSize);
            if (_trayMark is null)
            {
                return null;
            }

            var tray = new NotifyIcon
            {
                Icon = _trayMark,
                // The tooltip is the app's name, from the one place that defines it, so
                // it cannot drift from the About box.
                Text = AppInfo.Name,
                Visible = true,
                ContextMenuStrip = _trayMenu,
            };
            tray.DoubleClick += (_, _) => ShowSettings();
            return tray;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The tray mark at the shell's requested small-icon size, taken from the same
    /// image as the exe icon so the two can never drift apart.
    /// </summary>
    internal static Icon? CreateTrayMark(Size smallIconSize)
    {
        int edge = Math.Max(16, Math.Max(smallIconSize.Width, smallIconSize.Height));
        return AppIcon.LoadForSize(edge);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMark?.Dispose();
            _trayMark = null;

            _widget.Dispose();
            _trayMenu.Dispose();
        }

        base.Dispose(disposing);
    }
}
