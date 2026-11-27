using System.Text.Json;

namespace Chassis.Core.Catalog;

/// <summary>The built-in parts list (embedded parts.json).</summary>
public sealed class PartCatalog
{
    sealed record CatalogFile(int Version, string Notes, List<Part> Parts);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    readonly Dictionary<string, Part> _byId;

    public IReadOnlyList<Part> Parts { get; }
    public string Notes { get; }

    PartCatalog(CatalogFile file)
    {
        Parts = file.Parts;
        Notes = file.Notes;
        _byId = file.Parts.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    static PartCatalog? _default;
    public static PartCatalog Default => _default ??= LoadEmbedded();

    public static PartCatalog FromJson(string json) =>
        new(JsonSerializer.Deserialize<CatalogFile>(json, Json) ?? throw new InvalidDataException("Empty parts list."));

    static PartCatalog LoadEmbedded()
    {
        using var s = typeof(PartCatalog).Assembly.GetManifestResourceStream("Chassis.Core.parts.json")
            ?? throw new InvalidOperationException("parts.json is missing from Chassis.Core.");
        using var r = new StreamReader(s);
        return FromJson(r.ReadToEnd());
    }

    public Part? Find(string? id) => id is not null && _byId.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<Part> In(Category c) => Parts.Where(p => p.Category == c);

    /// <summary>Case-insensitive search across brand, name and facts.</summary>
    public IEnumerable<Part> Search(Category c, string? query)
    {
        var all = In(c);
        if (string.IsNullOrWhiteSpace(query)) return all;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(p =>
        {
            var hay = $"{p.Brand} {p.Name} {string.Join(' ', p.Facts)}";
            return words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
        });
    }
}
