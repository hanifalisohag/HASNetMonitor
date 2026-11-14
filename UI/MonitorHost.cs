using System.Diagnostics;
using System.Drawing;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Interop;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// Owns the widget, the tray icon and the menu. Runs for the whole session; nothing
/// here is per-frame, so the UI thread stays idle while the network is quiet.
/// </summary>
internal sealed class MonitorHost : ApplicationContext
{
    private readonly MonitorConfig _config;
    private readonly string _configPath;
    private readonly TaskbarWidget _widget;
    private readonly ContextMenuStrip _menu;
    private NotifyIcon? _trayIcon;
    private Icon? _trayMark;
    private bool _settingsOpen;
    private bool _disposed;

    public MonitorHost()
    {
        _configPath = MonitorConfig.DefaultConfigPath;
        bool firstRun = !File.Exists(_configPath);

        _config = MonitorConfig.Load(_configPath, MonitorConfig.LegacyConfigPath);
        if (firstRun)
        {
            _config.TrySave(_configPath);
        }

        _menu = BuildMenu();
        _widget = new TaskbarWidget(_config);
        _widget.MenuRequested += ShowMenuAt;

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

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

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
                config.NudgeXPx = Math.Clamp(config.NudgeXPx + offsetX, -MonitorConfig.MaxNudgePx, MonitorConfig.MaxNudgePx);
                config.NudgeYPx = Math.Clamp(config.NudgeYPx + offsetY, -MonitorConfig.MaxNudgePx, MonitorConfig.MaxNudgePx);
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

        var openFolder = new ToolStripMenuItem("Open settings folder");
        openFolder.Click += (_, _) => OpenSettingsFolder();
        menu.Items.Add(openFolder);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Exit();
        menu.Items.Add(exit);

        menu.Opening += (_, _) =>
        {
            startup.Checked = _config.StartWithWindows;
            settings.Enabled = !_settingsOpen;

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
                        && _config.Style == tag["style:".Length..],
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

    private void ShowMenuAt(Point screenPoint)
    {
        if (_disposed || _settingsOpen)
        {
            return;
        }

        _menu.Show(screenPoint);

        // A no-activate window never owns the foreground, so the popup needs a nudge
        // to dismiss itself when the user clicks elsewhere.
        NativeMethods.SetForegroundWindow(_menu.Handle);
    }

    private void ShowSettings()
    {
        if (_disposed || _settingsOpen)
        {
            return;
        }

        _settingsOpen = true;
        try
        {
            using var form = new SettingsForm(_config.Clone());

            // Keep the pre-dialog state so Cancel can put the widget back exactly as
            // it was, even though every change was already applied live.
            MonitorConfig original = _config.Clone();

            form.LiveChanged += (_, live) => ApplyLive(form, live, original);

            if (form.ShowDialog() != DialogResult.OK || form.Result is not { } updated)
            {
                _config.CopyFrom(original);
                _widget.ApplyConfig();
                return;
            }

            _config.CopyFrom(updated);
            _config.TrySave(_configPath);
            _widget.ApplyConfig();
            ApplyStartup(_config.StartWithWindows);
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    /// <summary>
    /// Shows a change on the real widget immediately. Start-with-Windows is the one
    /// setting that is not applied here: writing the registry belongs to accepting the
    /// dialog, so the original value is kept until then.
    /// </summary>
    private void ApplyLive(SettingsForm form, MonitorConfig live, MonitorConfig original)
    {
        MonitorConfig applied = live.Clone();
        applied.StartWithWindows = original.StartWithWindows;

        _config.CopyFrom(applied);
        _widget.ApplyConfig();
    }

    private void ApplyStartup(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            _config.StartWithWindows = enabled;
            _config.TrySave(_configPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Locked-down profile: the widget keeps working, it just will not autostart.
        }
    }

    private void OpenSettingsFolder()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Nothing to do: opening a folder is a convenience, not a feature.
        }
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
                Text = "Taskbar Network Monitor",
                Visible = true,
                ContextMenuStrip = _menu,
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
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
