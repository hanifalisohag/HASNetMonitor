using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.UI;

/// <summary>
/// Settings dialog: look (style, stacking order, font, caps, glow), colours,
/// cadence and scale. Every control applies to the running widget as it is touched,
/// so the dialog needs no preview of its own. Values are validated by
/// <see cref="MonitorConfig.Normalize"/> on the way out, so nothing picked here can
/// produce a config the widget cannot draw.
/// </summary>
internal sealed class SettingsForm : Form
{
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
    private readonly CheckBox _showCpu;
    private readonly CheckBox _showGpu;
    private readonly CheckBox _showRam;
    private readonly CheckBox _showDisk;
    private readonly CheckBox _showBattery;
    private readonly CheckBox _bold;
    private readonly CheckBox _italic;
    private readonly CheckBox _roundedBars;
    private readonly CheckBox _accentGlow;
    private readonly ComboBox _style;
    private readonly ComboBox _sortOrder;
    private readonly ComboBox _fontFamily;
    private readonly NumericUpDown _arrowSize;
    private readonly ComboBox _arrowStyle;
    private readonly ComboBox _downloadTextMode;
    private readonly ComboBox _uploadTextMode;
    private readonly Button _downloadTextColorButton;
    private readonly Button _uploadTextColorButton;
    private readonly ComboBox _presetBox;
    private readonly Label _presetNote;
    private readonly Button _savePresetButton;

    /// <summary>
    /// The file the saved looks live in, or null when the dialog must not touch the disk.
    /// </summary>
    private readonly string? _presetPath;

    /// <summary>
    /// The user's saved looks, loaded once so the picker and the name-on-reopen lookup see
    /// the same list. Empty when the file is absent or unreadable.
    /// </summary>
    private IReadOnlyList<WidgetPreset> _userPresets;

    private ComboBox _userBox = null!;
    private Button _deletePresetButton = null!;

    /// <summary>
    /// Set while a preset is being pushed into the controls, so the events each setter
    /// raises do not bounce back into the picker or re-derive the preset name mid-way.
    /// </summary>
    private bool _applyingPreset;

    /// <summary>
    /// The picker entry that means "these settings match no preset". It is not a choice:
    /// selecting it applies nothing.
    /// </summary>
    internal const string CustomEntry = "Custom";

    /// <summary>
    /// The saved-look picker's placeholder when there is nothing saved yet. A DropDownList
    /// cannot be empty without looking broken, and the entry must not be mistaken for a
    /// preset name.
    /// </summary>
    internal const string NoSavedPresets = "No saved looks yet";

    public SettingsForm(MonitorConfig config)
        : this(config, UserPresets.DefaultPath)
    {
    }

    /// <summary>
    /// Builds the dialog against a specific preset file. A null path means "do not touch the
    /// disk": the picker shows the built-in gallery and saving does nothing. The tests use
    /// that so they never write to a real profile.
    /// </summary>
    internal SettingsForm(MonitorConfig config, string? presetPath)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _presetPath = presetPath;
        _userPresets = presetPath is null
            ? Array.Empty<WidgetPreset>()
            : UserPresets.Load(presetPath);

        Text = "Taskbar Network Monitor \u2013 Settings";

