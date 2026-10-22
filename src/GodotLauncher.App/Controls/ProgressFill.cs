using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GodotLauncher.App.Controls;

/// <summary>
/// Paints a horizontal progress fill that can sit behind a button label. The control keeps the
/// button's shape, so the label stays readable while the background itself becomes the progress bar.
/// </summary>
public sealed class ProgressFill : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ProgressFill, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Track));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<ProgressFill, double>(nameof(CornerRadius), 8);

    static ProgressFill()
    {
        AffectsRender<ProgressFill>(ProgressProperty, FillProperty, TrackProperty, CornerRadiusProperty);
    }

    /// <summary>Gets or sets the completed fraction, clamped to 0 through 1.</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Gets or sets the brush of the completed part.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the brush of the remaining part.</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>Gets or sets the corner radius used to clip both parts.</summary>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var radius = Math.Max(0, CornerRadius);
        using (context.PushClip(new RoundedRect(bounds, radius)))
        {
            context.DrawRectangle(Track, null, bounds);

            var fraction = Math.Clamp(Progress, 0, 1);
            if (fraction > 0)
            {
                context.DrawRectangle(Fill, null, new Rect(0, 0, bounds.Width * fraction, bounds.Height));
            }
        }
    }
}
