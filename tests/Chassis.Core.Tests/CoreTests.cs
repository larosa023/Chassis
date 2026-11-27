using Chassis.Core.Bench;
using Chassis.Core.Builds;
using Chassis.Core.Catalog;
using Chassis.Core.Checks;
using Chassis.Core.Storage;
using Xunit;

namespace Chassis.Core.Tests;

public class CatalogTests
{
    readonly PartCatalog _c = PartCatalog.Default;

    [Fact]
    public void Every_category_has_parts_and_each_part_has_its_spec()
    {
        foreach (var cat in Categories.All) Assert.True(_c.In(cat).Count() >= 5, $"{cat} needs at least 5 parts");
        foreach (var p in _c.Parts)
        {
            object? spec = p.Category switch
            {
                Category.Case => p.Case, Category.Motherboard => p.Board, Category.Cpu => p.Cpu, Category.Cooler => p.Cooler,
                Category.Memory => p.Memory, Category.Gpu => p.Gpu, Category.Storage => p.Storage, Category.Psu => p.Psu, _ => null,
            };
            Assert.True(spec is not null, $"{p.Id} is missing its {p.Category} spec");
            Assert.True(p.Price > 0, $"{p.Id} has no price");
            Assert.NotEmpty(p.Facts);
        }
    }

