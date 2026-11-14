using System.Drawing;
using TaskbarNetMonitor.Core;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// Small settings dialog: colours, cadence and scale. Values are validated by
/// <see cref="MonitorConfig.Normalize"/> on the way out, so nothing typed here can
/// produce a config the widget cannot draw.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Button _downloadColorButton;
    private readonly Button _uploadColorButton;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _maxScale;
    private readonly NumericUpDown _barThickness;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _backdropOpacity;
    private readonly CheckBox _autoScale;
    private readonly CheckBox _showText;
    private readonly CheckBox _showBackdrop;
    private readonly CheckBox _startWithWindows;

    public SettingsForm(MonitorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Text = "Taskbar Network Monitor \u2013 Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 14, 14, 10);
        Font = new Font("Segoe UI", 9f);

        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _downloadColorButton = CreateColorButton("Download bar colour");
        _downloadColorButton.Click += (_, _) => PickColor(_downloadColorButton);
        _uploadColorButton = CreateColorButton("Upload bar colour");
        _uploadColorButton.Click += (_, _) => PickColor(_uploadColorButton);

        _interval = new NumericUpDown
        {
            Minimum = MonitorConfig.MinRefreshIntervalMs,
            Maximum = MonitorConfig.MaxRefreshIntervalMs,
            Increment = 250,
            Value = Math.Clamp(config.RefreshIntervalMs, MonitorConfig.MinRefreshIntervalMs, MonitorConfig.MaxRefreshIntervalMs),
            Width = 90,
        };

        _autoScale = new CheckBox { Text = "Fit bars to current traffic", Checked = config.AutoScale, AutoSize = true };

        _maxScale = new NumericUpDown
        {
            Minimum = 0.1m,
            Maximum = 100000m,
            DecimalPlaces = 1,
            Increment = 10m,
            Value = (decimal)Math.Clamp(config.MaxScaleMbps, 0.1, 100000),
            Width = 90,
        };

        _showText = new CheckBox { Text = "Show speeds as text", Checked = config.ShowText, AutoSize = true };

        _barThickness = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 12,
            Value = Math.Clamp(config.BarThicknessPx, 1, 12),
            Width = 60,
        };

        _fontSize = new NumericUpDown
        {
            Minimum = 6m,
            Maximum = 20m,
            DecimalPlaces = 1,
            Increment = 0.5m,
            Value = (decimal)Math.Clamp(config.FontSizePt, 6, 20),
            Width = 60,
        };

        _showBackdrop = new CheckBox { Text = "Tint behind the widget", Checked = config.ShowBackdrop, AutoSize = true };

        _backdropOpacity = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 255,
            Value = Math.Clamp(config.BackdropOpacity, 0, 255),
            Width = 60,
        };

        _startWithWindows = new CheckBox { Text = "Start with Windows", Checked = config.StartWithWindows, AutoSize = true };

        AddRow(table, "Download colour:", _downloadColorButton);
        AddRow(table, "Upload colour:", _uploadColorButton);
        AddRow(table, "Refresh (ms):", _interval);
        AddRow(table, "Scaling:", _autoScale);
        AddRow(table, "Max scale (Mbps):", _maxScale);
        AddRow(table, "Bar thickness (px):", _barThickness);
        AddRow(table, "Font size (pt):", _fontSize);
        AddRow(table, "Text:", _showText);
        AddRow(table, "Appearance:", _showBackdrop);
        AddRow(table, "Tint opacity:", _backdropOpacity);
        AddRow(table, "Start-up:", _startWithWindows);

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
        root.Controls.Add(table);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;

        SetColor(_downloadColorButton, config.DownloadColorHex);
        SetColor(_uploadColorButton, config.UploadColorHex);

        ok.Click += (_, _) => ApplyDialogResult();
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
            MaxScaleMbps = (double)_maxScale.Value,
            BarThicknessPx = (int)_barThickness.Value,
            FontSizePt = (double)_fontSize.Value,
            ShowText = _showText.Checked,
            ShowBackdrop = _showBackdrop.Checked,
            BackdropOpacity = (int)_backdropOpacity.Value,
            StartWithWindows = _startWithWindows.Checked,
        }.Normalize();
    }

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

    private static void PickColor(Button button)
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
}
