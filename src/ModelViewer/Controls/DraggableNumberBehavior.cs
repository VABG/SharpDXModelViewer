using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModelViewer.Controls;

/// <summary>
/// Attached behavior that lets a <see cref="TextBox"/> respond to horizontal mouse-drag
/// gestures to nudge its numeric value up or down — exactly like the numeric fields
/// in Blender, Maya, 3ds Max, etc.
///
/// Usage in XAML:
///   &lt;TextBox local:DraggableNumberBehavior.IsEnabled="True"
///            local:DraggableNumberBehavior.Sensitivity="0.05"
///            local:DraggableNumberBehavior.Minimum="0.01" /&gt;
///
/// The behavior raises <see cref="DragValueChangedEvent"/> on the TextBox for every
/// drag step.  The parent panel can listen and commit values to the model immediately.
/// </summary>
public static class DraggableNumberBehavior
{
    // ── Attached properties ───────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(DraggableNumberBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Value change per pixel of horizontal drag.  E.g. 0.05 means dragging
    /// 10 px right adds 0.5 to the field value.</summary>
    public static readonly DependencyProperty SensitivityProperty =
        DependencyProperty.RegisterAttached(
            "Sensitivity", typeof(double), typeof(DraggableNumberBehavior),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.RegisterAttached(
            "Minimum", typeof(double?), typeof(DraggableNumberBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.RegisterAttached(
            "Maximum", typeof(double?), typeof(DraggableNumberBehavior),
            new PropertyMetadata(null));

    // ── Public API ────────────────────────────────────────────────

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetSensitivity(DependencyObject element, double value) =>
        element.SetValue(SensitivityProperty, value);

    public static double GetSensitivity(DependencyObject element) =>
        (double)element.GetValue(SensitivityProperty);

    public static void SetMinimum(DependencyObject element, double? value) =>
        element.SetValue(MinimumProperty, value);

    public static double? GetMinimum(DependencyObject element) =>
        (double?)element.GetValue(MinimumProperty);

    public static void SetMaximum(DependencyObject element, double? value) =>
        element.SetValue(MaximumProperty, value);

    public static double? GetMaximum(DependencyObject element) =>
        (double?)element.GetValue(MaximumProperty);

    // ── Per-TextBox drag state ────────────────────────────────────

    private class DragState
    {
        public bool IsDragging;
        public double StartMouseX;
        public double StartValue;
    }

    // ── Hook-up / unhook ──────────────────────────────────────────

    private static void OnIsEnabledChanged(DependencyObject dep, DependencyPropertyChangedEventArgs e)
    {
        if (dep is not TextBox tb) return;

        if ((bool)e.NewValue)
        {
            tb.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
            tb.PreviewMouseMove += OnMouseMove;
            tb.PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
            tb.MouseEnter += OnMouseEnter;
            tb.MouseLeave += OnMouseLeave;
        }
        else
        {
            tb.PreviewMouseLeftButtonDown -= OnMouseLeftButtonDown;
            tb.PreviewMouseMove -= OnMouseMove;
            tb.PreviewMouseLeftButtonUp -= OnMouseLeftButtonUp;
            tb.MouseEnter -= OnMouseEnter;
            tb.MouseLeave -= OnMouseLeave;
        }
    }

    // ── Cursor hint: show H-Arrow when hovering a draggable field ──

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is not DragState)
            tb.Cursor = Cursors.SizeWE;
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is not DragState)
            tb.Cursor = null; // fall back to default I-beam
    }

    // ── Mouse handlers ────────────────────────────────────────────

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        var state = new DragState
        {
            IsDragging = true,
            StartMouseX = Mouse.GetPosition(null).X,  // screen coords (works across windows)
            StartValue = ReadCurrentValue(textBox),
        };

        textBox.Tag = state;
        e.Handled = false; // allow normal text selection
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.Tag is not DragState state) return;
        if (!state.IsDragging) return;
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            state.IsDragging = false;
            textBox.Tag = null;
            return;
        }

        // Only start adjusting after the user moves more than 2 px (jitter threshold)
        double currentX = Mouse.GetPosition(null).X;
        double totalDeltaX = currentX - state.StartMouseX;

        if (Math.Abs(totalDeltaX) < 2.0) return;

        double sensitivity = GetSensitivity(textBox);
        double minimum = GetMinimum(textBox) ?? double.NegativeInfinity;
        double maximum = GetMaximum(textBox) ?? double.PositiveInfinity;

        double newValue = state.StartValue + totalDeltaX * sensitivity;
        newValue = Math.Clamp(newValue, minimum, maximum);

        // Format and write back immediately
        textBox.Text = newValue.ToString("F3", CultureInfo.InvariantCulture);

        // Raise drag-value-changed event so parent can commit to model in real time
        var args = new DragValueChangedEventArgs(
            DragValueChangedEvent, newValue, textBox);
        textBox.RaiseEvent(args);

        // Keep cursor at end so the text doesn't jump around
        textBox.CaretIndex = textBox.Text.Length;
    }

    private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (textBox.Tag is DragState state)
        {
            state.IsDragging = false;
            textBox.Tag = null;

            // Final commit — let the parent know the value is settled
            textBox.RaiseEvent(new RoutedEventArgs(TextBox.LostFocusEvent, textBox));
        }
    }

    // ── Routed event for drag-value changes (bubbles up) ──────────

    public static readonly RoutedEvent DragValueChangedEvent =
        EventManager.RegisterRoutedEvent(
            "DragValueChanged", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DraggableNumberBehavior));

    /// <summary>XAML attachable event wrapper — required naming convention for WPF.</summary>
    public static void AddDragValueChangedHandler(DependencyObject element, RoutedEventHandler handler) =>
        ((UIElement)element).AddHandler(DragValueChangedEvent, handler);

    public static void RemoveDragValueChangedHandler(DependencyObject element, RoutedEventHandler handler) =>
        ((UIElement)element).RemoveHandler(DragValueChangedEvent, handler);

    // ── Helpers ───────────────────────────────────────────────────

    private static double ReadCurrentValue(TextBox textBox)
    {
        if (double.TryParse(textBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double val))
            return val;

        return 0.0;
    }
}

/// <summary>
/// Carries the new dragged value.  Set <see cref="Accepted"/> to <c>false</c>
/// in a handler to reject the change (the TextBox text will revert).
/// </summary>
public class DragValueChangedEventArgs : RoutedEventArgs
{
    public double NewValue { get; }
    public TextBox SourceTextBox { get; }
    public bool Accepted { get; set; } = true;

    public DragValueChangedEventArgs(RoutedEvent routedEvent, double newValue, TextBox sourceTextBox)
        : base(routedEvent)
    {
        NewValue = newValue;
        SourceTextBox = sourceTextBox;
        Accepted = true;
    }
}
