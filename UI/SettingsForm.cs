using System.Drawing;
using System.Drawing.Drawing2D;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.UI;

/// <summary>
/// Settings dialog: look (style, stacking order, font, caps, glow), colours,
/// cadence and scale, with a live preview of the widget on a mock taskbar. Values
/// are validated by <see cref="MonitorConfig.Normalize"/> on the way out, so nothing
/// picked here can produce a config the widget cannot draw.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const float PreviewScale = 2f;

    private const int PreviewHeight = 128;

    private readonly Icon? _appIcon;

    private readonly Button _downloadColorButton;
    private readonly Button _uploadColorButton;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _maxScale;
    private readonly NumericUpDown _barThickness;
    private readonly NumericUpDown _segmentGap;
    private readonly NumericUpDown _nudgeX;
    private readonly NumericUpDown _nudgeY;
    private readonly NumericUpDown _offsetFromTray;
    private readonly Button _nudgeLeft;
    private readonly Button _nudgeRight;
    private readonly Button _nudgeUp;
    private readonly Button _nudgeDown;
    private readonly Button _resetNudge;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _backdropOpacity;
    private readonly CheckBox _autoScale;
    private readonly CheckBox _sharedScale;
    private readonly CheckBox _showText;
    private readonly CheckBox _showBackdrop;
    private readonly CheckBox _crispText;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _bold;
    private readonly CheckBox _italic;
    private readonly CheckBox _roundedBars;
    private readonly CheckBox _accentGlow;
    private readonly ComboBox _style;
    private readonly ComboBox _sortOrder;
    private readonly ComboBox _fontFamily;
    private readonly PictureBox _preview;
    private readonly Label _previewError;

    public SettingsForm(MonitorConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        Text = "Taskbar Network Monitor \u2013 Settings";

        // The window shows the application mark rather than the stock WinForms icon.
        // The form does not own an Icon it is given, so it is kept here to dispose.
        _appIcon = AppIcon.Load();
        if (_appIcon is not null)
        {
            Icon = _appIcon;
        }

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 14, 14, 10);
        Font = new Font("Segoe UI", 9f);

        _downloadColorButton = CreateColorButton("Download bar colour");
        _downloadColorButton.Click += (_, _) => PickColor(_downloadColorButton);
        _uploadColorButton = CreateColorButton("Upload bar colour");
        _uploadColorButton.Click += (_, _) => PickColor(_uploadColorButton);

        _interval = new NumericUpDown
        {
            Minimum = MonitorConfig.MinRefreshIntervalMs,
            Maximum = MonitorConfig.MaxRefreshIntervalMs,
            Increment = 250,
            Value = Numeric.Clamp(config.RefreshIntervalMs, MonitorConfig.MinRefreshIntervalMs, MonitorConfig.MaxRefreshIntervalMs),
            Width = 90,
        };

        _autoScale = new CheckBox { Text = "Fit bars to current traffic", Checked = config.AutoScale, AutoSize = true };

        _sharedScale = new CheckBox { Text = "One scale for both bars", Checked = config.SharedScale, AutoSize = true };

        _maxScale = new NumericUpDown
        {
            Minimum = 0.1m,
            Maximum = 100000m,
            DecimalPlaces = 1,
            Increment = 10m,
            Value = (decimal)Numeric.Clamp(config.MaxScaleMbps, 0.1, 100000),
            Width = 90,
        };

        _showText = new CheckBox { Text = "Show speeds as text", Checked = config.ShowText, AutoSize = true };

        _barThickness = new NumericUpDown
        {
            Minimum = 1,
            Maximum = MonitorConfig.MaxBarThicknessPx,
            Value = Numeric.Clamp(config.BarThicknessPx, 1, MonitorConfig.MaxBarThicknessPx),
            Width = 60,
        };

        _segmentGap = new NumericUpDown
        {
            Minimum = 0,
            Maximum = MonitorConfig.MaxSegmentGapPx,
            Value = Numeric.Clamp(config.SegmentGapPx, 0, MonitorConfig.MaxSegmentGapPx),
            Width = 60,
        };

        _nudgeX = CreateNudgeBox(config.NudgeXPx);
        _nudgeY = CreateNudgeBox(config.NudgeYPx);

        _offsetFromTray = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 64,
            Value = Numeric.Clamp(config.OffsetFromTrayPx, 0, 64),
            Width = 60,
        };

        _nudgeLeft = CreateNudgeButton("\u2190", "Move the widget left", _nudgeX, -1);
        _nudgeRight = CreateNudgeButton("\u2192", "Move the widget right", _nudgeX, +1);
        _nudgeUp = CreateNudgeButton("\u2191", "Move the widget up", _nudgeY, -1);
        _nudgeDown = CreateNudgeButton("\u2193", "Move the widget down", _nudgeY, +1);
        _resetNudge = new Button { Text = "Reset nudges", AutoSize = true };
        _resetNudge.Click += (_, _) =>
        {
            _nudgeX.Value = 0;
            _nudgeY.Value = 0;
            _offsetFromTray.Value = 6;
        };

        _fontSize = new NumericUpDown
        {
            Minimum = 6m,
            Maximum = 20m,
            DecimalPlaces = 1,
            Increment = 0.5m,
            Value = (decimal)Numeric.Clamp(config.FontSizePt, 6, 20),
            Width = 60,
        };

        _showBackdrop = new CheckBox { Text = "Tint behind the widget", Checked = config.ShowBackdrop, AutoSize = true };

        _crispText = new CheckBox
        {
            Text = "Crisp text like the clock",
            Checked = config.CrispText,
            AutoSize = true,
            AccessibleDescription = "Draws subpixel (ClearType) glyphs on an opaque plate, "
                + "the way the taskbar clock is drawn. The plate hides the taskbar behind "
                + "the widget, so it is off by default.",
        };

        _backdropOpacity = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 255,
            Value = Numeric.Clamp(config.BackdropOpacity, 0, 255),
            Width = 60,
        };

        _startWithWindows = new CheckBox { Text = "Start with Windows", Checked = config.StartWithWindows, AutoSize = true };

        List<Choice> styleChoices = [];
        foreach (string style in WidgetStyles.All)
        {
            styleChoices.Add(new Choice(style, WidgetStyles.Describe(style)));
        }

        _style = CreateChoiceBox(styleChoices, WidgetStyles.Normalize(config.Style));

        _sortOrder = CreateChoiceBox(
        [
            new Choice(StackOrder.DownloadFirst, "Download on top, upload below"),
            new Choice(StackOrder.UploadFirst, "Upload on top, download below"),
        ], StackOrder.Normalize(config.SortOrder));

        _fontFamily = CreateFontBox(config.FontFamily);

        _bold = new CheckBox { Text = "Bold", Checked = config.FontBold, AutoSize = true };
        _italic = new CheckBox { Text = "Italic", Checked = config.FontItalic, AutoSize = true };
        _roundedBars = new CheckBox { Text = "Rounded caps", Checked = config.RoundedBars, AutoSize = true };
        _accentGlow = new CheckBox { Text = "Glow around the fill", Checked = config.AccentGlow, AutoSize = true };

        var position = CreateGroup("Position");
        AddRow(position, "Offset from tray (px):", _offsetFromTray);
        AddRow(position, "Nudge left / right (px):", Combine(_nudgeX, _nudgeLeft, _nudgeRight));
        AddRow(position, "Nudge up / down (px):", Combine(_nudgeY, _nudgeUp, _nudgeDown));
        AddRow(position, string.Empty, _resetNudge);

        var look = CreateGroup("Look");
        AddRow(look, "Style:", _style);
        AddRow(look, "Stacking:", _sortOrder);
        AddRow(look, "Font:", _fontFamily);
        AddRow(look, string.Empty, Combine(_bold, _italic));
        AddRow(look, "Bar shape:", Combine(_roundedBars, _accentGlow));
        AddRow(look, "Bar thickness (px):", _barThickness);
        AddRow(look, "Segment gap (px):", _segmentGap);
        AddRow(look, "Font size (pt):", _fontSize);
        AddRow(look, "Text:", _showText);
        AddRow(look, "Sharpness:", _crispText);

        var colours = CreateGroup("Colours and behaviour");
        AddRow(colours, "Download colour:", _downloadColorButton);
        AddRow(colours, "Upload colour:", _uploadColorButton);
        AddRow(colours, "Refresh (ms):", _interval);
        AddRow(colours, "Scaling:", _autoScale);
        AddRow(colours, "Bar scale:", _sharedScale);
        AddRow(colours, "Max scale (Mbps):", _maxScale);
        AddRow(colours, "Appearance:", _showBackdrop);
        AddRow(colours, "Tint opacity:", _backdropOpacity);
        AddRow(colours, "Start-up:", _startWithWindows);

        var columns = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        columns.Controls.Add(look, 0, 0);
        columns.Controls.Add(colours, 1, 0);
        columns.Controls.Add(position, 0, 1);

        _preview = new PictureBox
        {
            Size = new Size(430, PreviewHeight),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(24, 24, 26),
            SizeMode = PictureBoxSizeMode.Normal,
            AccessibleName = "Widget preview",
        };

        _previewError = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            Visible = false,
            MaximumSize = new Size(430, 0),
        };

        var previewGroup = CreateGroup("Preview");
        AddRow(previewGroup, string.Empty, _preview);
        AddRow(previewGroup, string.Empty, _previewError);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 26) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        root.Controls.Add(columns);
        root.Controls.Add(previewGroup);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;

        SetColor(_downloadColorButton, config.DownloadColorHex);
        SetColor(_uploadColorButton, config.UploadColorHex);

        ok.Click += (_, _) => ApplyDialogResult();

        foreach (Control input in new Control[]
        {
            _style, _sortOrder, _fontFamily, _bold, _italic, _roundedBars, _accentGlow,
            _showText, _showBackdrop, _crispText, _sharedScale, _barThickness, _segmentGap, _fontSize,
            _nudgeX, _nudgeY, _offsetFromTray, _nudgeLeft, _nudgeRight, _nudgeUp, _nudgeDown, _resetNudge,
        })
        {
            WireForPreview(input);
        }

        // Crisping means an opaque plate, so the tint controls follow it rather than
        // silently disagreeing with what the renderer does.
        _crispText.CheckedChanged += (_, _) => ApplyCrispCoupling();
        ApplyCrispCoupling();

        RefreshPreview();
    }

    /// <summary>
    /// Keeps the tint controls honest while crisp text is on: the plate is forced opaque,
    /// so the opacity spinner shows 255 and is locked rather than pretending to apply.
    /// </summary>
    private void ApplyCrispCoupling()
    {
        bool crisp = _crispText.Checked;
        if (crisp)
        {
            _showBackdrop.Checked = true;
            _backdropOpacity.Value = WidgetRenderer.OpaqueAlpha;
        }

        _backdropOpacity.Enabled = !crisp;
    }

    /// <summary>The normalised settings the user accepted.</summary>
    public MonitorConfig? Result { get; private set; }

    private void ApplyDialogResult()
    {
        Result = BuildResult();
    }

    /// <summary>Reads the controls back into a normalised config (no validation gaps).</summary>
    internal MonitorConfig BuildResult()
    {
        return new MonitorConfig
        {
            DownloadColorHex = _downloadColorButton.Tag as string ?? MonitorConfig.DefaultDownloadColor,
            UploadColorHex = _uploadColorButton.Tag as string ?? MonitorConfig.DefaultUploadColor,
            RefreshIntervalMs = (int)_interval.Value,
            AutoScale = _autoScale.Checked,
            SharedScale = _sharedScale.Checked,
            MaxScaleMbps = (double)_maxScale.Value,
            BarThicknessPx = (int)_barThickness.Value,
            SegmentGapPx = (int)_segmentGap.Value,
            NudgeXPx = (int)_nudgeX.Value,
            NudgeYPx = (int)_nudgeY.Value,
            OffsetFromTrayPx = (int)_offsetFromTray.Value,
            FontSizePt = (double)_fontSize.Value,
            ShowText = _showText.Checked,
            ShowBackdrop = _showBackdrop.Checked,
            BackdropOpacity = (int)_backdropOpacity.Value,
            StartWithWindows = _startWithWindows.Checked,
            Style = SelectedKey(_style, WidgetStyles.Bars),
            SortOrder = SelectedKey(_sortOrder, StackOrder.DownloadFirst),
            FontFamily = SelectedKey(_fontFamily, FontCatalog.Automatic),
            FontBold = _bold.Checked,
            FontItalic = _italic.Checked,
            RoundedBars = _roundedBars.Checked,
            AccentGlow = _accentGlow.Checked,
            CrispText = _crispText.Checked,
        }.Normalize();
    }

    private void WireForPreview(Control input)
    {
        switch (input)
        {
            case ComboBox combo:
                combo.SelectedIndexChanged += (_, _) => RefreshPreview();
                break;
            case CheckBox check:
                check.CheckedChanged += (_, _) => RefreshPreview();
                break;
            case NumericUpDown number:
                number.ValueChanged += (_, _) => RefreshPreview();
                break;
        }
    }

    /// <summary>
    /// Draws the current settings on a mock dark taskbar, at twice the normal scale,
    /// so the style and font are actually legible while choosing them.
    /// </summary>
    private void RefreshPreview()
    {
        MonitorConfig config;
        try
        {
            config = BuildResult();
            Bitmap canvas = TaskbarPreview.Render(config, _preview.Size, PreviewScale, PreviewHeight);

            Image? previous = _preview.Image;
            _preview.Image = canvas;
            previous?.Dispose();
            _previewError.Visible = false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            _previewError.Text = "The preview could not be drawn: " + ex.Message;
            _previewError.Visible = true;
            return;
        }

        // Every change is also handed to the live widget, so the user sees it on the
        // real taskbar immediately instead of after pressing OK.
        LiveChanged?.Invoke(this, config);
    }

    /// <summary>
    /// Raised on every control change with the settings as they are right now. The
    /// host applies them to the running widget; nothing is saved until the user
    /// accepts the dialog.
    /// </summary>
    public event EventHandler<MonitorConfig>? LiveChanged;

    private static GroupBox CreateGroup(string caption)
    {
        var group = new GroupBox
        {
            Text = caption,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(0, 0, 10, 8),
        };

        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        group.Controls.Add(table);
        group.Tag = table;
        return group;
    }

    private static TableLayoutPanel TableOf(GroupBox group) => (TableLayoutPanel)group.Tag!;

    private static Control Combine(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };

        foreach (Control control in controls)
        {
            control.Margin = new Padding(0, 0, 12, 0);
            flow.Controls.Add(control);
        }

        return flow;
    }

    /// <summary>A nudged offset box: whole pixels, signed, never beyond the safe range.</summary>
    private static NumericUpDown CreateNudgeBox(int value) => new()
    {
        Minimum = -MonitorConfig.MaxNudgePx,
        Maximum = MonitorConfig.MaxNudgePx,
        Value = Numeric.Clamp(value, -MonitorConfig.MaxNudgePx, MonitorConfig.MaxNudgePx),
        Width = 60,
    };

    /// <summary>
    /// One step of the arrow buttons. A single click is one pixel, so the widget can be
    /// lined up exactly; holding the button repeats it.
    /// </summary>
    private static Button CreateNudgeButton(string text, string tooltip, NumericUpDown target, int delta)
    {
        var button = new Button { Text = text, AutoSize = false, Width = 30, Height = 24 };
        new ToolTip().SetToolTip(button, tooltip);
        button.Click += (_, _) =>
        {
            decimal next = Numeric.Clamp(target.Value + delta, target.Minimum, target.Maximum);
            target.Value = next;
        };
        return button;
    }

    private static ComboBox CreateChoiceBox(IReadOnlyList<Choice> choices, string selectedKey)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230,
        };

        foreach (Choice choice in choices)
        {
            combo.Items.Add(choice);
        }

        combo.SelectedIndex = IndexOfChoice(combo, selectedKey);
        return combo;
    }

    private static ComboBox CreateFontBox(string selectedFamily)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230,
        };

        combo.Items.Add(new Choice(FontCatalog.Automatic, FontCatalog.Describe(FontCatalog.Automatic)));
        foreach (string family in FontCatalog.InstalledFamilies)
        {
            combo.Items.Add(new Choice(family, family));
        }

        int index = IndexOfChoice(combo, FontCatalog.Normalize(selectedFamily));
        combo.SelectedIndex = index;

        // A family can disappear between two runs; keep the dialog usable.
        if (combo.SelectedIndex < 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    private static int IndexOfChoice(ComboBox combo, string key)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is Choice choice && string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return combo.Items.Count > 0 ? 0 : -1;
    }

    private static string SelectedKey(ComboBox combo, string fallback)
        => combo.SelectedItem is Choice choice ? choice.Key : fallback;

    private static void AddRow(GroupBox group, string caption, Control input) => AddRow(TableOf(group), caption, input);

    private static void AddRow(TableLayoutPanel table, string caption, Control input)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6),
        };

        input.Anchor = AnchorStyles.Left;
        input.Margin = new Padding(0, 4, 0, 4);

        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row);
        table.Controls.Add(input, 1, row);
    }

    private static Button CreateColorButton(string accessibleName)
    {
        var button = new Button
        {
            Text = "#000000",
            AutoSize = false,
            Size = new Size(110, 26),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            AccessibleName = accessibleName,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(120, 120, 120);
        return button;
    }

    private void PickColor(Button button)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = ColorHelper.Parse(button.Tag as string, Color.FromArgb(46, 204, 113)),
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SetColor(button, ColorHelper.ToHex(dialog.Color));
            RefreshPreview();
        }
    }

    private static void SetColor(Button button, string hex)
    {
        string normalized = ColorHelper.ToHex(ColorHelper.Parse(hex, Color.FromArgb(46, 204, 113)));
        button.Tag = normalized;
        button.Text = normalized;
        Color color = ColorHelper.Parse(normalized, Color.Black);
        button.BackColor = color;
        button.ForeColor = color.GetBrightness() > 0.55 ? Color.Black : Color.White;
    }

    /// <summary>One entry of a drop-down: the stored key plus the wording shown.</summary>
    private sealed record Choice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
