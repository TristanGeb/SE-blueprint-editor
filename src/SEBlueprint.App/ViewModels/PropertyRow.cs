using System.Windows.Media;

namespace SEBlueprint.App.ViewModels;

/// <summary>One line in the Properties panel. <see cref="Swatch"/> is set for color properties.</summary>
public sealed record PropertyRow(string Name, string Value, Brush? Swatch = null);
