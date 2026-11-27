using System.Text.Json.Serialization;

namespace Chassis.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<Category>))]
public enum Category { Case, Motherboard, Cpu, Cooler, Memory, Gpu, Storage, Psu }

public static class Categories
{
    /// <summary>Build order: the order parts go into a real case.</summary>
    public static IReadOnlyList<Category> All { get; } =
        [Category.Case, Category.Motherboard, Category.Cpu, Category.Cooler, Category.Memory, Category.Gpu, Category.Storage, Category.Psu];

    public static string Label(Category c) => c switch
    {
        Category.Case => "Case",
        Category.Motherboard => "Motherboard",
        Category.Cpu => "Processor",
        Category.Cooler => "CPU cooler",
        Category.Memory => "Memory",
        Category.Gpu => "Graphics card",
        Category.Storage => "Storage",
        Category.Psu => "Power supply",
        _ => c.ToString(),
    };

    public static string Short(Category c) => c switch
    {
        Category.Cpu => "CPU",
        Category.Gpu => "GPU",
        Category.Psu => "PSU",
        Category.Memory => "RAM",
        Category.Motherboard => "Board",
        _ => c.ToString(),
    };
}

public sealed record CaseSpec(string[] Boards, string[] Psus, int MaxGpuMm, int MaxCoolerMm, int MaxRadMm, int DepthMm, int HeightMm, int WidthMm, int Fans,
    string Type = "Mid tower", string Panel = "Tempered glass");
public sealed record BoardSpec(string Socket, string Chipset, string Form, string Memory, int MemorySlots, int M2Slots, bool Pcie5M2, bool Wifi,
    int MaxMemoryGb = 128, string GpuPcie = "4.0");
public sealed record CpuSpec(string Socket, int Cores, int Threads, double BoostGhz, int PowerW, int Multi, int Single, int Game, bool Igpu,
    double BaseGhz = 0, int L3Mb = 0, int TdpW = 0, string Arch = "");
public sealed record CoolerSpec(string Kind, int HeightMm, int RadMm, int CapacityW, int NoiseDb, int Fans = 1, bool Rgb = false)
{
    public bool IsLiquid => Kind == "Liquid";
}
public sealed record MemorySpec(string Type, int CapacityGb, int Speed, int Cl, int Modules, bool Rgb = false, string Profile = "");
public sealed record GpuSpec(int LengthMm, double Slots, int PowerW, int Raster, int Rt, int VramGb, string Connector, string MemType = "", int BoostMhz = 0);
public sealed record StorageSpec(string Iface, int CapacityGb, int ReadMBs, int WriteMBs, bool Dram = false, int Tbw = 0, string FormFactor = "M.2 2280");
public sealed record PsuSpec(int Watts, string Form, string Rating, bool Native12v2x6, string Modular = "", bool Atx31 = false);

public sealed record Part
{
    public required string Id { get; init; }
    [JsonPropertyName("cat")] public required Category Category { get; init; }
    public required string Brand { get; init; }
    public required string Name { get; init; }
    public decimal Price { get; init; }
    public string Color { get; init; } = "#222222";
    public CaseSpec? Case { get; init; }
    public BoardSpec? Board { get; init; }
    public CpuSpec? Cpu { get; init; }
    public CoolerSpec? Cooler { get; init; }
    public MemorySpec? Memory { get; init; }
    public GpuSpec? Gpu { get; init; }
    public StorageSpec? Storage { get; init; }
    public PsuSpec? Psu { get; init; }

    public string FullName => $"{Brand} {Name}";

    /// <summary>Two to four short facts for a part card.</summary>
    public IReadOnlyList<string> Facts => Category switch
    {
        Category.Case when Case is { } c => [string.Join(" / ", c.Boards), $"GPU ≤ {c.MaxGpuMm} mm", $"Cooler ≤ {c.MaxCoolerMm} mm"],
        Category.Motherboard when Board is { } b => [b.Socket, b.Form, b.Memory, b.Pcie5M2 ? "PCIe 5.0 SSD" : "PCIe 4.0 SSD"],
        Category.Cpu when Cpu is { } c => [$"{c.Cores} cores / {c.Threads} threads", $"{c.BoostGhz:0.0} GHz", $"{c.PowerW} W", c.Socket],
        Category.Cooler when Cooler is { } c => c.IsLiquid ? [$"{c.RadMm} mm radiator", $"Handles ~{c.CapacityW} W"] : [$"{c.HeightMm} mm tall", $"Handles ~{c.CapacityW} W"],
        Category.Memory when Memory is { } m => [$"{m.CapacityGb} GB", $"{m.Type}-{m.Speed}", $"CL{m.Cl}"],
        Category.Gpu when Gpu is { } g => [$"{g.VramGb} GB", $"{g.LengthMm} mm", $"{g.PowerW} W"],
        Category.Storage when Storage is { } s => [Cap(s.CapacityGb), $"{s.ReadMBs:N0} MB/s read"],
        Category.Psu when Psu is { } p => [$"{p.Watts} W", p.Form, p.Rating],
        _ => [],
    };

    static string Cap(int gb) => gb >= 1000 ? $"{gb / 1000.0:0.#} TB" : $"{gb} GB";
    static string YesNo(bool b) => b ? "Yes" : "No";

