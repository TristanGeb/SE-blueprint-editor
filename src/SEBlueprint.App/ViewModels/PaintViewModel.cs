using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SEBlueprint.Core.Colors;

namespace SEBlueprint.App.ViewModels;

/// <summary>
/// The paint color chosen in the Paint panel, as the hue/saturation/value sliders the game's color picker uses
/// (hue 0-360, saturation and value 0-100).
/// </summary>
public sealed partial class PaintViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush), nameof(ColorMask))]
    private double _hue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush), nameof(ColorMask))]
    private double _saturation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush), nameof(ColorMask))]
    private double _value = 42;

    /// <summary>The color as SE stores it in the blueprint.</summary>
    public Vector3 ColorMask => SeColor.HsvToMask(new Vector3((float)(Hue / 360), (float)(Saturation / 100), (float)(Value / 100)));

    public Brush PreviewBrush => MaskToBrush(ColorMask);

    /// <summary>Sets the sliders to match a block's existing paint (the eyedropper).</summary>
    public void SetFromMask(Vector3 mask)
    {
        var hsv = SeColor.MaskToHsv(mask);
        Hue = Math.Round(hsv.X * 360);
        Saturation = Math.Round(hsv.Y * 100);
        Value = Math.Round(hsv.Z * 100);
    }

    public static Brush MaskToBrush(Vector3 mask)
    {
        var rgb = SeColor.MaskToRgb(mask);
        var brush = new SolidColorBrush(Color.FromRgb(ToByte(rgb.X), ToByte(rgb.Y), ToByte(rgb.Z)));
        brush.Freeze();
        return brush;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}
