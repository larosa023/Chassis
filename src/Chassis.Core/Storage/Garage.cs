using System.Text.Json;
using System.Text.Json.Serialization;
using Chassis.Core.Builds;

namespace Chassis.Core.Storage;

public sealed record AppSettings
{
    public string Theme { get; init; } = "bench";
    public bool RgbLighting { get; init; } = true;
    public bool AutoRotate { get; init; } = false;
    public bool CheckForUpdates { get; init; } = true;
    public string? LastBuildId { get; init; }
}

sealed record GarageFile(int Version, List<PcBuild> Builds, AppSettings Settings);

/// <summary>Saved builds and settings in one small JSON file (%LocalAppData%\Chassis\garage.json). Writes are atomic.</summary>
public sealed class Garage
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly string _path;
    readonly List<PcBuild> _builds;

    public AppSettings Settings { get; private set; }
    public IReadOnlyList<PcBuild> Builds => _builds;
    public event Action? Changed;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chassis", "garage.json");

    public Garage(string path)
    {
        _path = path;
        GarageFile? file = null;
        try { if (File.Exists(path)) file = JsonSerializer.Deserialize<GarageFile>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Copy(path, path + ".broken", overwrite: true); } catch (IOException) { }
        }
        _builds = file?.Builds is { Count: > 0 } b ? b : Presets.All.Select(p => p with { }).ToList();
        Settings = file?.Settings ?? new AppSettings();
    }

    public PcBuild? Find(string? id) => _builds.FirstOrDefault(b => b.Id == id);

    /// <summary>The build to open on launch: the last one used, or the first in the garage.</summary>
    public PcBuild Current => Find(Settings.LastBuildId) ?? _builds.FirstOrDefault() ?? Presets.All[0];

    public void Save(PcBuild build)
    {
        var i = _builds.FindIndex(b => b.Id == build.Id);
        if (i >= 0) _builds[i] = build; else _builds.Insert(0, build);
        Settings = Settings with { LastBuildId = build.Id };
        Persist();
    }

    public bool Delete(string id)
    {
        if (_builds.RemoveAll(b => b.Id == id) == 0) return false;
        if (Settings.LastBuildId == id) Settings = Settings with { LastBuildId = _builds.FirstOrDefault()?.Id };
        Persist();
        return true;
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        Persist();
    }

    void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new GarageFile(1, _builds, Settings), Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* keep going in memory */ }
        if (Changed is { } handlers)
            foreach (var h in handlers.GetInvocationList().Cast<Action>())
            {
                try { h(); }
                catch (Exception e) { ListenerFailed?.Invoke(e); }
            }
    }

    /// <summary>Where listener failures are reported (the app logs them). A failing listener never stops a save.</summary>
    public static Action<Exception>? ListenerFailed { get; set; }
}
