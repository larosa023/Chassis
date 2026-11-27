using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Chassis.App.Theming;
using Chassis.Core.Catalog;

namespace Chassis.App.Services;

/// <summary>The 3D bench: a WebView2 running the bundled Three.js scene (Assets/Scene). Messages go both ways as JSON.</summary>
public sealed partial class SceneView : Grid
{
    const string Host = "scene.chassis.app";
    readonly WebView2 _web = new();
    readonly Border _fallback;
    bool _ready, _starting;
    string? _theme, _build;
    readonly List<string> _queued = new();

    public event Action<Category>? Picked;

    /// <summary>A picture was dropped onto the 3D view (bytes, file name).</summary>
    public event Action<byte[], string>? ImageDropped;

    public SceneView()
    {
        _fallback = new Border
        {
            Visibility = Visibility.Collapsed, Padding = new Thickness(32),
            Child = Ui.Column(8,
                Ui.Title("The 3D view couldn't start", 22),
                Ui.Muted("Chassis draws the 3D bench with Microsoft Edge WebView2, which comes with Windows 11. If it's missing, install the WebView2 Runtime from Microsoft and reopen Chassis. Everything else still works."),
                new HyperlinkButton { Content = "Get the WebView2 Runtime", NavigateUri = new Uri("https://developer.microsoft.com/microsoft-edge/webview2/") }),
        };
        Children.Add(_web);
        Children.Add(_fallback);
        _web.DefaultBackgroundColor = ThemeBrushes.Bg.Color;
        Loaded += async (_, _) => await StartAsync();
    }

    async Task StartAsync()
    {
        if (_starting) return;
        _starting = true;
        try
        {
            await _web.EnsureCoreWebView2Async();
            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Assets", "Scene"), CoreWebView2HostResourceAccessKind.Allow);
            var s = core.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = Environment.GetEnvironmentVariable("CHASSIS_DEVTOOLS") == "1";
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += (_, e) =>
            {
                CrashLog.Write($"scene process failed: {e.ProcessFailedKind}", null, show: false);
                _ready = false;
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    core.Reload();
            };
            core.NavigationStarting += (_, e) =>
            {
                // The scene never navigates anywhere else.
                if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            _web.Source = new Uri($"https://{Host}/index.html");
        }
        catch (Exception e)
        {
            CrashLog.Write("scene start", e, show: false);
            _web.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
        }
    }

    void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "ready")
            {
                _ready = true;
                if (_theme is not null) Send(_theme);
                if (_build is not null) Send(_build);
                foreach (var q in _queued) Send(q);
                _queued.Clear();
            }
            else if (type == "pick" && doc.RootElement.TryGetProperty("category", out var c) && Enum.TryParse<Category>(c.GetString(), out var cat))
                Picked?.Invoke(cat);
            else if (type == "importImage" && doc.RootElement.TryGetProperty("dataUrl", out var d) && d.GetString() is { } url && url.IndexOf(',') is var comma and > 0)
            {
                var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "the picture" : "the picture";
                try { ImageDropped?.Invoke(Convert.FromBase64String(url[(comma + 1)..]), name); }
                catch (FormatException) { }
            }
        }
        catch (JsonException) { }
    }

    void Send(string json)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(json); }
        catch (Exception e) { CrashLog.Write("scene send", e, show: false); }
    }

    public void SetTheme(object themeMessage)
    {
        _theme = JsonSerializer.Serialize(themeMessage);
        _web.DefaultBackgroundColor = ThemeBrushes.Bg.Color;
        if (_ready) Send(_theme);
    }

    public void SetBuild(string json)
    {
        _build = json;
        if (_ready) Send(json);
    }

    /// <summary>View options: exploded, rgb, autoRotate, reset, focus.</summary>
    public void SetView(object view)
    {
        var json = JsonSerializer.Serialize(view);
        if (_ready) Send(json); else _queued.Add(json);
    }
}
