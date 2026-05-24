using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ModelViewer.Rendering;
using SharpDX;
using WpfColor = System.Windows.Media.Color;

namespace ModelViewer.Controls;

/// <summary>
/// Encapsulates light-direction sliders, shadow parameters, and color pickers.
/// Raises events whenever any lighting parameter changes.
/// </summary>
internal partial class LightControlPanel : UserControl
{
    /// <summary>
    /// Fired whenever yaw or pitch changes, carrying the resulting 3D direction.
    /// </summary>
    public event Action<Vector3>? LightDirectionChanged;

    /// <summary>
    /// Fired whenever shadow parameters (PcfRadius, ShadowBias, ShadowNormalBias) change.
    /// </summary>
    public event Action<float, float, float>? ShadowParamsChanged;

    /// <summary>
    /// Fired whenever LightColor or AmbientColor changes.
    /// Carries (lightColor, ambientColor) as Vector4.
    /// </summary>
    public event Action<Vector4, Vector4>? LightColorsChanged;

    /// <summary>Whether the panel body is currently expanded.</summary>
    public bool IsExpanded
    {
        get => ContentGrid.Visibility == Visibility.Visible;
        set
        {
            ContentGrid.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (CollapseBtn != null)
                CollapseBtn.Tag = value ? "Expanded" : "Collapsed";
            // Rotate chevron via RenderTransform
            if (CollapseBtn != null)
            {
                var angle = value ? 0d : -90d;
                CollapseBtn.RenderTransform = new RotateTransform(angle);
                CollapseBtn.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            }
        }
    }

        // ── ShadowSettings binding ───────────────────────────────────────────
    /// <summary>
    /// The single source of truth for shadow/lighting parameters.
    /// When set, all UI controls are synced to reflect the current values.
    /// </summary>
    public ShadowSettings? Settings
    {
        get => _settings;
        set
        {
            if (_settings == value) return;

            // Unsubscribe from old instance
            if (_settings != null)
                _settings.PropertyChanged -= Settings_PropertyChanged;

            _settings = value;

            if (_settings != null)
            {
                _settings.PropertyChanged += Settings_PropertyChanged;
                SyncUiFromSettings();
            }
        }
    }

    private ShadowSettings? _settings;

    /// <summary>
    /// Syncs all UI controls to reflect the current ShadowSettings values.
    /// Called on initial assignment and whenever PropertyChanged fires.
    /// </summary>
    private void SyncUiFromSettings()
    {
        if (_settings == null) return;

        // Shadow parameters
        if (PcfRadiusText != null)
            PcfRadiusText.Text = _settings.PcfRadius.ToString("F3", CultureInfo.InvariantCulture);
        if (ShadowBiasText != null)
            ShadowBiasText.Text = _settings.ShadowBias.ToString("F4", CultureInfo.InvariantCulture);
        if (ShadowNormalBiasText != null)
            ShadowNormalBiasText.Text = _settings.ShadowNormalBias.ToString("F4", CultureInfo.InvariantCulture);

        // Light color
        if (LightColorBtn != null)
        {
            _lightR = _settings.LightColor.X;
            _lightG = _settings.LightColor.Y;
            _lightB = _settings.LightColor.Z;
            LightColorBtn.Background = new SolidColorBrush(
                WpfColor.FromRgb((byte)(_lightR * 255), (byte)(_lightG * 255), (byte)(_lightB * 255)));
        }

        // Ambient color
        if (AmbientColorBtn != null)
        {
            _ambientR = _settings.AmbientColor.X;
            _ambientG = _settings.AmbientColor.Y;
            _ambientB = _settings.AmbientColor.Z;
            AmbientColorBtn.Background = new SolidColorBrush(
                WpfColor.FromRgb((byte)(_ambientR * 255), (byte)(_ambientG * 255), (byte)(_ambientB * 255)));
        }
    }

