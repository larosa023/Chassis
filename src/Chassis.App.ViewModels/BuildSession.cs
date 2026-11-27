using System.Text.Json;
using System.Text.Json.Nodes;
using Chassis.Core.Bench;
using Chassis.Core.Builds;
using Chassis.Core.Catalog;
using Chassis.Core.Checks;
using Chassis.Core.Storage;

namespace Chassis.App.ViewModels;

public enum SlotState { Empty, Ok, Warning, Problem }

/// <summary>One category in the build strip: what's in it and whether it's happy.</summary>
public sealed record Slot(Category Category, string Label, string Value, SlotState State, decimal Price);

/// <summary>A part you could put in the selected slot, with how it would change the build.</summary>
public sealed record Candidate(Part Part, bool Selected, bool Fits, string? Reason, int ScoreDelta)
{
    public string PriceText => Format.Money(Part.Price);
    public string DeltaText => Selected ? "In your build" : ScoreDelta == 0 ? "" : (ScoreDelta > 0 ? "+" : "−") + Math.Abs(ScoreDelta).ToString("N0") + " score";
}

public enum SortMode { Recommended, PriceLow, PriceHigh, Performance }

/// <summary>The build being edited: parts, undo, checks, estimates and the 3D scene message. Every change is saved to the garage.</summary>
public sealed class BuildSession : ObservableObject
{
    readonly PartCatalog _catalog;
    readonly Garage _garage;
    readonly Stack<PcBuild> _undo = new();
    readonly Stack<PcBuild> _redo = new();
    Category _selected = Category.Case;
    string _query = "";
    SortMode _sort = SortMode.Recommended;

    public BuildSession(PartCatalog catalog, Garage garage)
    {
        _catalog = catalog;
        _garage = garage;
        Build = garage.Current;
        Recompute();
        _selected = Resolved.Missing.FirstOrDefault(Category.Gpu);
    }

    public PartCatalog Catalog => _catalog;
    public Garage Garage => _garage;
    public PcBuild Build { get; private set; }
    public PcBuild.Resolved Resolved { get; private set; } = null!;
    public CheckReport Report { get; private set; } = null!;
    public BenchResult Bench { get; private set; } = null!;
    public IReadOnlyList<Slot> Slots { get; private set; } = [];
    public IReadOnlyList<Candidate> Candidates { get; private set; } = [];

    /// <summary>Raised after any change to the build (the page redraws, the scene gets a new message).</summary>
    public event Action? BuildChanged;

