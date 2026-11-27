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

public sealed record CaseSpec(string[] Boards, string[] Psus, int MaxGpuMm, int MaxCoolerMm, int MaxRadMm, int DepthMm, int HeightMm, int WidthMm, int Fans);
public sealed record BoardSpec(string Socket, string Chipset, string Form, string Memory, int MemorySlots, int M2Slots, bool Pcie5M2, bool Wifi);
public sealed record CpuSpec(string Socket, int Cores, int Threads, double BoostGhz, int PowerW, int Multi, int Single, int Game, bool Igpu);
public sealed record CoolerSpec(string Kind, int HeightMm, int RadMm, int CapacityW, int NoiseDb)
{
    public bool IsLiquid => Kind == "Liquid";
}
public sealed record MemorySpec(string Type, int CapacityGb, int Speed, int Cl, int Modules, bool Rgb = false);
public sealed record GpuSpec(int LengthMm, double Slots, int PowerW, int Raster, int Rt, int VramGb, string Connector);
public sealed record StorageSpec(string Iface, int CapacityGb, int ReadMBs, int WriteMBs);
public sealed record PsuSpec(int Watts, string Form, string Rating, bool Native12v2x6);

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

    /// <summary>Two or three short facts for a part card.</summary>
    public IReadOnlyList<string> Facts => Category switch
    {
        Category.Case when Case is { } c => [string.Join(" / ", c.Boards), $"GPU ≤ {c.MaxGpuMm} mm", $"Cooler ≤ {c.MaxCoolerMm} mm"],
        Category.Motherboard when Board is { } b => [b.Socket, b.Form, b.Memory, b.Pcie5M2 ? "PCIe 5.0 SSD" : "PCIe 4.0 SSD"],
        Category.Cpu when Cpu is { } c => [$"{c.Cores} cores / {c.Threads} threads", $"{c.BoostGhz:0.0} GHz", $"{c.PowerW} W", c.Socket],
        Category.Cooler when Cooler is { } c => c.IsLiquid ? [$"{c.RadMm} mm radiator", $"Handles ~{c.CapacityW} W"] : [$"{c.HeightMm} mm tall", $"Handles ~{c.CapacityW} W"],
        Category.Memory when Memory is { } m => [$"{m.CapacityGb} GB", $"{m.Type}-{m.Speed}", $"CL{m.Cl}"],
        Category.Gpu when Gpu is { } g => [$"{g.VramGb} GB", $"{g.LengthMm} mm", $"{g.PowerW} W"],
        Category.Storage when Storage is { } s => [s.CapacityGb >= 1000 ? $"{s.CapacityGb / 1000.0:0.#} TB" : $"{s.CapacityGb} GB", $"{s.ReadMBs:N0} MB/s read"],
        Category.Psu when Psu is { } p => [$"{p.Watts} W", p.Form, p.Rating],
        _ => [],
    };
}
