using System.Globalization;
using System.Text.RegularExpressions;
using Chassis.Core.Catalog;

namespace Chassis.Core.Import;

public enum MatchStrength { Guess, Likely, Exact }

/// <summary>One part found in a spec list, with the text it was found in.</summary>
public sealed record SpecMatch(Category Category, Part Part, MatchStrength Strength, string Evidence, double Score);

public sealed record ImportResult(IReadOnlyList<SpecMatch> Matches, string Text)
{
    public IEnumerable<Category> NotFound => Categories.All.Where(c => Matches.All(m => m.Category != c));
    public SpecMatch? For(Category c) => Matches.FirstOrDefault(m => m.Category == c);
}

/// <summary>
/// Reads a spec list (text from a screenshot, a store page, PCPartPicker, Task Manager, or pasted text)
/// and finds the matching parts in the catalog.
/// Each part name is split into tokens: model numbers and qualifiers (Ti, XT, X3D, Pro, EVO…) must all appear;
/// series words (Tomahawk, Trident, Dynamic…) must mostly appear; brand and partner words only add confidence.
/// </summary>
public static partial class SpecMatcher
{
    static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "gaming", "wifi", "wifi7", "wi", "fi", "edition", "the", "and", "with", "rgb", "oc", "pc", "case", "cooler", "cpu", "memory", "ram",
        "processor", "graphics", "card", "video", "motherboard", "power", "supply", "drive", "ssd", "nvme", "founders", "limited",
        "geforce", "radeon", "nvidia", "amd", "intel", "for", "atx", "matx", "itx", "mid", "tower", "lcd", "tm", "w", "gb", "tb",
        "used", "low", "profile", "micro", "mini-itx", "pcie", "sata", "hard",
    };

    static readonly HashSet<string> Qualifiers = new(StringComparer.Ordinal)
    {
        "ti", "super", "xt", "xtx", "gre", "se", "pro", "plus", "max", "evo", "xl", "mini", "elite", "ultra", "lite", "neo", "air", "flow",
        "rs", "extreme", "hero", "apex", "dark", "vision", "shift", "compact",
    };

    static readonly HashSet<string> IntelFamily = new(StringComparer.Ordinal) { "core", "i3", "i5", "i7", "i9", "ultra" };

    [GeneratedRegex(@"\(([^)]*)\)")] private static partial Regex Parens();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(tb|gb)\b")] private static partial Regex Units();
    [GeneratedRegex(@"[^a-z0-9\s]")] private static partial Regex Punct();
    [GeneratedRegex(@"\(\s*\d+\s*x\s*\d+\s*gb\s*\)")] private static partial Regex KitBreakdown();
    [GeneratedRegex(@"^\d+(gb|tb)$")] private static partial Regex Capacity();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"ddr([45])[\s-]*(\d{4})")] private static partial Regex DdrSpeed();
    [GeneratedRegex(@"(\d{4})\s*(mt\s*s|mts|mhz)")] private static partial Regex SpeedUnit();
    [GeneratedRegex(@"\b(\d{1,3})gb\b")] private static partial Regex CapGb();
    [GeneratedRegex(@"^[a-z]?\d{3,5}[a-z0-9]*$")] private static partial Regex ModelNumber();

    /// <summary>Lowercases, merges sizes ("2 TB" → "2tb", "64.0 GB" → "64gb") and turns punctuation into spaces.</summary>
    public static string Normalize(string s)
    {
        var t = s.ToLowerInvariant().Replace('×', 'x').Replace("™", "").Replace("®", "").Replace("©", "");
        t = Units().Replace(t, m => ((int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))).ToString(CultureInfo.InvariantCulture) + m.Groups[2].Value);
        t = KitBreakdown().Replace(t, " ");   // "32 GB (2 x 16 GB)": the kit size is what matters
        t = Punct().Replace(t, " ");
        return Spaces().Replace(t, " ").Trim();
    }

    static string[] Tokens(string normalized) => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    sealed record Window(string Raw, HashSet<string> Set, string Squashed, HashSet<string> Folded);

    /// <summary>Folds letters that text recognition confuses with digits (O/0, l/1, I/1) in tokens that contain a digit.</summary>
    static string Fold(string t) => t.Any(char.IsDigit) ? t.Replace('o', '0').Replace('l', '1').Replace('i', '1') : t;

    sealed record Pattern(Part Part, string[] Required, string[] Words, string[] Bonus, string[] Brand, bool ModelMode);

    static readonly Dictionary<PartCatalog, List<Pattern>> Cache = new();

    static List<Pattern> Patterns(PartCatalog catalog)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(catalog, out var list)) return list;
            list = catalog.Parts.Select(Build).ToList();
            Cache[catalog] = list;
            return list;
        }
    }

    static Pattern Build(Part p)
    {
        var brand = Tokens(Normalize(p.Brand)).Where(t => t.Length >= 2).ToArray();
        var name = Parens().Replace(p.Name, "");   // "(2×16)", "(PCIe 5.0)", "(2024)" are noise for matching
        var toks = Tokens(Normalize(name)).Where(t => t.Length >= 2 || char.IsDigit(t[0])).Distinct().ToList();

        if (p.Category is Category.Cpu or Category.Gpu)
        {
            // Model mode: family word + model number (+ qualifiers right after it) are required; everything else is a bonus.
            var req = new List<string>();
            var modelIdx = toks.FindIndex(t => ModelNumber().IsMatch(t) && t.Count(char.IsDigit) >= 3);
            if (modelIdx >= 0)
            {
                req.Add(toks[modelIdx]);
                for (var i = modelIdx + 1; i < toks.Count && Qualifiers.Contains(toks[i]); i++) req.Add(toks[i]);
            }
            foreach (var fam in new[] { "ryzen", "rtx", "rx", "arc" }) if (toks.Contains(fam)) req.Add(fam);
            var bonus = toks.Where(t => !req.Contains(t) && !Stop.Contains(t) && t.Length >= 2).ToArray();
            return new Pattern(p, req.ToArray(), [], bonus, brand, ModelMode: true);
        }

        var required = new List<string>();
        var words = new List<string>();
        var bonusWords = new List<string>();
        foreach (var t in toks)
        {
            if (Stop.Contains(t) || t.Length < 2) continue;
            if (p.Category == Category.Memory && t.StartsWith("cl", StringComparison.Ordinal) && t.Length > 2 && char.IsDigit(t[2])) { bonusWords.Add(t); continue; }
            if (t.Any(char.IsDigit) || Qualifiers.Contains(t)) required.Add(t);
            else words.Add(t);
        }
        return new Pattern(p, required.ToArray(), words.ToArray(), bonusWords.ToArray(), brand, ModelMode: false);
    }

    static bool Has(Window w, string token) =>
        w.Set.Contains(token) || w.Folded.Contains(Fold(token)) ||
        (token.Length >= 3 && !Capacity().IsMatch(token) && w.Squashed.Contains(token, StringComparison.Ordinal));

    static double? Score(Pattern p, Window w)
    {
        if (p.Required.Length == 0 && p.Words.Length == 0) return null;
        foreach (var r in p.Required) if (!Has(w, r)) return null;
        double score = p.Required.Sum(r => r.Length + 2.0);
        var brandHit = p.Brand.Any(b => Has(w, b));

        if (p.ModelMode)
        {
            if (p.Part.Category == Category.Cpu)
            {
                var family = p.Part.Brand == "Intel" ? IntelFamily.Any(f => w.Set.Contains(f)) : w.Set.Contains("ryzen");
                if (!family) return null;
            }
            if (p.Required.Length == 0) return null;
            foreach (var b in p.Bonus) score += Has(w, b) ? (b.EndsWith("gb", StringComparison.Ordinal) ? 3 : 0.6) : -0.15;
        }
        else
        {
            if (p.Words.Length > 0)
            {
                var hit = p.Words.Count(x => Has(w, x));
                if (hit < Math.Ceiling(p.Words.Length * 0.6)) return null;
                score += p.Words.Where(x => Has(w, x)).Sum(x => x.Length) - (p.Words.Length - hit) * 1.5;
            }
            // A part named by one plain word ("North") needs its brand to be sure.
            if (p.Required.Length == 0 && p.Words.Length == 1 && !brandHit) return null;
            foreach (var b in p.Bonus) score += Has(w, b) ? 1 : 0;
        }
        if (brandHit) score += 1.5;
        return score;
    }

    static List<Window> Windows(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var result = new List<Window>();
        for (var i = 0; i < lines.Count; i++)
        {
            result.Add(Make(lines[i]));
            if (i + 1 < lines.Count) result.Add(Make(lines[i] + " " + lines[i + 1]));
        }
        return result;

        static Window Make(string raw)
        {
            var n = Normalize(raw);
            var toks = Tokens(n);
            return new Window(raw, new HashSet<string>(toks, StringComparer.Ordinal), n.Replace(" ", "", StringComparison.Ordinal),
                new HashSet<string>(toks.Select(Fold), StringComparer.Ordinal));
        }
    }

    public static ImportResult Match(string text, PartCatalog catalog)
    {
        var windows = Windows(text ?? "");
        var matches = new List<SpecMatch>();
        foreach (var cat in Categories.All)
        {
            SpecMatch? best = null;
            foreach (var p in Patterns(catalog).Where(x => x.Part.Category == cat))
            foreach (var w in windows)
            {
                if (Score(p, w) is not { } s) continue;
                // Prefer the higher score; on a tie prefer the single line (shorter evidence), then the standard
                // (usually cheapest) model, e.g. the Founders Edition when the list just says "RTX 5080".
                if (best is null || s > best.Score + 1e-9 || (Math.Abs(s - best.Score) < 1e-9 && (w.Raw.Length < best.Evidence.Length ||
                    (w.Raw.Length == best.Evidence.Length && p.Part.Price < best.Part.Price))))
                {
                    var allWords = p.Words.All(x => Has(w, x)) && (!p.ModelMode || p.Brand.Any(b => Has(w, b)) || p.Bonus.Length == 0);
                    best = new SpecMatch(cat, p.Part, allWords ? MatchStrength.Exact : MatchStrength.Likely, w.Raw, s);
                }
            }
            if (best is null && cat == Category.Memory) best = GuessMemory(windows, catalog);
            if (best is not null) matches.Add(best);
        }
        return new ImportResult(matches, text ?? "");
    }

    /// <summary>When no kit is named, read type, size and speed ("Memory 64.0 GB · 6000 MT/s · DDR5") and pick the closest kit.</summary>
    static SpecMatch? GuessMemory(List<Window> windows, PartCatalog catalog)
    {
        string? type = null; int? speed = null; int? cap = null; string evidence = "";
        foreach (var w in windows)
        {
            var n = Normalize(w.Raw);
            var ddr = DdrSpeed().Match(n);
            if (ddr.Success) { type ??= "DDR" + ddr.Groups[1].Value; speed ??= int.Parse(ddr.Groups[2].Value, CultureInfo.InvariantCulture); evidence = w.Raw; }
            if (type is null && (w.Set.Contains("ddr5") || w.Set.Contains("ddr4"))) { type = w.Set.Contains("ddr5") ? "DDR5" : "DDR4"; evidence = evidence.Length > 0 ? evidence : w.Raw; }
            var su = SpeedUnit().Match(n);
            if (speed is null && su.Success) speed = int.Parse(su.Groups[1].Value, CultureInfo.InvariantCulture);
            var isMemoryLine = w.Set.Contains("memory") || w.Set.Contains("ram") || w.Set.Contains("ddr5") || w.Set.Contains("ddr4") || w.Set.Contains("installed");
            if (cap is null && isMemoryLine && !w.Set.Contains("vram") && !w.Set.Contains("dedicated"))
            {
                var c = CapGb().Match(n);
                if (c.Success && int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture) is var v && v is 8 or 16 or 24 or 32 or 48 or 64 or 96 or 128 or 192 or 256)
                { cap = v; if (evidence.Length == 0) evidence = w.Raw; }
            }
        }
        if (type is null && speed is int sp) type = sp >= 4400 ? "DDR5" : "DDR4";
        if (type is null || cap is null) return null;
        var kit = catalog.In(Category.Memory).Where(p => p.Memory!.Type == type)
            .OrderBy(p => Math.Abs(p.Memory!.CapacityGb - cap.Value))
            .ThenBy(p => speed is int s ? Math.Abs(p.Memory!.Speed - s) : 0)
            .ThenBy(p => p.Memory!.Cl).ThenBy(p => p.Price).FirstOrDefault();
        return kit is null ? null : new SpecMatch(Category.Memory, kit, MatchStrength.Guess, evidence, 0);
    }
}