    /// <summary>
    /// PropertyChanged handler — syncs UI when settings change externally (e.g., Reset()).
    /// </summary>
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Dispatcher.Invoke is safe here because PropertyChanged may fire from any thread
        Dispatcher.Invoke(() => SyncUiFromSettings());
    }

    // ── Cached color state (read back when picker opens) ──
    private float _lightR, _lightG, _lightB;
    private float _ambientR, _ambientG, _ambientB;

    public LightControlPanel()
    {
        InitializeComponent();
        // Default: expanded
        IsExpanded = true;
    }

    /// <summary>Toggles the panel body visibility.</summary>
    private void OnCollapseToggle_Click(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    private void OnSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: during XAML initialization not all controls exist yet
        if (YawSlider == null || PitchSlider == null)
            return;

        // Update the display text for the changed slider
        if (sender == YawSlider)
            YawText.Text = $"{e.NewValue:F0}°";
        else if (sender == PitchSlider)
            PitchText.Text = $"{e.NewValue:F0}°";

        // Convert yaw / pitch to a 3D direction vector
        float yawRad = (float)(YawSlider.Value * Math.PI / 180.0);
        float pitchRad = (float)((PitchSlider.Value - 90.0) * Math.PI / 180.0);

        float x = (float)(Math.Cos(pitchRad) * Math.Cos(yawRad));
        float y = (float)Math.Sin(pitchRad);
        float z = (float)(Math.Cos(pitchRad) * Math.Sin(yawRad));

        var direction = new Vector3(x, y, z);
        direction.Normalize();

                // Notify parent
        LightDirectionChanged?.Invoke(direction);
    }

        private void OnShadowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PcfRadiusText == null || ShadowBiasText == null || ShadowNormalBiasText == null)
            return;

        float pcf = ReadShadowValue(PcfRadiusText);
        float bias = ReadShadowValue(ShadowBiasText);
        float normalBias = ReadShadowValue(ShadowNormalBiasText);

        ShadowParamsChanged?.Invoke(pcf, bias, normalBias);
    }

        /// <summary>Raised by DraggableNumberBehavior on every drag step — commits live.</summary>
    private void OnShadowDragValueChanged(object? sender, RoutedEventArgs e) =>
        OnShadowSlider_ValueChanged(null!, default!);

    /// <summary>Shared LostFocus handler for all shadow TextBoxes.</summary>
    private void OnShadowText_LostFocus(object? sender, RoutedEventArgs e) =>
        OnShadowSlider_ValueChanged(null!, default!);

    /// <summary>Shared KeyDown handler — commits on Enter.</summary>
    private void OnShadowText_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnShadowSlider_ValueChanged(null!, default!);
    }

    /// <summary>Reads a float from a shadow TextBox, returning 0 on parse failure.</summary>
    private static float ReadShadowValue(TextBox tb)
    {
        return float.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : 0f;
    }

        /// <summary>
    /// Opens a color picker dialog for the clicked button (LightColor or AmbientColor).
    /// </summary>
    private void OnColorPickerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        // Determine which color we're picking
        byte r, g, b;
        if (btn == LightColorBtn)
        {
            r = (byte)(_lightR * 255);
            g = (byte)(_lightG * 255);
            b = (byte)(_lightB * 255);
        }
        else
        {
            r = (byte)(_ambientR * 255);
            g = (byte)(_ambientG * 255);
            b = (byte)(_ambientB * 255);
        }

        var result = ColorPickerDialog.Show(WpfColor.FromArgb(255, r, g, b));
        if (!result.HasValue)
            return;

        var chosen = result.Value;
        float rf = chosen.R / 255f;
        float gf = chosen.G / 255f;
        float bf = chosen.B / 255f;

        if (btn == LightColorBtn)
        {
            _lightR = rf; _lightG = gf; _lightB = bf;
            btn.Background = new SolidColorBrush(chosen);

            // Fire with current ambient too
            LightColorsChanged?.Invoke(
                new Vector4(_lightR, _lightG, _lightB, 1f),
                new Vector4(_ambientR, _ambientG, _ambientB, 1f));
        }
        else
        {
            _ambientR = rf; _ambientG = gf; _ambientB = bf;
            btn.Background = new SolidColorBrush(chosen);

            // Fire with current light too
            LightColorsChanged?.Invoke(
                new Vector4(_lightR, _lightG, _lightB, 1f),
                new Vector4(_ambientR, _ambientG, _ambientB, 1f));
        }
    }
}

/// <summary>
/// Lightweight WPF color picker dialog — no WinForms dependency.
/// Shows R/G/B sliders and a live preview swatch.
/// </summary>
internal static class ColorPickerDialog
{
        public static WpfColor? Show(WpfColor initialColor)
    {
        var win = new Window
        {
            Title = "Pick Color",
            Width = 260,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = Brushes.White,
            WindowStyle = WindowStyle.ToolWindow,
        };

        var root = new StackPanel { Margin = new Thickness(12) };

        // ── Preview swatch ──
        var swatch = new Border
        {
            Background = new SolidColorBrush(initialColor),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Height = 60,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(swatch);

        // ── R / G / B sliders ──
        byte[] values = { initialColor.R, initialColor.G, initialColor.B };
        string[] labels = { "Red", "Green", "Blue" };
        var sliders = new Slider[3];

                for (int i = 0; i < 3; i++)
        {
            int channel = i; // capture for lambda
            var label = new TextBlock
            {
                Text = labels[channel],
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            };
            root.Children.Add(label);

            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };

            var valText = new TextBlock
            {
                Text = values[channel].ToString(),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 10,
                Width = 28,
            };
            DockPanel.SetDock(valText, Dock.Right);
            dock.Children.Add(valText);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                Value = values[channel],
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Height = 16,
            };
            dock.Children.Add(slider);
            root.Children.Add(dock);

            sliders[channel] = slider;

            // Update swatch live
            slider.ValueChanged += (_, ea) =>
            {
                values[channel] = (byte)ea.NewValue;
                valText.Text = values[channel].ToString();
                swatch.Background = new SolidColorBrush(
                    WpfColor.FromArgb(255, values[0], values[1], values[2]));
            };
        }

        // ── Hex readout ──
        var hexText = new TextBlock
        {
            Text = $"#{initialColor.R:X2}{initialColor.G:X2}{initialColor.B:X2}",
                        Foreground = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(hexText);

                // Update hex text live
        for (int i = 0; i < 3; i++)
        {
            sliders[i].ValueChanged += (_, _) =>
            {
                hexText.Text = $"#{values[0]:X2}{values[1]:X2}{values[2]:X2}";
            };
        }

        root.Children.Add(new DockPanel { Margin = new Thickness(0, 16, 0, 0) }); // spacer

        // ── Buttons ──
        var btnPanel = new DockPanel();
        var cancelBtn = new Button { Content = "Cancel", Width = 70, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
        var okBtn = new Button { Content = "OK", Width = 70, HorizontalAlignment = HorizontalAlignment.Right };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        root.Children.Add(btnPanel);

        win.Content = root;

                cancelBtn.Click += (_, _) => { win.DialogResult = false; };
        okBtn.Click += (_, _) => { win.DialogResult = true; };

        bool? shown = win.ShowDialog();
        if (shown != true)
            return null;

        // okBtn click closes window; read final values
        return System.Windows.Media.Color.FromArgb(255, values[0], values[1], values[2]);
    }
}
