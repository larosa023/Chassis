using System.Text.Json;
using Chassis.App.ViewModels;
using Chassis.App.ViewModels.Theming;
using Chassis.Core.Builds;
using Chassis.Core.Catalog;
using Chassis.Core.Storage;
using Xunit;

namespace Chassis.App.ViewModels.Tests;

public sealed class SessionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "chassis-vm-" + Guid.NewGuid().ToString("N"));
    readonly BuildSession _s;

    public SessionTests() => _s = new BuildSession(PartCatalog.Default, new Garage(Path.Combine(_dir, "garage.json")));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Opens_the_first_preset_complete()
    {
        Assert.Equal(Presets.All[0].Name, _s.Build.Name);
        Assert.All(_s.Slots, s => Assert.True(s.State is SlotState.Ok or SlotState.Warning, s.Label));
    }

    [Fact]
    public void Choosing_a_part_updates_everything_and_undo_restores_it()
    {
        var before = _s.Bench.Score;
        var changes = 0;
        _s.BuildChanged += () => changes++;
        _s.Selected = Category.Gpu;
        _s.Choose("intel-b580");
        Assert.Equal("intel-b580", _s.Build[Category.Gpu]);
        Assert.True(_s.Bench.Score < before);
        Assert.True(_s.CanUndo);
        _s.Undo();
        Assert.Equal("nv-5090", _s.Build[Category.Gpu]);
        Assert.Equal(before, _s.Bench.Score);
        _s.Redo();
        Assert.Equal("intel-b580", _s.Build[Category.Gpu]);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Candidates_mark_parts_that_wont_fit()
    {
        _s.Selected = Category.Cpu;   // flagship is AM5
        var intel = _s.Candidates.First(c => c.Part.Id == "intel-14900k");
        Assert.False(intel.Fits);
        Assert.Contains("doesn't fit", intel.Reason!);
        Assert.True(_s.Candidates.First(c => c.Part.Id == "amd-9950x").Fits);
        Assert.True(_s.Candidates[0].Selected);
        Assert.Equal("In your build", _s.Candidates[0].DeltaText);
    }

    [Fact]
    public void Search_and_sort_filter_candidates()
    {
        _s.Selected = Category.Gpu;
        _s.Query = "radeon";
        Assert.All(_s.Candidates, c => Assert.Contains("Radeon", c.Part.Name));
        _s.Sort = SortMode.PriceLow;
        Assert.True(_s.Candidates.Zip(_s.Candidates.Skip(1)).All(p => p.First.Part.Price <= p.Second.Part.Price));
        _s.Selected = Category.Psu;
        Assert.Equal("", _s.Query);
    }

    [Fact]
    public void New_build_starts_empty_and_walks_through_slots()
    {
        _s.NewBuild();
        Assert.Equal(Category.Case, _s.Selected);
        Assert.All(_s.Slots, s => Assert.Equal(SlotState.Empty, s.State));
        _s.Choose("fractal-north");
        Assert.Equal(Category.Motherboard, _s.Selected);
        Assert.Contains(_s.Garage.Builds, b => b.Id == _s.Build.Id);
    }

    [Fact]
    public void Scene_message_has_parts_and_problems()
    {
        _s.Choose("intel-14900k");
        using var doc = JsonDocument.Parse(_s.SceneJson());
        var r = doc.RootElement;
        Assert.Equal("build", r.GetProperty("type").GetString());
        Assert.Equal(285, r.GetProperty("case").GetProperty("widthMm").GetInt32());
        Assert.Equal(575, r.GetProperty("gpu").GetProperty("powerW").GetInt32());
        Assert.Equal("Liquid", r.GetProperty("cooler").GetProperty("kind").GetString());
        var probs = r.GetProperty("problems").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Cpu", probs);
        Assert.Contains("Motherboard", probs);
        Assert.DoesNotContain("Case", probs);
    }

    [Fact]
    public void Share_code_import_opens_a_new_copy()
    {
        var code = _s.ShareCode;
        _s.NewBuild();
        Assert.True(_s.Import(code, out var b));
        Assert.Equal(Presets.All[0].Name, b.Name);
        Assert.NotEqual(Presets.All[0].Id, b.Id);
        Assert.False(_s.Import("nope", out _));
    }

    [Fact]
    public void Imported_parts_go_in_as_one_undo_step_and_keep_the_case()
    {
        _s.NewBuild();
        _s.Choose("fractal-north");
        var found = Chassis.Core.Import.SpecMatcher.Match("Ryzen 7 9800X3D\nGeForce RTX 5080\nNZXT H5 Flow", PartCatalog.Default);
        _s.ApplyParts(found.Matches.Where(m => m.Category != Category.Case).Select(m => m.Part));
        Assert.Equal("fractal-north", _s.Build[Category.Case]);
        Assert.Equal("amd-9800x3d", _s.Build[Category.Cpu]);
        Assert.Equal("nv-5080", _s.Build[Category.Gpu]);
        Assert.Equal(Category.Motherboard, _s.Selected);
        _s.Undo();
        Assert.Null(_s.Build[Category.Cpu]);
        Assert.Equal("fractal-north", _s.Build[Category.Case]);
    }

    [Fact]
    public void Remove_empties_a_slot()
    {
        _s.Remove(Category.Storage);
        Assert.Equal(SlotState.Empty, _s.Slots.Single(x => x.Category == Category.Storage).State);
        Assert.Equal(0, _s.Bench.StorageReadMBs);
    }
}

