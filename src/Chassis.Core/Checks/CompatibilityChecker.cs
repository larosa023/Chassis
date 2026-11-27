using Chassis.Core.Builds;
using Chassis.Core.Catalog;

namespace Chassis.Core.Checks;

public enum Severity { Good, Info, Warning, Problem }

/// <summary>One finding about a build. Problems mean it won't physically fit or won't boot.</summary>
public sealed record Finding(Severity Severity, string Title, string Detail, params Category[] Parts);

public sealed record CheckReport(IReadOnlyList<Finding> Findings, int Missing = 0)
{
    public int Problems => Findings.Count(f => f.Severity == Severity.Problem);
    public int Warnings => Findings.Count(f => f.Severity == Severity.Warning);
    public bool Ok => Problems == 0;
    public bool Flags(Category c) => Findings.Any(f => f.Severity == Severity.Problem && f.Parts.Contains(c));

    public string Headline => Problems > 0
        ? $"{Problems} problem{(Problems == 1 ? "" : "s")} to fix"
        : Missing == Categories.All.Count ? "Start with a case"
        : Missing > 0 ? $"So far so good, {Missing} part{(Missing == 1 ? "" : "s")} to go"
        : Warnings > 0 ? $"Works, with {Warnings} thing{(Warnings == 1 ? "" : "s")} to know" : "Everything fits and works together";
}

public static class CompatibilityChecker
{
    /// <summary>Estimated peak draw: CPU and GPU at full load plus board, memory, fans and drives.</summary>
    public static int PeakWatts(PcBuild.Resolved b) =>
        (b.Cpu?.Cpu?.PowerW ?? 0) + (b.Gpu?.Gpu?.PowerW ?? 0) + (b.Count > 0 ? 50 : 0) + (b.Storage is null ? 0 : 8);

    /// <summary>Power supply we'd recommend: peak plus 30% headroom for GPU spikes, rounded up to a common size.</summary>
    public static int RecommendedPsu(PcBuild.Resolved b)
    {
        var need = PeakWatts(b) * 1.3;
        int[] sizes = [550, 650, 750, 850, 1000, 1200, 1600];
        return sizes.FirstOrDefault(s => s >= need, 1600);
    }