    public Category Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { _query = ""; OnPropertyChanged(nameof(Query)); RefreshCandidates(); } }
    }

    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value ?? "")) RefreshCandidates(); }
    }

    public SortMode Sort
    {
        get => _sort;
        set { if (SetProperty(ref _sort, value)) RefreshCandidates(); }
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string TotalText => Format.Money(Resolved.Total);
    public string ShareCode => Chassis.Core.Builds.ShareCode.Encode(Build);

    public void Choose(string partId)
    {
        var p = _catalog.Find(partId) ?? throw new ArgumentException($"Unknown part {partId}");
        if (Build[p.Category] == p.Id) return;
        Apply(Build.With(p.Category, p.Id));
        // Move on to the next empty slot, like a build guide.
        var missing = Resolved.Missing.ToList();
        if (missing.Count > 0) Selected = missing[0];
    }

    /// <summary>Puts several parts in at once (from an imported spec list) as one undo step.</summary>
    public void ApplyParts(IEnumerable<Part> parts)
    {
        var next = Build;
        var changed = false;
        foreach (var p in parts)
        {
            if (next[p.Category] == p.Id) continue;
            next = next.With(p.Category, p.Id);
            changed = true;
        }
        if (!changed) return;
        Apply(next);
        var missing = Resolved.Missing.ToList();
        if (missing.Count > 0) Selected = missing[0];
    }

    public void Remove(Category c)
    {
        if (Build[c] is null) return;
        Apply(Build.With(c, null));
    }

    public void Rename(string name)
    {
        if (name.Trim() == Build.Name) return;
        Build = Build.Renamed(name);
        _garage.Save(Build);
        OnPropertyChanged(nameof(Build));
    }

    public void Undo() { if (_undo.Count == 0) return; _redo.Push(Build); Set(_undo.Pop()); }
    public void Redo() { if (_redo.Count == 0) return; _undo.Push(Build); Set(_redo.Pop()); }

    /// <summary>Open a build from the garage (or a shared one). Undo history starts fresh.</summary>
    public void Open(PcBuild b)
    {
        _undo.Clear(); _redo.Clear();
        Set(b);
        Selected = Resolved.Missing.FirstOrDefault(Category.Gpu);
    }

    public PcBuild NewBuild(string name = "New build")
    {
        var b = new PcBuild { Name = name };
        Open(b);
        Selected = Category.Case;
        return b;
    }

    public bool Import(string code, out PcBuild build)
    {
        if (!Chassis.Core.Builds.ShareCode.TryDecode(code, _catalog, out build)) return false;
        build = build with { Id = Guid.NewGuid().ToString("N")[..10] };
        Open(build);
        return true;
    }

    void Apply(PcBuild next)
    {
        _undo.Push(Build);
        if (_undo.Count > 100) { var keep = _undo.Take(100).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        _redo.Clear();
        Set(next);
    }

    void Set(PcBuild b)
    {
        Build = b;
        _garage.Save(b);
        Recompute();
        Raise(BuildChanged);
    }

    /// <summary>Calls each listener on its own so one failing page can't stop the others (or the edit itself).</summary>
    public static Action<Exception>? ListenerFailed { get; set; }

    static void Raise(Action? handlers)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList().Cast<Action>())
        {
            try { h(); }
            catch (Exception e) { ListenerFailed?.Invoke(e); }
        }
    }

    void Recompute()
    {
        Resolved = Build.Resolve(_catalog);
        Report = CompatibilityChecker.Check(Resolved);
        Bench = Estimator.Run(Resolved);
        Slots = Categories.All.Select(c =>
        {
            var p = Resolved.Get(c);
            var state = p is null ? SlotState.Empty
                : Report.Findings.Any(f => f.Severity == Severity.Problem && f.Parts.Contains(c)) ? SlotState.Problem
                : Report.Findings.Any(f => f.Severity == Severity.Warning && f.Parts.Contains(c)) ? SlotState.Warning : SlotState.Ok;
            return new Slot(c, Categories.Label(c), p?.FullName ?? "Choose…", state, p?.Price ?? 0);
        }).ToList();
        RefreshCandidates();
        foreach (var n in new[] { nameof(Build), nameof(Resolved), nameof(Report), nameof(Bench), nameof(Slots), nameof(CanUndo), nameof(CanRedo), nameof(TotalText), nameof(ShareCode) })
            OnPropertyChanged(n);
    }

    void RefreshCandidates()
    {
        var baseScore = Bench.Score;
        var current = Build[_selected];
        var list = _catalog.Search(_selected, _query).Select(p =>
        {
            var trial = Build.With(_selected, p.Id).Resolve(_catalog);
            var problem = CompatibilityChecker.Check(trial).Findings
                .FirstOrDefault(f => f.Severity == Severity.Problem && f.Parts.Contains(_selected));
            var delta = p.Id == current ? 0 : Estimator.Run(trial).Score - baseScore;
            return new Candidate(p, p.Id == current, problem is null, problem?.Title, delta);
        });
        Candidates = (_sort switch
        {
            SortMode.PriceLow => list.OrderBy(c => c.Part.Price),
            SortMode.PriceHigh => list.OrderByDescending(c => c.Part.Price),
            SortMode.Performance => list.OrderByDescending(c => c.ScoreDelta),
            _ => list.OrderByDescending(c => c.Selected).ThenByDescending(c => c.Fits).ThenByDescending(c => c.Part.Price),
        }).ToList();
        OnPropertyChanged(nameof(Candidates));
    }

    /// <summary>The message the 3D scene draws from: each part's id, name and size facts, plus the categories with problems.</summary>
    public string SceneJson()
    {
        var o = new JsonObject { ["type"] = "build" };
        var problems = new JsonArray();
        foreach (var c in Categories.All)
            if (c != Category.Case && Report.Flags(c))
                problems.Add(c.ToString());
        o["problems"] = problems;
        foreach (var c in Categories.All)
        {
            if (Resolved.Get(c) is not { } p) continue;
            object? spec = c switch
            {
                Category.Case => p.Case, Category.Motherboard => p.Board, Category.Cpu => p.Cpu, Category.Cooler => p.Cooler,
                Category.Memory => p.Memory, Category.Gpu => p.Gpu, Category.Storage => p.Storage, Category.Psu => p.Psu, _ => null,
            };
            var node = spec is null ? new JsonObject() : JsonSerializer.SerializeToNode(spec, spec.GetType(), PartCatalog.Json)!.AsObject();
            node["id"] = p.Id;
            node["name"] = p.Name;
            node["brand"] = p.Brand;
            node["color"] = p.Color;
            node["facts"] = string.Join(" · ", p.Facts) + " · " + Format.Money(p.Price);
            o[SceneKey(c)] = node;
        }
        return o.ToJsonString();
    }

    public static string SceneKey(Category c) => c switch
    {
        Category.Case => "case", Category.Motherboard => "board", Category.Cpu => "cpu", Category.Cooler => "cooler",
        Category.Memory => "memory", Category.Gpu => "gpu", Category.Storage => "storage", _ => "psu",
    };
}

public static class Format
{
    public static string Money(decimal v) => "$" + v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public static string Int(double v) => Math.Round(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public static string Fps(double v) => v <= 0 ? "—" : $"{v:0} fps";
}