public class ThemeTests
{
    [Fact]
    public void Every_theme_is_readable()
    {
        foreach (var t in Themes.All)
        {
            Assert.True(Contrast.Ratio(t.Text, t.Bg) >= 7, $"{t.Name} text");
            Assert.True(Contrast.Ratio(t.Text, t.Surface) >= 7, $"{t.Name} text on cards");
            Assert.True(Contrast.Ratio(t.Muted, t.Surface) >= 4.5, $"{t.Name} muted {Contrast.Ratio(t.Muted, t.Surface):0.00}");
            Assert.True(Contrast.Ratio(t.OnAccent, t.Accent) >= 3, $"{t.Name} accent button {Contrast.Ratio(t.OnAccent, t.Accent):0.00}");
            Assert.True(Contrast.Ratio(t.Good, t.Surface) >= 3, $"{t.Name} good");
            Assert.True(Contrast.Ratio(t.Bad, t.Surface) >= 3, $"{t.Name} bad");
        }
        Assert.Equal("bench", Themes.Get("nope").Key);
    }
}

public class UpdateTests
{
    sealed class Fake(UpdateOffer? offer, bool installed = true, bool fail = false) : IUpdateSource
    {
        public bool IsInstalled => installed;
        public Task<UpdateOffer?> CheckAsync() => fail ? throw new HttpRequestException("offline") : Task.FromResult(offer);
        public Task DownloadAndRestartAsync(UpdateOffer o, IProgress<int> p) { p.Report(100); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Shows_banner_when_update_exists()
    {
        var vm = new UpdateViewModel(new Fake(new UpdateOffer("0.2.0", "New stuff")), a => a());
        await vm.CheckAsync();
        Assert.True(vm.Available);
        Assert.Equal("Chassis 0.2.0 is ready", vm.Title);
    }

    [Fact]
    public async Task Quiet_when_offline_but_manual_check_explains()
    {
        var vm = new UpdateViewModel(new Fake(null, fail: true), a => a());
        await vm.CheckAsync();
        Assert.False(vm.Available);
        Assert.Equal("", vm.Status);
        await vm.CheckAsync(manual: true);
        Assert.Contains("Couldn't reach GitHub", vm.Status);
        var dev = new UpdateViewModel(new Fake(null, installed: false), a => a());
        await dev.CheckAsync(manual: true);
        Assert.Contains("development build", dev.Status);
    }
}