    [Fact]
    public void Ids_are_unique()
    {
        Assert.Equal(_c.Parts.Count, _c.Parts.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Search_matches_all_words()
    {
        var hits = _c.Search(Category.Gpu, "rtx founders").ToList();
        Assert.True(hits.Count >= 4);
        Assert.All(hits, h => Assert.Contains("Founders", h.Name));
        Assert.All(hits, h => Assert.Contains("RTX", h.Name));
        Assert.Empty(_c.Search(Category.Gpu, "rtx zzz"));
    }
}

public class CheckTests
{
    static PcBuild.Resolved R(PcBuild b) => b.Resolve(PartCatalog.Default);

    [Fact]
    public void All_presets_have_no_problems()
    {
        foreach (var p in Presets.All)
        {
            var report = CompatibilityChecker.Check(R(p));
            Assert.True(report.Ok, $"{p.Name}: {string.Join("; ", report.Findings.Where(f => f.Severity == Severity.Problem).Select(f => f.Title))}");
            Assert.True(R(p).Complete, p.Name);
        }
    }

    [Fact]
    public void Wrong_socket_is_a_problem()
    {
        var b = Presets.All[1].With(Category.Cpu, "intel-14900k");
        var r = CompatibilityChecker.Check(R(b));
        Assert.Contains(r.Findings, f => f.Severity == Severity.Problem && f.Title.Contains("doesn't fit the motherboard"));
        Assert.True(r.Flags(Category.Cpu));
    }

    [Fact]
    public void Ddr4_in_ddr5_board_is_a_problem()
    {
        var r = CompatibilityChecker.Check(R(Presets.All[1].With(Category.Memory, "corsair-lpx-32-3200")));
        Assert.Contains(r.Findings, f => f.Severity == Severity.Problem && f.Title == "Wrong type of memory");
    }

    [Fact]
    public void Big_parts_in_small_case_are_problems()
    {
        var b = Presets.All[3].With(Category.Gpu, "nv-5090").With(Category.Cooler, "noctua-nh-d15-g2").With(Category.Motherboard, "msi-x870-tomahawk").With(Category.Psu, "corsair-rm850x");
        var titles = CompatibilityChecker.Check(R(b)).Findings.Where(f => f.Severity == Severity.Problem).Select(f => f.Title).ToList();
        Assert.Contains("Motherboard is too big for the case", titles);
        Assert.Contains("Cooler is too tall", titles);
        Assert.Contains("Power supply doesn't fit the case", titles);
    }

    [Fact]
    public void Weak_psu_is_flagged()
    {
        var r = CompatibilityChecker.Check(R(Presets.All[0].With(Category.Psu, "msi-a650bn")));
        Assert.Contains(r.Findings, f => f.Severity == Severity.Problem && f.Title == "Power supply is too weak");
    }

    [Fact]
    public void Gen5_ssd_on_gen4_board_warns_and_is_capped()
    {
        var b = Presets.All[1].With(Category.Storage, "crucial-t705-2t");
        Assert.Contains(CompatibilityChecker.Check(R(b)).Findings, f => f.Title == "SSD will run at half speed");
        Assert.Equal(7000, Estimator.Run(R(b)).StorageReadMBs);
    }

    [Fact]
    public void Empty_build_says_start_with_a_case()
    {
        var r = CompatibilityChecker.Check(R(new PcBuild()));
        Assert.Equal("Start with a case", Assert.Single(r.Findings).Title);
    }

    [Fact]
    public void No_igpu_without_gpu_is_a_problem()
    {
        var b = new PcBuild().With(Category.Motherboard, "msi-z790-tomahawk").With(Category.Cpu, "intel-13400f");
        Assert.Contains(CompatibilityChecker.Check(R(b)).Findings, f => f.Title == "No graphics output");
    }
}

public class BenchTests
{
    static BenchResult Run(PcBuild b) => Estimator.Run(b.Resolve(PartCatalog.Default));

    [Fact]
    public void Faster_gpu_gives_more_fps_at_4k()
    {
        var a = Run(Presets.All[1]);
        var b = Run(Presets.All[1].With(Category.Gpu, "nv-5090"));
        Assert.True(b.Fps("aaa", Resolution.P2160).Fps > a.Fps("aaa", Resolution.P2160).Fps * 1.5);
    }

    [Fact]
    public void Fps_goes_down_as_resolution_goes_up()
    {
        var r = Run(Presets.All[1]);
        foreach (var g in Estimator.Games)
            Assert.True(r.Fps(g.Key, Resolution.P1080).Fps >= r.Fps(g.Key, Resolution.P1440).Fps && r.Fps(g.Key, Resolution.P1440).Fps >= r.Fps(g.Key, Resolution.P2160).Fps, g.Key);
    }

    [Fact]
    public void Esports_at_1080_is_cpu_limited_on_a_big_gpu()
    {
        var r = Run(Presets.All[0]);
        Assert.Equal("CPU", r.Fps("esports", Resolution.P1080).Bottleneck.Limiter);
        Assert.Equal("GPU", r.Fps("rt", Resolution.P2160).Bottleneck.Limiter);
    }

    [Fact]
    public void Undersized_cooler_lowers_multicore_score()
    {
        var good = Run(Presets.All[4]);
        var bad = Run(Presets.All[4].With(Category.Cooler, "noctua-nh-l12s"));
        Assert.True(bad.CpuMulti < good.CpuMulti * 0.8);
        Assert.True(bad.CpuThrottles);
        Assert.True(bad.CpuTempC > good.CpuTempC);
    }

    [Fact]
    public void Scores_rank_presets_sensibly()
    {
        var flagship = Run(Presets.All[0]).Score;
        var mid = Run(Presets.All[1]).Score;
        var budget = Run(Presets.All[2]).Score;
        Assert.True(flagship > mid && mid > budget, $"{flagship} {mid} {budget}");
        Assert.InRange(Run(Presets.All[0]).Fps("aaa", Resolution.P1440).Fps, 110, 200);
        Assert.InRange(Run(Presets.All[2]).Fps("aaa", Resolution.P1080).Fps, 35, 90);
    }

    [Fact]
    public void Memory_bandwidth_is_dual_channel()
    {
        Assert.Equal(96.0, Run(Presets.All[1]).MemoryBandwidthGBs);
    }

    [Fact]
    public void Empty_build_runs_without_errors()
    {
        var r = Run(new PcBuild());
        Assert.Equal(0, r.Score);
        Assert.Equal("Incomplete", r.Tier);
    }
}

public class BuildTests
{
    [Fact]
    public void Share_code_round_trips()
    {
        var b = Presets.All[3].Renamed("Anthony's | ITX");
        var code = ShareCode.Encode(b);
        Assert.StartsWith("CHASSIS1-", code);
        Assert.True(ShareCode.TryDecode(code, PartCatalog.Default, out var back));
        Assert.Equal("Anthony's / ITX", back.Name);
        foreach (var c in Categories.All) Assert.Equal(b[c], back[c]);
    }

    [Fact]
    public void Bad_share_codes_are_rejected()
    {
        Assert.False(ShareCode.TryDecode("hello", PartCatalog.Default, out _));
        Assert.False(ShareCode.TryDecode("CHASSIS1-!!!", PartCatalog.Default, out _));
        Assert.False(ShareCode.TryDecode("", PartCatalog.Default, out _));
    }

    [Fact]
    public void Garage_saves_and_reloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chassis-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "garage.json");
        try
        {
            var g = new Garage(path);
            Assert.Equal(Presets.All.Count, g.Builds.Count);
            var mine = new PcBuild { Name = "Mine" }.With(Category.Gpu, "nv-5080");
            g.Save(mine);
            g.Update(s => s with { Theme = "circuit" });
            var g2 = new Garage(path);
            Assert.Equal("Mine", g2.Current.Name);
            Assert.Equal("nv-5080", g2.Current[Category.Gpu]);
            Assert.Equal("circuit", g2.Settings.Theme);
            Assert.True(g2.Delete(mine.Id));
            Assert.Null(new Garage(path).Find(mine.Id));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Broken_garage_file_starts_fresh_and_keeps_a_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chassis-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "garage.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var g = new Garage(path);
            Assert.Equal(Presets.All.Count, g.Builds.Count);
            Assert.True(File.Exists(path + ".broken"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