    public static CheckReport Check(PcBuild.Resolved b)
    {
        var f = new List<Finding>();
        var (pc, board, cpu, cooler, mem, gpu, ssd, psu) = (b.Case?.Case, b.Board?.Board, b.Cpu?.Cpu, b.Cooler?.Cooler, b.Memory?.Memory, b.Gpu?.Gpu, b.Storage?.Storage, b.Psu?.Psu);

        if (cpu is not null && board is not null)
        {
            if (cpu.Socket != board.Socket)
                f.Add(new(Severity.Problem, "Processor doesn't fit the motherboard",
                    $"The {b.Cpu!.Name} uses the {cpu.Socket} socket, but the {b.Board!.Name} has {board.Socket}.", Category.Cpu, Category.Motherboard));
            else
                f.Add(new(Severity.Good, "Processor fits the motherboard", $"Both use {cpu.Socket}.", Category.Cpu, Category.Motherboard));
        }

        if (mem is not null && board is not null)
        {
            if (mem.Type != board.Memory)
                f.Add(new(Severity.Problem, "Wrong type of memory",
                    $"The {b.Board!.Name} takes {board.Memory}, but this kit is {mem.Type}. They don't fit the same slots.", Category.Memory, Category.Motherboard));
            else if (mem.Modules > board.MemorySlots)
                f.Add(new(Severity.Problem, "Not enough memory slots", $"This kit has {mem.Modules} sticks and the board has {board.MemorySlots} slots.", Category.Memory, Category.Motherboard));
            else
            {
                f.Add(new(Severity.Good, "Memory fits the motherboard", $"{mem.Type}, {mem.Modules} of {board.MemorySlots} slots used.", Category.Memory, Category.Motherboard));
                if (mem.Type == "DDR5" && board.Socket == "AM5" && mem.Speed > 6400)
                    f.Add(new(Severity.Info, "Fast memory may need tuning", "Ryzen runs best at DDR5-6000. Faster kits may need a manual BIOS setting.", Category.Memory));
            }
        }

        if (board is not null && pc is not null)
        {
            if (!pc.Boards.Contains(board.Form))
                f.Add(new(Severity.Problem, "Motherboard is too big for the case",
                    $"The {b.Case!.Name} fits {string.Join(", ", pc.Boards)} boards; the {b.Board!.Name} is {board.Form}.", Category.Motherboard, Category.Case));
            else
                f.Add(new(Severity.Good, "Motherboard fits the case", $"{board.Form} board in a case that takes {string.Join(", ", pc.Boards)}.", Category.Motherboard, Category.Case));
        }

        if (gpu is not null && pc is not null)
        {
            var spare = pc.MaxGpuMm - gpu.LengthMm;
            if (spare < 0)
                f.Add(new(Severity.Problem, "Graphics card is too long",
                    $"The card is {gpu.LengthMm} mm and the case fits up to {pc.MaxGpuMm} mm ({-spare} mm short).", Category.Gpu, Category.Case));
            else if (spare < 15)
                f.Add(new(Severity.Warning, "Tight fit for the graphics card", $"Only {spare} mm to spare. Front fans or a radiator there can block it.", Category.Gpu, Category.Case));
            else
                f.Add(new(Severity.Good, "Graphics card fits", $"{spare} mm of room to spare.", Category.Gpu, Category.Case));
        }

        if (cooler is not null && pc is not null)
        {
            if (cooler.IsLiquid)
            {
                if (cooler.RadMm > pc.MaxRadMm)
                    f.Add(new(Severity.Problem, "Radiator is too big for the case", $"This cooler needs room for a {cooler.RadMm} mm radiator; the case takes up to {pc.MaxRadMm} mm.", Category.Cooler, Category.Case));
                else
                    f.Add(new(Severity.Good, "Radiator fits", $"{cooler.RadMm} mm radiator, case takes up to {pc.MaxRadMm} mm.", Category.Cooler, Category.Case));
            }
            else if (cooler.HeightMm > pc.MaxCoolerMm)
                f.Add(new(Severity.Problem, "Cooler is too tall", $"The cooler is {cooler.HeightMm} mm and the case fits up to {pc.MaxCoolerMm} mm. The side panel won't close.", Category.Cooler, Category.Case));
            else
                f.Add(new(Severity.Good, "Cooler fits under the side panel", $"{pc.MaxCoolerMm - cooler.HeightMm} mm of clearance.", Category.Cooler, Category.Case));
        }

        if (cooler is not null && cpu is not null)
        {
            var ratio = (double)cooler.CapacityW / cpu.PowerW;
            if (ratio < 0.85)
                f.Add(new(Severity.Warning, "Cooler is undersized", $"The {b.Cpu!.Name} can pull {cpu.PowerW} W; this cooler handles about {cooler.CapacityW} W. It will run hot and slow down under heavy loads.", Category.Cooler, Category.Cpu));
            else if (ratio < 1.05)
                f.Add(new(Severity.Info, "Cooler is just enough", "Fine for gaming. Long all-core workloads will run warm.", Category.Cooler, Category.Cpu));
        }

        if (psu is not null && pc is not null)
        {
            if (!pc.Psus.Contains(psu.Form))
                f.Add(new(Severity.Problem, "Power supply doesn't fit the case", $"The case takes {string.Join(" or ", pc.Psus)} power supplies; this one is {psu.Form}.", Category.Psu, Category.Case));
        }

        if (psu is not null && (cpu is not null || gpu is not null))
        {
            var peak = PeakWatts(b);
            var rec = RecommendedPsu(b);
            if (psu.Watts < peak)
                f.Add(new(Severity.Problem, "Power supply is too weak", $"The build can draw about {peak} W at full load; this power supply is {psu.Watts} W.", Category.Psu));
            else if (psu.Watts < peak * 1.2)
                f.Add(new(Severity.Warning, "Power supply has little headroom", $"About {peak} W at full load on a {psu.Watts} W unit. We'd suggest {rec} W or more.", Category.Psu));
            else
                f.Add(new(Severity.Good, "Plenty of power", $"About {peak} W at full load on a {psu.Watts} W unit.", Category.Psu));
            if (gpu is { Connector: "12V-2x6" } && !psu.Native12v2x6)
                f.Add(new(Severity.Info, "Uses an adapter for the GPU", "This card uses a 12V-2x6 plug and the power supply needs the included adapter. Push it in all the way.", Category.Gpu, Category.Psu));
        }

        if (ssd is not null && board is not null && ssd.Iface == "NVMe5" && !board.Pcie5M2)
            f.Add(new(Severity.Warning, "SSD will run at half speed", "This is a PCIe 5.0 drive, but the motherboard's M.2 slots are PCIe 4.0. It works, capped near 7,000 MB/s.", Category.Storage, Category.Motherboard));

        if (cpu is not null && b.Board is not null && gpu is null && !cpu.Igpu)
            f.Add(new(Severity.Problem, "No graphics output", $"The {b.Cpu!.Name} has no built-in graphics, so the PC needs a graphics card to show anything.", Category.Cpu, Category.Gpu));

        if (gpu is not null && pc is not null && gpu.Slots >= 3 && pc.Boards.SequenceEqual(["ITX"]))
            f.Add(new(Severity.Info, "Thick card in a small case", "A 3-slot card in a mini-ITX case runs warmer. Make sure the side fans are installed.", Category.Gpu, Category.Case));

        if (mem is not null && mem.CapacityGb < 32)
            f.Add(new(Severity.Info, "16 GB is the minimum today", "Fine for most games; 32 GB gives headroom for newer titles, browsers and streaming.", Category.Memory));

        var missing = b.Missing.ToList();
        if (missing.Count > 0)
            f.Add(new(Severity.Info, missing.Count == Categories.All.Count ? "Start with a case" : $"{missing.Count} part{(missing.Count == 1 ? "" : "s")} to go",
                "Still needed: " + string.Join(", ", missing.Select(Categories.Label)) + ".", [.. missing]));

        var ordered = f.OrderByDescending(x => x.Severity).ToList();
        return new CheckReport(ordered, missing.Count);
    }
}
