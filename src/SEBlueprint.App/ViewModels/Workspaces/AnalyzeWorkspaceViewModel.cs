using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SEBlueprint.Core.Analysis;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.App.ViewModels.Workspaces;

/// <summary>One row in the "weakest spots" list.</summary>
public sealed record AnalysisRow(Block Block, string Name, string HitPoints, string Direction, Brush Swatch);

/// <summary>
/// Read-only combat analysis. Currently: an armor heat map, either of hull thickness or of how exposed the
/// ship's functional blocks are. Results are recomputed when the blueprint changes.
/// </summary>
public sealed partial class AnalyzeWorkspaceViewModel : WorkspaceViewModel
{
    /// <summary>Heat-map scale (log): at or below this many HP is full red...</summary>
    public const float RedHitPoints = 1000f;

    /// <summary>...and at or above this many HP is full green. One heavy armor block is 16,500 HP.</summary>
    public const float GreenHitPoints = 50000f;

    private const int MaxRows = 100;

    /// <summary>Block types whose loss cripples a ship; the "critical only" filter keeps just these.</summary>
    private static readonly HashSet<string> CriticalTypes = new(StringComparer.Ordinal)
    {
        "Reactor", "BatteryBlock", "HydrogenEngine", "Cockpit", "RemoteControl", "Gyro", "JumpDrive",
        "OxygenTank", "OxygenGenerator", "MyProgrammableBlock", "TimerBlock", "SurvivalKit", "MedicalRoom",
        "CryoChamber", "BasicMissionBlock", "DefensiveCombatBlock", "OffensiveCombatBlock", "FlightMovementBlock",
        "TurretControlBlock", "EventControllerBlock", "Projector", "CargoContainer",
    };

    private ArmorAnalysisResult? _result;
    private Blueprint? _analyzedBlueprint;
    private bool _stale = true;
    private int _runId;

