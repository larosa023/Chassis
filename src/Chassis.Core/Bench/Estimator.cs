using Chassis.Core.Builds;
using Chassis.Core.Catalog;
using Chassis.Core.Checks;

namespace Chassis.Core.Bench;

public enum Resolution { P1080, P1440, P2160 }

/// <summary>A kind of game, with how hard it leans on the GPU and the CPU. Numbers are for a reference card (raster index 100) at 4K.</summary>
public sealed record GameProfile(string Key, string Name, string Blurb, double GpuFps4k, double CpuFpsPerPoint, bool RayTraced, int VramNeedGb4k);

public sealed record FpsResult(GameProfile Game, Resolution Res, double Fps, double GpuFps, double CpuFps)
{
    /// <summary>Which part holds the frame rate back, and by how much (0..1).</summary>
    public (string Limiter, double Amount) Bottleneck =>
        GpuFps <= CpuFps ? ("GPU", 1 - GpuFps / CpuFps) : ("CPU", 1 - CpuFps / GpuFps);
}

public sealed record BenchResult
{
    public required int CpuMulti { get; init; }
    public required int CpuSingle { get; init; }
    public required bool CpuThrottles { get; init; }
    public required IReadOnlyList<FpsResult> Games { get; init; }
    public required double MemoryBandwidthGBs { get; init; }
    public required double MemoryLatencyNs { get; init; }
    public required int StorageReadMBs { get; init; }
    public required int StorageWriteMBs { get; init; }
    public required double LevelLoadSeconds { get; init; }
    public required int PeakWatts { get; init; }
    public required int GamingWatts { get; init; }
    public required double PsuLoad { get; init; }
    public required int CpuTempC { get; init; }
    public required int GpuTempC { get; init; }
    public required int NoiseDb { get; init; }
    public required int Score { get; init; }
    public required string Tier { get; init; }
    public required string Verdict { get; init; }
    public required decimal Price { get; init; }

    public FpsResult Fps(string gameKey, Resolution r) => Games.First(g => g.Game.Key == gameKey && g.Res == r);
    public double DollarsPerFrame => Fps("aaa", Resolution.P1440).Fps is > 0 and var f ? (double)Price / f : 0;
}

public static class Estimator
{
    public static IReadOnlyList<GameProfile> Games { get; } =
    [
        new("esports", "Competitive shooter", "Light graphics, very high frame rates. CPU matters most.", 420, 5.0, false, 4),
        new("br", "Battle royale", "Big maps, lots of players. Balanced load.", 140, 2.6, false, 8),
        new("aaa", "Open-world AAA", "Modern big-budget game at high settings.", 72, 1.9, false, 12),
        new("rt", "Ray-traced showcase", "Full ray tracing, no upscaling. GPU matters most.", 45, 1.6, true, 14),
    ];

    public static IReadOnlyList<Resolution> Resolutions { get; } = [Resolution.P1080, Resolution.P1440, Resolution.P2160];

    public static string Label(Resolution r) => r switch { Resolution.P1080 => "1080p", Resolution.P1440 => "1440p", _ => "4K" };

    static double ResScale(Resolution r) => r switch { Resolution.P1080 => 2.2, Resolution.P1440 => 1.65, _ => 1.0 };

    /// <summary>Soft minimum: the slower part sets the pace, and the faster one still helps a little.</summary>
    static double SoftMin(double a, double b, double k = 4) => a <= 0 || b <= 0 ? 0 : Math.Pow(Math.Pow(a, -k) + Math.Pow(b, -k), -1 / k);

    static double MemoryGameFactor(MemorySpec? m) => m switch
    {
        null => 0.9,
        { Type: "DDR4" } => 0.9 + Math.Min(0.04, (m.Speed - 3200) / 20000.0),
        _ => Math.Clamp(0.96 + (m.Speed - 5200) / 20000.0 - Math.Max(0, m.Cl - 30) / 400.0, 0.9, 1.02),
    };

    public static double CpuThermalFactor(CpuSpec cpu, CoolerSpec? cooler)
    {
        if (cooler is null) return 0.6;
        var ratio = (double)cooler.CapacityW / cpu.PowerW;
        return ratio >= 1 ? 1 : Math.Max(0.6, 1 - 0.6 * (1 - ratio));
    }

