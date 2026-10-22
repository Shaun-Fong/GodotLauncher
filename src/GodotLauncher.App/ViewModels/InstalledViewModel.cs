using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

/// <summary>Installed page: launches editors and manages their registrations and files.</summary>
public sealed partial class InstalledViewModel : ViewModelBase
{
    private readonly IDialogHost _dialogs;

    public InstalledViewModel(AppServices services, IDialogHost dialogs)
        : base(services)
    {
        _dialogs = dialogs;
        ReloadFromLibrary();
    }

    public ObservableCollection<InstalledItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string SummaryText => string.Format(L.Installed.Count, Items.Count);

    public void ReloadFromLibrary()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ReloadFromLibrary);
            return;
        }

        var selectedId = Items.FirstOrDefault(item => item.IsDefault)?.Installation.Id;
        Items.Clear();
        foreach (var installation in Services.Library.Items
                     .OrderByDescending(item => item.IsDefault)
                     .ThenByDescending(item => item.LastLaunched ?? DateTimeOffset.MinValue)
                     .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Items.Add(new InstalledItemViewModel(Services, this, installation));
        }

        _ = selectedId;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SummaryText));
    }

    protected override void OnLanguageChanged() => ReloadFromLibrary();

    protected override void OnInstallationsChanged() => ReloadFromLibrary();

    public void Launch(InstalledItemViewModel item)
    {
        try
        {
            var result = Services.Launch(item.Installation);
            Services.Log.Info($"Launched {item.Title} (pid {result.ProcessId}).");
            ReloadFromLibrary();
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not launch {item.Title}.", ex);
            _ = ShowErrorAsync(L.Error.Launch, ex.Message);
        }
    }

    public async Task SetDefaultAsync(InstalledItemViewModel item)
    {
        try
        {
            await Services.Library.SetDefaultAsync(item.Installation.Id);
        }
        catch (Exception ex)
        {
            Services.Log.Error("Could not set the default editor.", ex);
            await ShowErrorAsync(L.Error.Generic, ex.Message);
        }
    }

    public async Task TogglePinAsync(InstalledItemViewModel item)
    {
        try
        {
            await Services.Library.SetPinnedAsync(item.Installation.Id, !item.IsPinned);
        }
        catch (Exception ex)
        {
            Services.Log.Error("Could not update the pinned flag.", ex);
            await ShowErrorAsync(L.Error.Generic, ex.Message);
        }
    }

    public async Task RenameAsync(InstalledItemViewModel item)
    {
        var dialog = new DialogViewModel(
            L.Dialog.AliasTitle,
            item.VersionText,
            L.Dialog.Save,
            L.Dialog.Cancel,
            isPrompt: true,
            initialText: item.Installation.Alias,
            placeholder: item.Installation.DisplayName);
        var result = await _dialogs.ShowDialogAsync(dialog);
        if (!result.Confirmed)
        {
            return;
        }

        await Services.Library.SetAliasAsync(item.Installation.Id, result.Text);
    }

    public async Task EditArgumentsAsync(InstalledItemViewModel item)
    {
        var dialog = new DialogViewModel(
            string.Format(L.Dialog.ArgumentsTitle, item.Title),
            L.Dialog.ArgumentsHint,
            L.Dialog.Save,
            L.Dialog.Cancel,
            isPrompt: true,
            initialText: item.Installation.LaunchArguments);
        var result = await _dialogs.ShowDialogAsync(dialog);
        if (!result.Confirmed)
        {
            return;
        }

        await Services.Library.AddOrUpdateAsync(item.Installation with
        {
            LaunchArguments = string.IsNullOrWhiteSpace(result.Text) ? null : result.Text.Trim()
        });
    }

    public void OpenFolder(InstalledItemViewModel item)
    {
        var path = Directory.Exists(item.Installation.InstallPath)
            ? item.Installation.InstallPath
            : Path.GetDirectoryName(item.Installation.ExecutablePath) ?? item.Installation.InstallPath;
        Services.OpenExternally(path);
    }

    public async Task UninstallAsync(InstalledItemViewModel item)
    {
        var dialog = new DialogViewModel(
            string.Format(L.Dialog.UninstallTitle, item.Title),
            L.Dialog.UninstallBody,
            L.Dialog.Delete,
            L.Dialog.Cancel,
            isDanger: true,
            optionText: L.Dialog.DeleteFiles,
            optionValue: item.Installation.Source != InstallSource.LocalImport);
        var result = await _dialogs.ShowDialogAsync(dialog);
        if (!result.Confirmed)
        {
            return;
        }

        try
        {
            var deleteFiles = result.OptionValue && item.Installation.Source != InstallSource.LocalImport;
            await Services.Library.RemoveAsync(item.Installation.Id, deleteFiles);
            Services.Log.Info($"Removed {item.Title} (delete files: {deleteFiles}).");
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not uninstall {item.Title}.", ex);
            await ShowErrorAsync(L.Error.Generic, ex.Message);
        }
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        var path = await FilePickers.PickExecutableAsync(L.Dialog.ImportTitle);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var installation = Services.Installer.RegisterExisting(path);
            await Services.Library.AddOrUpdateAsync(installation);
            Services.Log.Info($"Registered existing editor at {path}.");
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not register '{path}'.", ex);
            await ShowErrorAsync(L.Error.Import, string.Format(L.Dialog.ImportFailed, ex.Message));
        }
    }

    [RelayCommand]
    private async Task CleanPreviewsAsync()
    {
        var candidates = Services.Library.FindPreviewCleanupCandidates();
        if (candidates.Count == 0)
        {
            await ShowInfoAsync(L.Installed.CleanPreviews, L.Installed.CleanPreviewsNone);
            return;
        }

        var confirmed = await _dialogs.ShowDialogAsync(new DialogViewModel(
            L.Installed.CleanPreviews,
            string.Join(Environment.NewLine, candidates.Select(item => item.DisplayName)),
            L.Dialog.Delete,
            L.Dialog.Cancel,
            isDanger: true));
        if (!confirmed.Confirmed)
        {
            return;
        }

        foreach (var installation in candidates)
        {
            await Services.Library.RemoveAsync(installation.Id, deleteFiles: true);
        }

        await ShowInfoAsync(
            L.Installed.CleanPreviews,
            string.Format(L.Installed.CleanPreviewsDone, candidates.Count));
    }

    private Task ShowInfoAsync(string title, string message) =>
        _dialogs.ShowDialogAsync(new DialogViewModel(title, message, L.Dialog.Ok, L.Dialog.Cancel, canCancel: false));

    private Task ShowErrorAsync(string title, string message) =>
        _dialogs.ShowDialogAsync(new DialogViewModel(title, message, L.Dialog.Ok, L.Dialog.Cancel, canCancel: false));
}