        // The window shows the application mark rather than the stock WinForms icon.
        // The form does not own an Icon it is given, so it is kept here to dispose.
        _appIcon = AppIcon.Load();
        if (_appIcon is not null)
        {
            Icon = _appIcon;
        }

        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10, 8, 10, 6);
        Font = new Font("Segoe UI", 9f);

        // The dialog is taller than a 768 px netbook screen once the preset row is in.
        // Capping the height and scrolling keeps OK reachable: a fixed dialog that cannot
        // shrink would hide its own buttons on such a screen. The groups are laid out in two
        // short columns so the whole form, Reset nudges included, fits above the footer
        // without needing that scroll. The retained height rule is 740 px, so that is the cap.
        AutoScroll = true;
        MaximumSize = new Size(1100, 740);

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

        _showCpu = new CheckBox { Text = "CPU", Checked = config.ShowCpu, AutoSize = true };
        _showRam = new CheckBox { Text = "RAM", Checked = config.ShowRam, AutoSize = true };
        _showGpu = new CheckBox { Text = "GPU", Checked = config.ShowGpu, AutoSize = true };
        _showDisk = new CheckBox { Text = "Disk", Checked = config.ShowDisk, AutoSize = true };
        _showBattery = new CheckBox { Text = "Battery (when on battery)", Checked = config.ShowBattery, AutoSize = true };

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
        _arrowSize = CreateArrowSizeBox(config.ArrowSizePercent);
        _arrowStyle = CreateArrowStyleBox(config.ArrowStyle);
        _downloadTextMode = CreateTextColorBox(config.DownloadTextColorMode);
        _uploadTextMode = CreateTextColorBox(config.UploadTextColorMode);
        _downloadTextColorButton = CreateColorButton("Download text colour");
        _uploadTextColorButton = CreateColorButton("Upload text colour");

        _bold = new CheckBox { Text = "Bold", Checked = config.FontBold, AutoSize = true };
        _italic = new CheckBox { Text = "Italic", Checked = config.FontItalic, AutoSize = true };
        _roundedBars = new CheckBox { Text = "Rounded caps", Checked = config.RoundedBars, AutoSize = true };
        _accentGlow = new CheckBox { Text = "Glow around the fill", Checked = config.AccentGlow, AutoSize = true };

        _presetBox = CreatePresetBox();
        _presetBox.SelectedIndexChanged += (_, _) => ApplySelectedPreset();

        _presetNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 2),
            ForeColor = SystemColors.GrayText,
        };

        var presets = CreateGroup("Presets");
        AddRow(presets, "Look:", _presetBox);
        AddRow(presets, string.Empty, _presetNote);

        // Filling the saved-look picker decides whether Delete is enabled, so both buttons
        // exist before the picker is built.
        _savePresetButton = new Button
        {
            Text = "Save current",
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            AccessibleDescription = "Saves the colours and shape currently shown as a preset "
                + "you can pick again later. Only the look is saved.",
        };
        _savePresetButton.Click += (_, _) => SaveCurrentLookAsPreset();

        _deletePresetButton = new Button
        {
            Text = "Delete",
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            AccessibleDescription = "Removes the saved preset selected on the left.",
        };
        _deletePresetButton.Click += (_, _) => DeleteSelectedUserPreset();

        _userBox = CreateUserPresetBox();
        _userBox.SelectedIndexChanged += (_, _) => ApplySelectedUserPreset();

        var savedRow = Combine(_userBox, _savePresetButton, _deletePresetButton);
        savedRow.Margin = new Padding(0, 4, 0, 4);
        AddRow(presets, "Saved looks:", savedRow);

        var position = CreateGroup("Position");
        AddRow(position, "Offset from tray (px):", _offsetFromTray);
        AddRow(position, "Nudge left / right (px):", Combine(_nudgeX, _nudgeLeft, _nudgeRight));
        AddRow(position, "Nudge up / down (px):", Combine(_nudgeY, _nudgeUp, _nudgeDown));
        AddRow(position, string.Empty, _resetNudge);

        var look = CreateGroup("Look");
        AddRow(look, "Style:", _style);
        AddRow(look, "Stacking:", _sortOrder);
        AddRow(look, "Font:", _fontFamily);
        AddRow(look, "Arrow size:", _arrowSize);
        AddRow(look, "Arrow type:", _arrowStyle);
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
        AddRow(colours, "Download text:", Combine(_downloadTextMode, _downloadTextColorButton));
        AddRow(colours, "Upload text:", Combine(_uploadTextMode, _uploadTextColorButton));
        AddRow(colours, "Refresh (ms):", _interval);
        AddRow(colours, "Scaling:", _autoScale);
        AddRow(colours, "Bar scale:", _sharedScale);
        AddRow(colours, "Max scale (Mbps):", _maxScale);
        AddRow(colours, "Appearance:", _showBackdrop);
        AddRow(colours, "Tint opacity:", _backdropOpacity);
        AddRow(colours, "Start-up:", _startWithWindows);

        // The widget-chip checkboxes, laid out side by side in a bare row in the Colours
        // column. They carry their own captions, so a group caption would repeat what the
        // chips already say.
        var statsChips = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleDescription = "Compact chips under the figures in the widget. They drop away "
                + "when the taskbar is too short for them.",
        };
        statsChips.Controls.Add(_showCpu);
        statsChips.Controls.Add(_showRam);
        statsChips.Controls.Add(_showGpu);
        statsChips.Controls.Add(_showDisk);
        statsChips.Controls.Add(_showBattery);

        var columns = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 3; row++)
        {
            columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Position shares the top row with Presets, and Look sits beside Colours, so the
        // widest row is about 950 px and the tallest column about 425 px. Stacking Position
        // in a third row made the form taller than the height it can show, which cut off the
        // Reset nudges button along with the footer.
        columns.Controls.Add(presets, 0, 0);
        columns.Controls.Add(position, 1, 0);
        columns.Controls.Add(look, 0, 1);
        columns.Controls.Add(colours, 1, 1);
        // The widget-chip row sits in the Colours column, which is a row shorter than the
        // Look column, so the chips do not stretch the form past the cap the footer rule
        // names.
        columns.Controls.Add(statsChips, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            // A bottom margin as well as the top one: hugging the table's edge put the last
            // pixels of OK outside the visible client area.
            Margin = new Padding(0, 4, 0, 4),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 26) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        // The root sizes the form (Dock=None) rather than filling it: docked inside a
        // form that is capped by MaximumSize, the table's client area came out a few pixels
        // narrower than its own children, which pushed OK past the visible edge.
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        root.Controls.Add(columns);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;

        SetColor(_downloadColorButton, config.DownloadColorHex);
        SetColor(_uploadColorButton, config.UploadColorHex);
        SetColor(_downloadTextColorButton, config.DownloadTextColorHex);
        SetColor(_uploadTextColorButton, config.UploadTextColorHex);
        _downloadTextColorButton.Click += (_, _) => PickColor(_downloadTextColorButton);
        _uploadTextColorButton.Click += (_, _) => PickColor(_uploadTextColorButton);

        ok.Click += (_, _) => ApplyDialogResult();

        // Crisping means an opaque plate, so the tint controls follow it rather than
        // silently disagreeing with what the renderer does. Registered before the live
        // wiring so the coupled values are already in place when the change is published.
        _crispText.CheckedChanged += (_, _) => ApplyCrispCoupling();
        ApplyCrispCoupling();

        foreach (Control input in new Control[]
        {
            _style, _sortOrder, _fontFamily, _arrowSize, _arrowStyle, _bold, _italic, _roundedBars,
            _accentGlow, _showText, _showBackdrop, _crispText, _sharedScale, _barThickness, _segmentGap,
            _fontSize, _nudgeX, _nudgeY, _offsetFromTray, _nudgeLeft, _nudgeRight, _nudgeUp, _nudgeDown,
            _resetNudge, _downloadTextMode, _uploadTextMode, _autoScale, _maxScale, _interval,
            _backdropOpacity, _startWithWindows, _showCpu, _showRam, _showGpu, _showDisk, _showBattery,
        })
        {
            WireForLive(input);
        }

        // The picker has to follow hand edits: changing one value away from a preset makes
        // the settings custom, and leaving the old name showing would claim a look that is
        // no longer on screen.
        foreach (Control input in new Control[]
        {
            _style, _sortOrder, _fontFamily, _arrowSize, _arrowStyle, _bold, _italic, _roundedBars,
            _accentGlow, _showBackdrop, _crispText, _barThickness, _segmentGap, _fontSize,
            _downloadTextMode, _uploadTextMode,
        })
        {
            WireForPresetLabel(input);
        }

        SyncPresetLabel();
    }

    /// <summary>
    /// Picking a preset replaces the whole look at once and shows it in the controls, so
    /// the user can see what they are about to get and still adjust a single value before
    /// pressing OK. Only appearance is replaced: cadence, scale, position and the logon
    /// entry are left exactly as the user set them.
    /// </summary>
    private void ApplySelectedPreset()
    {
        ApplyPreset(_presetBox.SelectedItem as string, builtInOnly: true);
    }

    /// <summary>
    /// Applies a look from the saved-look picker. The two pickers share one path so a saved
    /// preset and a built-in cannot drift apart in what they set.
    /// </summary>
    private void ApplySelectedUserPreset()
    {
        if (_applyingPreset || _userBox.SelectedItem is not string name || name == NoSavedPresets)
        {
            return;
        }

        ApplyPreset(name, builtInOnly: false);
    }

    private void ApplyPreset(string? name, bool builtInOnly)
    {
        if (_applyingPreset)
        {
            return;
        }

        WidgetPreset? preset = builtInOnly
            ? WidgetPresets.Find(name)
            : WidgetPresets.Find(name, _userPresets);
        if (preset is null)
        {
            return;
        }

        _applyingPreset = true;
        try
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, BuildResult());
            FillControls(applied);

            // The coupling owns the tint controls, so it is re-applied before the note is
            // written; a crisp preset would otherwise show the plate as off while the
            // renderer drew it on.
            ApplyCrispCoupling();
            _presetNote.Text = preset.Description;
        }
        finally
        {
            _applyingPreset = false;
        }

        PublishLive();
    }

    /// <summary>
    /// Names the built-in preset the controls currently describe, or "Custom" once they have
    /// been edited away from every one of them. Saved looks are deliberately not named here:
    /// they live in their own picker, and one of them matching the current look does not
    /// make it the preset the user chose from this list.
    /// </summary>
    private void SyncPresetLabel()
    {
        if (_applyingPreset)
        {
            return;
        }

        WidgetPreset? match = WidgetPresets.FindByAppearance(BuildResult());
        _applyingPreset = true;
        try
        {
            _presetBox.SelectedItem = match?.Name;
        }
        finally
        {
            _applyingPreset = false;
        }

        _presetNote.Text = match?.Description
            ?? "Custom: your own mix of settings. Pick a preset to start from a known look.";

        SyncUserPresetSelection();
    }

    /// <summary>
    /// Pushes a config into every control that owns an appearance value. The live-change
    /// handlers are already attached, so this is followed by one publish rather than a
    /// burst of them.
    /// </summary>
    private void FillControls(MonitorConfig config)
    {
        _style.SelectedIndex = IndexOfChoice(_style, WidgetStyles.Normalize(config.Style));
        _sortOrder.SelectedIndex = IndexOfChoice(_sortOrder, StackOrder.Normalize(config.SortOrder));
        _arrowStyle.SelectedIndex = IndexOfChoice(_arrowStyle, ArrowStyles.Normalize(config.ArrowStyle));
        _fontFamily.SelectedIndex = IndexOfChoice(_fontFamily, FontCatalog.Normalize(config.FontFamily));
        _arrowSize.Value = Numeric.Clamp(
            config.ArrowSizePercent,
            MonitorConfig.MinArrowSizePercent,
            MonitorConfig.MaxArrowSizePercent) / 100m;
        _barThickness.Value = Numeric.Clamp(config.BarThicknessPx, 1, MonitorConfig.MaxBarThicknessPx);
        _segmentGap.Value = Numeric.Clamp(config.SegmentGapPx, 0, MonitorConfig.MaxSegmentGapPx);
        _fontSize.Value = (decimal)Numeric.Clamp(config.FontSizePt, 6, 20);
        _bold.Checked = config.FontBold;
        _italic.Checked = config.FontItalic;
        _roundedBars.Checked = config.RoundedBars;
        _accentGlow.Checked = config.AccentGlow;

        _showCpu.Checked = config.ShowCpu;
        _showRam.Checked = config.ShowRam;
        _showGpu.Checked = config.ShowGpu;
        _showDisk.Checked = config.ShowDisk;
        _showBattery.Checked = config.ShowBattery;

        // Crisp text forces an opaque plate (ApplyCrispCoupling), so it is set before the
        // tint is, otherwise switching crisp text off would leave the previous preset's
        // backdrop switched on and the picker would report a look nobody chose.
        _crispText.Checked = config.CrispText;
        _showBackdrop.Checked = config.ShowBackdrop;
        _backdropOpacity.Value = Numeric.Clamp(config.BackdropOpacity, 0, 255);

        SetColor(_downloadColorButton, config.DownloadColorHex);
        SetColor(_uploadColorButton, config.UploadColorHex);
        SetColor(_downloadTextColorButton, config.DownloadTextColorHex);
        SetColor(_uploadTextColorButton, config.UploadTextColorHex);

        _downloadTextMode.SelectedIndex = IndexOfChoice(_downloadTextMode, TextColorModes.Normalize(config.DownloadTextColorMode));
        _uploadTextMode.SelectedIndex = IndexOfChoice(_uploadTextMode, TextColorModes.Normalize(config.UploadTextColorMode));
    }

    private ComboBox CreatePresetBox()
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230,
            AccessibleDescription = "A ready-made look. Choosing one sets the colours, bars, "
                + "arrow, type and tint together; the rest of the settings are left alone.",
        };

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            combo.Items.Add(preset.Name);
        }

        return combo;
    }

    /// <summary>
    /// The picker for the user's own looks. It starts with a placeholder, because nothing is
    /// selected until the user picks one - the look on screen may match no saved preset at
    /// all, and a picker that claimed one would be lying.
    /// </summary>
    private ComboBox CreateUserPresetBox()
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            AccessibleDescription = "Looks you saved yourself. Choosing one applies it; Delete "
                + "removes it.",
        };

        FillUserPresetBox(combo);
        return combo;
    }

    /// <summary>
    /// Rebuilds the saved-look picker from <see cref="_userPresets"/>. Called on open and
    /// after a save or delete, so the list is never a step behind the file.
    /// </summary>
    private void FillUserPresetBox(ComboBox combo, string? select = null)
    {
        // Rebuilding fires SelectedIndexChanged, which applies the entry. That would repaint
        // the widget to a different look just because a preset was saved or deleted, so the
        // guard is held for the whole rebuild.
        bool wasApplying = _applyingPreset;
        _applyingPreset = true;
        combo.BeginUpdate();
        try
        {
            combo.Items.Clear();
            foreach (WidgetPreset preset in _userPresets)
            {
                combo.Items.Add(preset.Name);
            }

            if (combo.Items.Count == 0)
            {
                combo.Items.Add(NoSavedPresets);
                combo.SelectedIndex = 0;
            }
            else
            {
                // Nothing selected by default: the entry above may be a different look, and a
                // picker that names it would be claiming the controls already show it. The
                // caller passes a name only when the controls really are that look.
                int index = select is null ? -1 : combo.Items.IndexOf(select);
                combo.SelectedIndex = index;
            }
        }
        finally
        {
            combo.EndUpdate();
            _applyingPreset = wasApplying;
        }

        _deletePresetButton.Enabled = _userPresets.Count > 0;
    }

    /// <summary>
    /// Marks the saved look whose appearance the controls currently show, if any. Called
    /// once the controls are filled, so the picker never names a look that is not on screen.
    /// </summary>
    private void SyncUserPresetSelection()
    {
        if (_userBox is null || _userPresets.Count == 0)
        {
            return;
        }

        WidgetPreset? match = WidgetPresets.FindByAppearance(BuildResult(), _userPresets);

        bool wasApplying = _applyingPreset;
        _applyingPreset = true;
        try
        {
            _userBox.SelectedIndex = match is null ? -1 : _userBox.Items.IndexOf(match.Name);
        }
        finally
        {
            _applyingPreset = wasApplying;
        }
    }

    /// <summary>
    /// Takes the look currently shown in the controls, asks for a name, and saves it. The
    /// new entry is selected immediately, so the user sees the preset they just made rather
    /// than wondering whether it took.
    /// </summary>
    private void SaveCurrentLookAsPreset()
    {
        if (_presetPath is null)
        {
            return;
        }

        string? typed = PresetNamePrompt.Ask(this, SuggestPresetName(), ValidateNewPresetName);
        if (typed is null)
        {
            return;
        }

        // Built through the same DTO the file uses, so a preset saved now and the same
        // preset read back from disk are identical - one normalization, both directions.
        WidgetPreset preset = UserPreset.FromConfig(UserPresets.NormalizeName(typed), BuildResult()).ToPreset();

        bool saved = UserPresets.TrySave(preset, _presetPath, out IReadOnlyList<WidgetPreset> stored, out string? error);

        // A failed write keeps the look for this session: losing a name the user has just
        // typed is worse than a preset that vanishes when the dialog closes.
        _userPresets = saved
            ? stored
            : [.. _userPresets.Where(p => !string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)), preset];

        FillUserPresetBox(_userBox, preset.Name);
        SyncPresetLabel();

        _presetNote.Text = saved
            ? $"Saved \"{preset.Name}\". Pick it from Saved looks any time."
            : $"Saved \"{preset.Name}\" for this session only: {Path.GetFileName(_presetPath)} could not be written ({error}).";
    }

    /// <summary>
    /// Removes the selected saved look. Refused while it is the only entry, because an empty
    /// picker would then have nothing to show, and the user can always overwrite instead.
    /// </summary>
    private void DeleteSelectedUserPreset()
    {
        if (_presetPath is null || _userPresets.Count == 0)
        {
            return;
        }

        if (_userPresets.Count <= 1)
        {
            MessageBox.Show(
                this,
                "This is the only saved look. Save another one before deleting it, or just "
                    + "overwrite it by saving under the same name.",
                "Delete preset",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string? selected = _userBox.SelectedItem as string;
        WidgetPreset? preset = WidgetPresets.Find(selected, _userPresets);
        if (preset is null)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"Delete the preset \"{preset.Name}\"? This only removes the saved look; the "
                + $"current settings stay as they are.",
            "Delete preset",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        // Hold on to the neighbour so the picker lands on something predictable instead of
        // jumping back to the first entry, which reads as another look being applied.
        string? neighbour = _userPresets
            .Select(p => p.Name)
            .FirstOrDefault(n => !string.Equals(n, preset.Name, StringComparison.OrdinalIgnoreCase));

        _userPresets = _userPresets
            .Where(p => !string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool written = UserPresets.TryWrite(_presetPath, _userPresets, out string? error);

        FillUserPresetBox(_userBox, neighbour);
        SyncPresetLabel();

        _presetNote.Text = written
            ? $"Deleted \"{preset.Name}\"."
            : $"\"{preset.Name}\" is gone from the list, but {Path.GetFileName(_presetPath)} could not be written ({error}).";
    }

    /// <summary>
    /// Why this name cannot be used for a new preset, or null when it can. Built-in names
    /// are already refused by the store; an existing saved name is refused here so the user
    /// is not silently overwriting a look they kept.
    /// </summary>
    private string? ValidateNewPresetName(string? raw)
    {
        string? problem = UserPresets.ValidateName(raw);
        if (problem is not null)
        {
            return problem;
        }

        string name = UserPresets.NormalizeName(raw);
        foreach (WidgetPreset preset in _userPresets)
        {
            if (string.Equals(UserPresets.NormalizeName(preset.Name), name, StringComparison.OrdinalIgnoreCase))
            {
                return $"You already have a preset called \"{preset.Name}\". Choose another name.";
            }
        }

        return null;
    }

    /// <summary>A free "My look N" suggestion, so the common case is one click.</summary>
    private string SuggestPresetName()
    {
        for (int n = 1; n <= 99; n++)
        {
            string candidate = $"My look {n}";
            if (ValidateNewPresetName(candidate) is null)
            {
                return candidate;
            }
        }

        return "My look";
    }

    private void WireForPresetLabel(Control input)
    {
        switch (input)
        {
            case ComboBox combo:
                combo.SelectedIndexChanged += (_, _) => SyncPresetLabel();
                break;
            case CheckBox check:
                check.CheckedChanged += (_, _) => SyncPresetLabel();
                break;
            case NumericUpDown number:
                number.ValueChanged += (_, _) => SyncPresetLabel();
                break;
        }
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
            ArrowSizePercent = SelectedArrowSize(),
            ArrowStyle = SelectedKey(_arrowStyle, MonitorConfig.DefaultArrowStyle),
            DownloadTextColorMode = SelectedKey(_downloadTextMode, TextColorModes.Auto),
            UploadTextColorMode = SelectedKey(_uploadTextMode, TextColorModes.Auto),
            DownloadTextColorHex = _downloadTextColorButton.Tag as string ?? MonitorConfig.DefaultTextColor,
            UploadTextColorHex = _uploadTextColorButton.Tag as string ?? MonitorConfig.DefaultTextColor,
            FontBold = _bold.Checked,
            FontItalic = _italic.Checked,
            RoundedBars = _roundedBars.Checked,
            AccentGlow = _accentGlow.Checked,
            CrispText = _crispText.Checked,
            ShowCpu = _showCpu.Checked,
            ShowRam = _showRam.Checked,
            ShowGpu = _showGpu.Checked,
            ShowDisk = _showDisk.Checked,
            ShowBattery = _showBattery.Checked,
        }.Normalize();
    }

    private void WireForLive(Control input)
    {
        switch (input)
        {
            case ComboBox combo:
                combo.SelectedIndexChanged += (_, _) => PublishLive();
                break;
            case CheckBox check:
                check.CheckedChanged += (_, _) => PublishLive();
                break;
            case NumericUpDown number:
                number.ValueChanged += (_, _) => PublishLive();
                break;
        }
    }

    /// <summary>
    /// Hands the settings as they are right now to the running widget, so every change
    /// is visible on the real taskbar the moment it is made. Nothing is saved until the
    /// user accepts the dialog.
    /// </summary>
    private void PublishLive()
    {
        LiveChanged?.Invoke(this, BuildResult());
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

    /// <summary>
    /// The arrow-size spinner. It steps in tenths of the figure's size, so 0.5, 0.6, 1,
    /// 2 and anything in between are all reachable by typing or by the arrows, and it
    /// never offers a value outside <see cref="MonitorConfig.ArrowSizePercent"/>'s bounds.
    /// Its right-click menu jumps to the round stops instead of stepping one at a time.
    /// </summary>
    private static NumericUpDown CreateArrowSizeBox(int selectedPercent)
    {
        int percent = Numeric.Clamp(selectedPercent, MonitorConfig.MinArrowSizePercent, MonitorConfig.MaxArrowSizePercent);
        var box = new NumericUpDown
        {
            Minimum = MonitorConfig.MinArrowSizePercent / 100m,
            Maximum = MonitorConfig.MaxArrowSizePercent / 100m,
            DecimalPlaces = 1,
            Increment = MonitorConfig.ArrowSizeStep / 100m,
            Value = percent / 100m,
            Width = 70,
            AccessibleDescription = "How tall the direction arrow is drawn, as a multiple of "
                + "the figure's size: 1 is the same size, 0.5 is half, 2 is twice.",
        };

        var menu = new ContextMenuStrip();
        foreach (int stop in ArrowSizeChoiceKeys(percent))
        {
            var item = new ToolStripMenuItem($"{stop / 100.0:0.0}x");
            int target = stop;
            item.Click += (_, _) =>
                box.Value = Numeric.Clamp(target, MonitorConfig.MinArrowSizePercent, MonitorConfig.MaxArrowSizePercent) / 100m;
            menu.Items.Add(item);
        }

        box.ContextMenuStrip = menu;
        return box;
    }

    /// <summary>
    /// The round stops the spinner's menu offers, plus the value it is on if that is not
    /// one of them, so a size set by hand is never dropped from the list. Sorted and
    /// clamped, because the menu shows them in order and never outside the bounds.
    /// </summary>
    internal static IReadOnlyList<int> ArrowSizeChoiceKeys(int selected)
    {
        int current = Numeric.Clamp(selected, MonitorConfig.MinArrowSizePercent, MonitorConfig.MaxArrowSizePercent);

        var stops = new List<int>
        {
            MonitorConfig.MinArrowSizePercent,
            MonitorConfig.DefaultArrowSizePercent,
            2 * MonitorConfig.DefaultArrowSizePercent,
            MonitorConfig.MaxArrowSizePercent,
        };

        if (!stops.Contains(current))
        {
            stops.Add(current);
        }

        stops.Sort();
        return stops;
    }

    private int SelectedArrowSize()
        => Numeric.Clamp(
            (int)Math.Round(_arrowSize.Value * 100m),
            MonitorConfig.MinArrowSizePercent,
            MonitorConfig.MaxArrowSizePercent);

    private static ComboBox CreateArrowStyleBox(string selected)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230,
            AccessibleDescription = "The shape of the arrow drawn beside each figure.",
        };

        foreach (string style in ArrowStyles.All)
        {
            combo.Items.Add(new Choice(style, ArrowStyles.Describe(style)));
        }

        combo.SelectedIndex = IndexOfChoice(combo, ArrowStyles.Normalize(selected));
        return combo;
    }

    private static ComboBox CreateTextColorBox(string selected)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            AccessibleDescription = "What colour the speed figure is drawn in.",
        };

        foreach (string mode in TextColorModes.All)
        {
            combo.Items.Add(new Choice(mode, TextColorModes.Describe(mode)));
        }

        combo.SelectedIndex = IndexOfChoice(combo, TextColorModes.Normalize(selected));
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
            PublishLive();
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

    // Test surface. The preset behaviour worth checking is the wiring between the picker
    // and the controls, and that cannot be observed from outside the form: the point of a
    // preset is that it moves controls the user would otherwise set by hand. These members
    // only read and drive the real controls - no production path uses them.

    internal int PresetCountForTest => _presetBox.Items.Count;

    internal string? SelectedPresetForTest => _presetBox.SelectedItem as string;

    internal string StyleForTest => SelectedKey(_style, WidgetStyles.Bars);

    internal bool BackdropForTest => _showBackdrop.Checked;

    internal bool CrispForTest => _crispText.Checked;

    internal int BarThicknessForTest => (int)_barThickness.Value;

    internal string DownloadColorForTest => _downloadColorButton.Tag as string ?? string.Empty;

    internal void ChoosePresetForTest(string name)
    {
        WidgetPreset preset = WidgetPresets.Find(name)
            ?? throw new ArgumentException($"no preset named {name}", nameof(name));

        _presetBox.SelectedItem = preset.Name;
    }

    internal void SetBarThicknessForTest(int px)
        => _barThickness.Value = Numeric.Clamp(px, 1, MonitorConfig.MaxBarThicknessPx);

    internal void FillForTest(MonitorConfig config) => FillControls(config);

    /// <summary>The saved-look entries the picker is showing, in order.</summary>
    internal IReadOnlyList<string> SavedPresetNamesForTest
        => [.. _userBox.Items.Cast<string>()];

    internal string? SelectedSavedPresetForTest => _userBox.SelectedItem as string;

    internal bool CanDeleteSavedPresetForTest => _deletePresetButton.Enabled;

    /// <summary>
    /// Chooses a saved look the way the user would, through the picker, so the wiring is
    /// what a test exercises rather than the apply method behind it.
    /// </summary>
    internal void SelectSavedPresetForTest(string name)
    {
        int index = _userBox.Items.IndexOf(name);
        if (index < 0)
        {
            throw new ArgumentException($"no saved preset named {name}", nameof(name));
        }

        _userBox.SelectedIndex = index;
    }

    /// <summary>
    /// The body of the Save current button without its name prompt, so a test can supply the
    /// name. Kept as the same code path: the prompt only decides the string.
    /// </summary>
    internal bool SavePresetForTest(string name)
    {
        if (_presetPath is null || ValidateNewPresetName(name) is not null)
        {
            return false;
        }

        WidgetPreset preset = UserPreset.FromConfig(UserPresets.NormalizeName(name), BuildResult()).ToPreset();
        bool saved = UserPresets.TrySave(preset, _presetPath, out IReadOnlyList<WidgetPreset> stored, out string? error);

        _userPresets = saved
            ? stored
            : [.. _userPresets.Where(p => !string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)), preset];

        FillUserPresetBox(_userBox, preset.Name);
        SyncPresetLabel();
        _presetNote.Text = saved ? preset.Name : error ?? string.Empty;
        return saved;
    }

    /// <summary>Removes a saved look without the confirmation dialog, for tests.</summary>
    internal void DeletePresetForTest(string name)
    {
        if (_presetPath is null)
        {
            return;
        }

        string? neighbour = _userPresets
            .Select(p => p.Name)
            .FirstOrDefault(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        _userPresets = _userPresets
            .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        UserPresets.TryWrite(_presetPath, _userPresets, out _);
        FillUserPresetBox(_userBox, neighbour);
        SyncPresetLabel();
    }

    internal string? NoteForTest => _presetNote.Text;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