    /// <summary>The full spec sheet for a part, as label/value rows.</summary>
    public IReadOnlyList<(string Label, string Value)> Specs => Category switch
    {
        Category.Case when Case is { } c =>
        [
            ("Type", c.Type), ("Motherboards", string.Join(", ", c.Boards)), ("Power supplies", string.Join(", ", c.Psus)),
            ("Max GPU length", $"{c.MaxGpuMm} mm"), ("Max cooler height", $"{c.MaxCoolerMm} mm"),
            ("Largest radiator", c.MaxRadMm > 0 ? $"{c.MaxRadMm} mm" : "None"), ("Fans included", c.Fans.ToString()),
            ("Side panel", c.Panel), ("Size (D × H × W)", $"{c.DepthMm} × {c.HeightMm} × {c.WidthMm} mm"),
            ("Volume", $"{c.DepthMm * c.HeightMm * (double)c.WidthMm / 1e6:0.0} L"),
        ],
        Category.Motherboard when Board is { } b =>
        [
            ("Socket", b.Socket), ("Chipset", b.Chipset), ("Form factor", b.Form), ("Memory", $"{b.Memory}, {b.MemorySlots} slots, up to {b.MaxMemoryGb} GB"),
            ("M.2 slots", $"{b.M2Slots}{(b.Pcie5M2 ? " (one or more PCIe 5.0)" : " (PCIe 4.0 or slower)")}"),
            ("Graphics slot", $"PCIe {b.GpuPcie} x16"), ("Wi-Fi", YesNo(b.Wifi)),
        ],
        Category.Cpu when Cpu is { } c =>
        [
            ("Architecture", c.Arch.Length > 0 ? c.Arch : "—"), ("Socket", c.Socket), ("Cores / threads", $"{c.Cores} / {c.Threads}"),
            ("Clock speed", c.BaseGhz > 0 ? $"{c.BaseGhz:0.0} GHz base, {c.BoostGhz:0.0} GHz boost" : $"{c.BoostGhz:0.0} GHz boost"),
            ("L3 cache", c.L3Mb > 0 ? $"{c.L3Mb} MB" : "—"), ("Rated power", c.TdpW > 0 ? $"{c.TdpW} W" : "—"), ("Max power", $"{c.PowerW} W"),
            ("Built-in graphics", YesNo(c.Igpu)), ("Cinebench R23 (multi / single)", $"{c.Multi:N0} / {c.Single:N0}"),
            ("Gaming index", $"{c.Game} (Ryzen 7 9800X3D = 100)"),
        ],
        Category.Cooler when Cooler is { } c =>
        [
            ("Type", c.IsLiquid ? "All-in-one liquid" : c.HeightMm <= 90 ? "Low-profile air" : "Tower air"),
            c.IsLiquid ? ("Radiator", $"{c.RadMm} mm") : ("Height", $"{c.HeightMm} mm"),
            ("Cooling capacity", $"~{c.CapacityW} W"), ("Fans", c.Fans.ToString()), ("Noise", $"~{c.NoiseDb} dB(A)"), ("RGB", YesNo(c.Rgb)),
        ],
        Category.Memory when Memory is { } m =>
        [
            ("Type", m.Type), ("Capacity", $"{m.CapacityGb} GB ({m.Modules} × {m.CapacityGb / Math.Max(1, m.Modules)} GB)"),
            ("Speed", $"{m.Speed} MT/s"), ("CAS latency", $"CL{m.Cl}"), ("True latency", $"{m.Cl * 2000.0 / m.Speed:0.0} ns"),
            ("Profile", m.Profile.Length > 0 ? m.Profile : "—"), ("RGB", YesNo(m.Rgb)),
        ],
        Category.Gpu when Gpu is { } g =>
        [
            ("Memory", $"{g.VramGb} GB{(g.MemType.Length > 0 ? " " + g.MemType : "")}"), ("Boost clock", g.BoostMhz > 0 ? $"{g.BoostMhz} MHz" : "—"),
            ("Length", $"{g.LengthMm} mm"), ("Thickness", $"{g.Slots:0.#} slots"), ("Board power", $"{g.PowerW} W"), ("Power connector", g.Connector),
            ("Raster / ray tracing index", $"{g.Raster} / {g.Rt} (RTX 4090 = 100)"),
        ],
        Category.Storage when Storage is { } s =>
        [
            ("Interface", s.Iface switch { "NVMe5" => "NVMe PCIe 5.0", "NVMe4" => "NVMe PCIe 4.0", "NVMe3" => "NVMe PCIe 3.0", "SATA" => "SATA III", "HDD" => "SATA hard drive", _ => s.Iface }),
            ("Form factor", s.FormFactor), ("Capacity", Cap(s.CapacityGb)), ("Sequential read / write", $"{s.ReadMBs:N0} / {s.WriteMBs:N0} MB/s"),
            ("DRAM cache", s.Iface == "HDD" ? "—" : YesNo(s.Dram)), ("Endurance", s.Tbw > 0 ? $"{s.Tbw:N0} TBW" : "—"),
        ],
        Category.Psu when Psu is { } p =>
        [
            ("Wattage", $"{p.Watts} W"), ("Form factor", p.Form), ("Efficiency", p.Rating), ("Cables", p.Modular.Length > 0 ? p.Modular : "—"),
            ("12V-2x6 GPU cable", p.Native12v2x6 ? "Native" : "Adapter"), ("ATX 3.1", YesNo(p.Atx31)),
        ],
        _ => [],
    };
}
