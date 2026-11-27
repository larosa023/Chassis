namespace Chassis.App.ViewModels;

public sealed record UpdateOffer(string Version, string Notes);

public interface IUpdateSource
{
    bool IsInstalled { get; }
    Task<UpdateOffer?> CheckAsync();
    Task DownloadAndRestartAsync(UpdateOffer offer, IProgress<int> progress);
}

/// <summary>Built-in updater: quiet when there's nothing new or no internet; a one-click banner when there is.</summary>
public sealed class UpdateViewModel : ObservableObject
{
    readonly IUpdateSource _source;
    readonly Action<Action> _post;
    UpdateOffer? _offer;
    int _progress;
    bool _downloading;
    string _status = "";

    public UpdateViewModel(IUpdateSource source, Action<Action> post)
    {
        _source = source;
        _post = post;
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => _offer is not null && !_downloading);
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(manual: true));
        DismissCommand = new RelayCommand(() => Offer = null);
    }

    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand CheckCommand { get; }
    public RelayCommand DismissCommand { get; }

    UpdateOffer? Offer
    {
        get => _offer;
        set
        {
            _offer = value;
            OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Notes));
            UpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool Available => _offer is not null;
    public string Title => _offer is { } o ? $"Chassis {o.Version} is ready" : "";
    public string Notes => _offer?.Notes ?? "";
    public int Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool Downloading { get => _downloading; private set => SetProperty(ref _downloading, value); }

    /// <summary>Result of the last manual check, shown in Settings.</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public async Task CheckAsync(bool manual = false)
    {
        if (manual) _post(() => Status = "Checking…");
        try
        {
            if (!_source.IsInstalled)
            {
                if (manual) _post(() => Status = "Updates work in the installed app (this is a development build).");
                return;
            }
            var o = await _source.CheckAsync();
            _post(() =>
            {
                Offer = o;
                if (manual) Status = o is null ? "You're on the latest version." : $"Version {o.Version} is ready to install.";
            });
        }
        catch (Exception)
        {
            if (manual) _post(() => Status = "Couldn't reach GitHub. Check your connection and try again.");
        }
    }

    async Task UpdateAsync()
    {
        if (_offer is not { } o) return;
        Downloading = true;
        try { await _source.DownloadAndRestartAsync(o, new Progress<int>(p => _post(() => Progress = p))); }
        catch (Exception) { Downloading = false; _post(() => Status = "The download didn't finish. Try again in a moment."); }
    }
}
