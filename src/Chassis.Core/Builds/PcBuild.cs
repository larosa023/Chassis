using System.Text;
using Chassis.Core.Catalog;

namespace Chassis.Core.Builds;

/// <summary>A saved build: a name and one part id per category. Immutable; edits return a new build.</summary>
public sealed record PcBuild
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; init; } = "Untitled build";
    public Dictionary<Category, string> Parts { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? this[Category c] => Parts.TryGetValue(c, out var id) ? id : null;

    public PcBuild With(Category c, string? partId)
    {
        var parts = new Dictionary<Category, string>(Parts);
        if (partId is null) parts.Remove(c); else parts[c] = partId;
        return this with { Parts = parts, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public PcBuild Renamed(string name) => this with { Name = string.IsNullOrWhiteSpace(name) ? "Untitled build" : name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };

    public PcBuild Copy(string? name = null) => this with { Id = Guid.NewGuid().ToString("N")[..10], Name = name ?? Name + " (copy)", Parts = new(Parts), UpdatedAt = DateTimeOffset.UtcNow };

    public Resolved Resolve(PartCatalog catalog) => new(catalog, this);

    /// <summary>The build's parts looked up in the catalog. Unknown ids resolve to null.</summary>
    public sealed class Resolved(PartCatalog catalog, PcBuild build)
    {
        public PcBuild Build => build;
        public Part? Get(Category c) => catalog.Find(build[c]);
        public Part? Case => Get(Category.Case);
        public Part? Board => Get(Category.Motherboard);
        public Part? Cpu => Get(Category.Cpu);
        public Part? Cooler => Get(Category.Cooler);
        public Part? Memory => Get(Category.Memory);
        public Part? Gpu => Get(Category.Gpu);
        public Part? Storage => Get(Category.Storage);
        public Part? Psu => Get(Category.Psu);
        public IEnumerable<Part> All => Categories.All.Select(Get).OfType<Part>();
        public decimal Total => All.Sum(p => p.Price);
        public int Count => All.Count();
        public bool Complete => Count == Categories.All.Count;
        public IEnumerable<Category> Missing => Categories.All.Where(c => Get(c) is null);
    }
}

/// <summary>Share codes: a short text a friend can paste into Chassis to load the same build.</summary>
public static class ShareCode
{
    const string Prefix = "CHASSIS1-";

    public static string Encode(PcBuild b)
    {
        var body = b.Name.Replace("|", "/") + "|" + string.Join("|", Categories.All.Select(c => b[c] ?? ""));
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? code, PartCatalog catalog, out PcBuild build)
    {
        build = new PcBuild();
        if (string.IsNullOrWhiteSpace(code)) return false;
        var t = code.Trim();
        if (!t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var b64 = t[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        string body;
        try { body = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return false; }
        var fields = body.Split('|');
        if (fields.Length != Categories.All.Count + 1) return false;
        var parts = new Dictionary<Category, string>();
        for (var i = 0; i < Categories.All.Count; i++)
        {
            var id = fields[i + 1];
            if (id.Length == 0) continue;
            var p = catalog.Find(id);
            if (p is null || p.Category != Categories.All[i]) return false;
            parts[Categories.All[i]] = p.Id;
        }
        build = new PcBuild { Name = fields[0].Length > 0 ? fields[0] : "Shared build", Parts = parts };
        return true;
    }
}

/// <summary>Starter builds shown in the Garage and used for the first launch.</summary>
public static class Presets
{
    static PcBuild Make(string id, string name, params string[] parts)
    {
        var map = new Dictionary<Category, string>();
        foreach (var p in parts) map[PartCatalog.Default.Find(p)!.Category] = p;
        return new PcBuild { Id = id, Name = name, Parts = map };
    }

    public static IReadOnlyList<PcBuild> All { get; } =
    [
        Make("preset-flagship", "No-compromise 4K", "lianli-o11-evo", "asus-x870e-hero", "amd-9800x3d", "arctic-lf3-360", "corsair-v-64-6000", "nv-5090", "samsung-9100pro-2t", "seasonic-vertex-1200"),
        Make("preset-1440", "1440p sweet spot", "fractal-north", "msi-b650-tomahawk", "amd-9700x", "thermalright-pa120se", "gskill-z5-32-6000", "amd-9070xt", "wd-sn850x-2t", "corsair-rm850x"),
        Make("preset-budget", "Budget 1080p", "nzxt-h5-flow", "gb-b650m-elite", "amd-7600", "thermalright-pa120se", "kingston-fury-32-5600", "intel-b580", "crucial-p3plus-1t", "msi-a650bn"),
        Make("preset-sff", "Small but mighty", "cm-nr200p-v2", "asus-b650e-i", "amd-9800x3d", "lianli-gaII-280", "gskill-z5-32-6000", "nv-5070ti", "samsung-990pro-2t", "corsair-sf750"),
        Make("preset-creator", "Creator workstation", "hyte-y70", "asus-z890-e", "intel-285k", "corsair-h150i", "gskill-z5-96-6400", "nv-5080", "crucial-t705-2t", "corsair-rm1000x"),
    ];
}