    public static BenchResult Run(PcBuild.Resolved b)
    {
        var cpu = b.Cpu?.Cpu;
        var gpu = b.Gpu?.Gpu;
        var mem = b.Memory?.Memory;
        var ssd = b.Storage?.Storage;
        var board = b.Board?.Board;
        var cooler = b.Cooler?.Cooler;

        var thermal = cpu is null ? 1 : CpuThermalFactor(cpu, cooler);
        var memMulti = mem?.Type == "DDR4" ? 0.97 : 1.0;
        var multi = cpu is null ? 0 : (int)Math.Round(cpu.Multi * thermal * memMulti);
        var single = cpu is null ? 0 : (int)Math.Round(cpu.Single * (thermal < 0.8 ? 0.97 : 1.0));

        var games = new List<FpsResult>();
        foreach (var g in Games)
        foreach (var r in Resolutions)
        {
            double gpuFps = 0, cpuFps = 0;
            if (gpu is not null)
            {
                var idx = g.RayTraced ? gpu.Rt : gpu.Raster;
                gpuFps = g.GpuFps4k * idx / 100.0 * ResScale(r);
                var need = r == Resolution.P2160 ? g.VramNeedGb4k : r == Resolution.P1440 ? g.VramNeedGb4k - 2 : g.VramNeedGb4k - 4;
                if (gpu.VramGb < need) gpuFps *= Math.Max(0.6, 1 - 0.1 * (need - gpu.VramGb));
            }
            if (cpu is not null)
                cpuFps = cpu.Game * g.CpuFpsPerPoint * MemoryGameFactor(mem) * (thermal < 0.8 ? 0.95 : 1);
            games.Add(new FpsResult(g, r, Math.Round(SoftMin(gpuFps, cpuFps)), gpuFps, cpuFps));
        }

        double bw = 0, lat = 0;
        if (mem is not null)
        {
            var channels = Math.Min(2, mem.Modules);
            bw = Math.Round(mem.Speed * 8.0 * channels / 1000, 1);
            lat = Math.Round(mem.Cl * 2000.0 / mem.Speed, 1);
        }

        int read = 0, write = 0;
        if (ssd is not null)
        {
            var cap = ssd.Iface == "NVMe5" && board is { Pcie5M2: false } ? 7000 : int.MaxValue;
            read = Math.Min(ssd.ReadMBs, cap);
            write = Math.Min(ssd.WriteMBs, cap);
        }
        var load = read > 0 ? Math.Round(2.5 + 4000.0 / read + (cpu is null ? 0 : 20000.0 / Math.Max(1, cpu.Multi)), 1) : 0;

        var peak = CompatibilityChecker.PeakWatts(b);
        var gaming = (int)Math.Round((cpu?.PowerW ?? 0) * 0.55 + (gpu?.PowerW ?? 0) * 0.9 + (b.Count > 0 ? 50 : 0));
        var psuLoad = b.Psu?.Psu is { } p && p.Watts > 0 ? (double)peak / p.Watts : 0;

        var cpuTemp = cpu is null ? 0 : (int)Math.Round(Math.Min(100, 28 + 60.0 * cpu.PowerW / Math.Max(60, cooler?.CapacityW ?? 60)));
        var gpuTemp = gpu is null ? 0 : (int)Math.Round(58 + gpu.PowerW / 30.0 + (b.Case?.Case is { Boards: ["ITX"] } ? 6 : 0));
        var noise = (int)Math.Round(Math.Max(cooler?.NoiseDb ?? 0, gpu is null ? 0 : 22 + gpu.PowerW / 40.0) + (cpuTemp > 90 ? 4 : 0));

        var aaa1440 = games.First(x => x.Game.Key == "aaa" && x.Res == Resolution.P1440).Fps;
        var score = (int)Math.Round(aaa1440 * 55 + multi * 0.12 + single * 0.8);

        var tier = score switch
        {
            >= 11000 => "Halo",
            >= 8500 => "Enthusiast",
            >= 6500 => "High-end",
            >= 4500 => "Mainstream",
            > 0 => "Entry",
            _ => "Incomplete",
        };

        return new BenchResult
        {
            CpuMulti = multi, CpuSingle = single, CpuThrottles = thermal < 1, Games = games,
            MemoryBandwidthGBs = bw, MemoryLatencyNs = lat, StorageReadMBs = read, StorageWriteMBs = write, LevelLoadSeconds = load,
            PeakWatts = peak, GamingWatts = gaming, PsuLoad = Math.Round(psuLoad, 2),
            CpuTempC = cpuTemp, GpuTempC = gpuTemp, NoiseDb = noise, Score = score, Tier = tier,
            Verdict = Verdict(b, games), Price = b.Total,
        };
    }

    static string Verdict(PcBuild.Resolved b, List<FpsResult> games)
    {
        if (b.Cpu is null || b.Gpu is null) return "Add a processor and a graphics card to see how it games.";
        var best = Resolutions.Reverse().FirstOrDefault(r => games.First(x => x.Game.Key == "aaa" && x.Res == r).Fps >= 60, Resolution.P1080);
        var at = games.First(x => x.Game.Key == "aaa" && x.Res == best);
        var fps = at.Fps;
        var (who, amt) = games.First(x => x.Game.Key == "aaa" && x.Res == Resolution.P1440).Bottleneck;
        var limit = amt < 0.12 ? "The CPU and GPU are well matched." : who == "CPU"
            ? "At 1440p the processor holds the graphics card back; a faster CPU would add frames."
            : "At 1440p the graphics card sets the pace, which is what you want.";
        return fps >= 60
            ? $"Built for {Label(best)}: about {fps:0} fps in a demanding open-world game. {limit}"
            : $"Best at 1080p with lower settings: about {fps:0} fps in a demanding open-world game. {limit}";
    }
}