    public AnalyzeWorkspaceViewModel(MainViewModel main)
        : base(main)
    {
        LegendBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new(ToColor(HeatColor(0f)), 0),
            new(ToColor(HeatColor(0.5f)), 0.5),
            new(ToColor(HeatColor(1f)), 1),
        }, 0);
        LegendBrush.Freeze();
    }

    public override string Name => "Analyze";

    public override string Description => "Armor heat maps: find thin hull spots and exposed components. Read-only.";

    public override bool AllowsEditing => false;

    /// <summary>True = hull thickness view, false = exposed components view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsExposure), nameof(Explanation))]
    private bool _showsThickness = true;

    /// <summary>In the exposure view, list and color only critical blocks (reactors, cockpits, gyros...).</summary>
    [ObservableProperty]
    private bool _criticalOnly = true;

    [ObservableProperty]
    private bool _isComputing;

    [ObservableProperty]
    private string _summary = string.Empty;

    public bool ShowsExposure
    {
        get => !ShowsThickness;
        set => ShowsThickness = !value;
    }

    public string Explanation => ShowsThickness
        ? "Outer armor colored by the hit points of armor directly behind it (measured straight inward). Red = thin spots."
        : "Armor shown see-through. Components colored by the hit points that must be shot through to reach them from their weakest direction (26 checked).";

    public ObservableCollection<AnalysisRow> Rows { get; } = new();

    public LinearGradientBrush LegendBrush { get; }

    public string LegendLow => $"≤ {RedHitPoints:N0} HP";

    public string LegendMid => $"~{System.Math.Sqrt(RedHitPoints * GreenHitPoints):N0}";

    public string LegendHigh => $"≥ {GreenHitPoints:N0} HP";

    public override void OnActivated() => _ = RefreshAsync();

    public override void OnBlueprintChanged()
    {
        _stale = true;
        if (Main.ActiveWorkspace == this)
            _ = RefreshAsync();
    }

    partial void OnShowsThicknessChanged(bool value)
    {
        _stale = true;
        _ = RefreshAsync();
    }

    partial void OnCriticalOnlyChanged(bool value) => ApplyResult();

    [RelayCommand]
    private void SelectRow(AnalysisRow? row)
    {
        if (row is not null)
            Main.SelectBlockFromViewport(row.Block, toggle: false);
    }

    private async Task RefreshAsync()
    {
        var blueprint = Main.Blueprint;
        if (blueprint is null)
        {
            _result = null;
            Rows.Clear();
            Summary = "Open a blueprint to analyze it.";
            ColorOverride = null;
            return;
        }
        if (!_stale && _analyzedBlueprint == blueprint && _result is not null)
        {
            ApplyResult();
            return;
        }

        var run = ++_runId;
        var kind = ShowsThickness ? ArmorAnalysisKind.Thickness : ArmorAnalysisKind.Exposure;
        var definitions = Main.Definitions;
        IsComputing = true;
        Summary = "Analyzing…";
        try
        {
            var result = await Task.Run(() => ArmorAnalysis.Analyze(blueprint, definitions, kind));
            if (run != _runId)
                return; // a newer request superseded this one
            _result = result;
            _analyzedBlueprint = blueprint;
            _stale = false;
            ApplyResult();
        }
        finally
        {
            if (run == _runId)
                IsComputing = false;
        }
    }

    /// <summary>Fills the list and sets the 3D colors from the current result and filters.</summary>
    private void ApplyResult()
    {
        if (_result is not { } result)
            return;
        var definitions = Main.Definitions;
        bool Listed(ArmorMeasurement m) => result.Kind == ArmorAnalysisKind.Thickness || !CriticalOnly || IsCritical(definitions.Get(m.Block));

        var listed = result.Weakest.Where(Listed).ToList();
        Rows.Clear();
        foreach (var m in listed.Take(MaxRows))
        {
            var def = definitions.Get(m.Block);
            var name = string.IsNullOrEmpty(m.Block.CustomName) ? def.DisplayName : m.Block.CustomName!;
            var swatch = new SolidColorBrush(ToColor(HeatColor(HeatFraction(m.HitPoints))));
            swatch.Freeze();
            Rows.Add(new AnalysisRow(m.Block, name, $"{m.HitPoints:N0} HP", DirectionName(m.WeakestDirection, result.Kind), swatch));
        }

        if (listed.Count == 0)
        {
            Summary = result.Kind == ArmorAnalysisKind.Thickness ? "No outer armor found." : "No matching components found.";
        }
        else
        {
            var red = listed.Count(m => m.HitPoints <= RedHitPoints);
            var what = result.Kind == ArmorAnalysisKind.Thickness ? "outer armor blocks" : CriticalOnly ? "critical components" : "components";
            Summary = $"{listed.Count:N0} {what} measured; {red:N0} at or below {RedHitPoints:N0} HP. Weakest first:";
        }

        var measurements = result.Measurements;
        var kind = result.Kind;
        var criticalOnly = CriticalOnly;
        ColorOverride = block =>
        {
            var def = definitions.Get(block);
            if (kind == ArmorAnalysisKind.Thickness)
            {
                return measurements.TryGetValue(block, out var m)
                    ? new Vector4(HeatColor(HeatFraction(m.HitPoints)), 1f)
                    : new Vector4(0.35f, 0.37f, 0.4f, 1f); // interior or functional: neutral
            }

            if (def.IsStructural)
                return new Vector4(0.6f, 0.68f, 0.78f, 0.07f); // ghosted armor so components show through
            if (measurements.TryGetValue(block, out var e) && (!criticalOnly || IsCritical(def)))
                return new Vector4(HeatColor(HeatFraction(e.HitPoints)), 1f);
            // Components filtered out by "critical only" are hidden (alpha 0 → not drawn at all). Drawing ~1,000
            // detailed models semi-transparently cost a third of the frame rate on large ships.
            return Vector4.Zero;
        };
    }

    private static bool IsCritical(BlockDefinition def) => CriticalTypes.Contains(def.TypeId);

    /// <summary>0 (red) .. 1 (green) on a log scale, so 1k → 2.5k → 16.5k → 50k HP are evenly spread.</summary>
    public static float HeatFraction(float hitPoints)
    {
        if (hitPoints <= RedHitPoints)
            return 0f;
        var t = (MathF.Log(hitPoints) - MathF.Log(RedHitPoints)) / (MathF.Log(GreenHitPoints) - MathF.Log(RedHitPoints));
        return System.Math.Clamp(t, 0f, 1f);
    }

    /// <summary>Red → yellow → green.</summary>
    public static Vector3 HeatColor(float t)
    {
        var red = new Vector3(0.85f, 0.12f, 0.1f);
        var yellow = new Vector3(0.95f, 0.8f, 0.12f);
        var green = new Vector3(0.15f, 0.72f, 0.25f);
        return t < 0.5f ? Vector3.Lerp(red, yellow, t * 2f) : Vector3.Lerp(yellow, green, (t - 0.5f) * 2f);
    }

    /// <summary>Names a grid direction like "Front-Left"; for thickness, the side the thin face points to.</summary>
    private static string DirectionName(Vector3I d, ArmorAnalysisKind kind)
    {
        var parts = new List<string>(3);
        if (d.Z < 0) parts.Add("Front");
        if (d.Z > 0) parts.Add("Back");
        if (d.Y > 0) parts.Add("Top");
        if (d.Y < 0) parts.Add("Bottom");
        if (d.X < 0) parts.Add("Left");
        if (d.X > 0) parts.Add("Right");
        var name = parts.Count == 0 ? "—" : string.Join("-", parts);
        return kind == ArmorAnalysisKind.Thickness ? $"{name} face" : $"from {name}";
    }

    private static Color ToColor(Vector3 c) =>
        Color.FromRgb((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255));
}
