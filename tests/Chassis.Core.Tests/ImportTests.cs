using Chassis.Core.Catalog;
using Chassis.Core.Import;
using Xunit;

namespace Chassis.Core.Tests;

public class ImportTests
{
    static ImportResult M(string text) => SpecMatcher.Match(text, PartCatalog.Default);

    public const string PartPicker = """
        PCPartPicker Part List
        CPU AMD Ryzen 7 9800X3D 4.7 GHz 8-Core Processor $479.00
        CPU Cooler Thermalright Peerless Assassin 120 SE 66.17 CFM CPU Cooler $35.90
        Motherboard MSI MAG B650 TOMAHAWK WIFI ATX AM5 Motherboard $199.99
        Memory G.Skill Trident Z5 RGB 32 GB (2 x 16 GB) DDR5-6000 CL30 Memory $109.99
        Storage Samsung 990 Pro 2 TB M.2-2280 PCIe 4.0 X4 NVME Solid State Drive $169.99
        Video Card ASUS TUF GAMING GeForce RTX 5070 Ti 16 GB Video Card $799.99
        Power Supply Corsair RM850x (2024) 850 W 80+ Gold Certified Fully Modular ATX Power Supply $139.99
        Total: $1,734.85
        """;

    [Fact]
    public void Reads_a_pcpartpicker_list()
    {
        var r = M(PartPicker);
        Assert.Equal("amd-9800x3d", r.For(Category.Cpu)?.Part.Id);
        Assert.Equal("thermalright-pa120se", r.For(Category.Cooler)?.Part.Id);
        Assert.Equal("msi-b650-tomahawk", r.For(Category.Motherboard)?.Part.Id);
        Assert.Equal("gskill-z5-32-6000", r.For(Category.Memory)?.Part.Id);
        Assert.Equal("samsung-990pro-2t", r.For(Category.Storage)?.Part.Id);
        Assert.Equal("nv-5070ti", r.For(Category.Gpu)?.Part.Id);
        Assert.Equal("corsair-rm850x", r.For(Category.Psu)?.Part.Id);
        Assert.Equal([Category.Case], r.NotFound.ToList());
        Assert.Equal(MatchStrength.Exact, r.For(Category.Cpu)!.Strength);
    }

    [Fact]
    public void Handles_windows_line_breaks()
    {
        var r = M(PartPicker.Replace("\n", "\r"));
        Assert.Equal(7, r.Matches.Count);
        Assert.DoesNotContain("\r", r.For(Category.Cpu)!.Evidence);
        Assert.True(r.For(Category.Cpu)!.Evidence.Length < 120);
    }

    [Fact]
    public void Reads_task_manager_style_specs()
    {
        var r = M("""
            AMD Ryzen 9 7900X 12-Core Processor
            NVIDIA GeForce RTX 4090
            Memory
            64.0 GB
            Speed: 6000 MT/s
            Form factor: DIMM DDR5
            Samsung SSD 990 PRO 2TB
            """);
        Assert.Equal("amd-7900x", r.For(Category.Cpu)?.Part.Id);
        Assert.Equal("nv-4090", r.For(Category.Gpu)?.Part.Id);
        var mem = r.For(Category.Memory)!.Part.Memory!;
        Assert.Equal(("DDR5", 64, 6000), (mem.Type, mem.CapacityGb, mem.Speed));
        Assert.Equal(MatchStrength.Guess, r.For(Category.Memory)!.Strength);
        Assert.Equal("samsung-990pro-2t", r.For(Category.Storage)?.Part.Id);
    }

    [Fact]
    public void Tells_similar_names_apart()
    {
        var r = M("""
            Intel Core i5-13400F
            MSI PRO B760M-A WIFI DDR4
            Corsair Vengeance LPX 32GB (2x16GB) DDR4 3200 CL16
            AMD Radeon RX 7600
            Lian Li O11 Dynamic EVO XL
            """);
        Assert.Equal("intel-13400f", r.For(Category.Cpu)?.Part.Id);
        Assert.Equal("msi-b760m-ddr4", r.For(Category.Motherboard)?.Part.Id);
        Assert.Equal("corsair-lpx-32-3200", r.For(Category.Memory)?.Part.Id);
        Assert.Equal("amd-rx7600", r.For(Category.Gpu)?.Part.Id);
        Assert.Equal("lianli-o11-evo-xl", r.For(Category.Case)?.Part.Id);
    }

    [Theory]
    [InlineData("GeForce RTX 5070", "nv-5070")]
    [InlineData("RTX 5070 Ti", "nv-5070ti")]
    [InlineData("MSI GeForce RTX 5060 Ti 8G Ventus 2X 8GB", "msi-rtx5060ti8-ventus")]
    [InlineData("Radeon RX 9070 XT", "amd-9070xt")]
    [InlineData("Sapphire NITRO+ AMD Radeon RX 9070 XT", "sapphire-9070xt-nitro")]
    [InlineData("Intel Arc B580", "intel-b580")]
    public void Picks_the_right_graphics_card(string text, string id) => Assert.Equal(id, M(text).For(Category.Gpu)?.Part.Id);

    [Theory]
    [InlineData("Ryzen 9 9950X3D", "amd-9950x3d")]
    [InlineData("AMD Ryzen 9 9950X 16-Core", "amd-9950x")]
    [InlineData("Intel(R) Core(TM) i9-14900K", "intel-14900k")]
    [InlineData("Intel Core Ultra 7 265K", "intel-265k")]
    [InlineData("Ryzen 5 7600", "amd-7600")]
    public void Picks_the_right_processor(string text, string id) => Assert.Equal(id, M(text).For(Category.Cpu)?.Part.Id);

    [Fact]
    public void Radeon_model_number_is_not_read_as_a_ryzen()
    {
        var r = M("AMD Radeon RX 7600 8GB");
        Assert.Null(r.For(Category.Cpu));
        Assert.Equal("amd-rx7600", r.For(Category.Gpu)?.Part.Id);
    }

    [Fact]
    public void Survives_common_misreads()
    {
        Assert.Equal("lianli-o11-evo", M("Lian Li 011 Dynamic EVO").For(Category.Case)?.Part.Id);
        Assert.Equal("amd-9800x3d", M("AMD Ryzen 7 98OOX3D").For(Category.Cpu)?.Part.Id);
        Assert.Equal("msi-b650-tomahawk", M("MSI MAG 8650 TOMAHAWK WIFI ATX AM5 Motherboard").For(Category.Motherboard)?.Part.Id);
    }

    [Fact]
    public void Nothing_in_garbage()
    {
        Assert.Empty(M("hello world\nthis is not a spec list\n1234").Matches);
        Assert.Empty(M("").Matches);
    }

    [Fact]
    public void Normalizes_sizes_and_symbols()
    {
        Assert.Equal("ryzen 7 9800x3d 64gb 2tb", SpecMatcher.Normalize("Ryzen™ 7 9800X3D — 64.0 GB, 2 TB"));
    }
}
