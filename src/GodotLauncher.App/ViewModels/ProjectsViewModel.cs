using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Launch;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Projects;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

/// <summary>Projects page: search, create, import and per-project editor selection.</summary>
public sealed partial class ProjectsViewModel : ViewModelBase
{
    private readonly IDialogHost _dialogs;
    private readonly List<ProjectRecord> _records = [];

    public ProjectsViewModel(AppServices services, IDialogHost dialogs)
        : base(services)
    {
        _dialogs = dialogs;
        services.ProjectChanged += (_, _) => Reload();
        Reload();
    }

    /// <summary>Visible rows after the search filter.</summary>
    public ObservableCollection<ProjectItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => Items.Count > 0;

    public string EmptyText => _records.Count == 0 ? L.Projects.Empty : L.Projects.NoResults;

    public string EmptyHintText => _records.Count == 0 ? L.Projects.EmptyHint : L.Projects.NoResultsHint;

    public string CountText => string.Format(L.Projects.Count, _records.Count);

    [RelayCommand]
    private async Task CreateAsync()
    {
        var parent = await FilePickers.PickFolderAsync(L.Projects.CreateFolder);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        var dialog = new DialogViewModel(
            title: L.Projects.CreateTitle,
            message: L.Projects.CreateName,
            primaryText: L.Projects.Create,
            cancelText: L.Dialog.Cancel,
            isPrompt: true,
            initialText: L.Projects.CreateDefaultName,
            placeholder: L.Projects.CreateName);

        var answer = await _dialogs.ShowDialogAsync(dialog);
        if (!answer.Confirmed || string.IsNullOrWhiteSpace(answer.Text))
        {
            return;
        }

        var name = answer.Text.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await ShowErrorAsync(L.Projects.CreateTitle, L.Projects.InvalidName);
            return;
        }

        var directory = Path.Combine(parent, name);
        if (File.Exists(Path.Combine(directory, GodotProjectFile.FileName)))
        {
            await ShowErrorAsync(L.Projects.CreateTitle, L.Projects.CreateFailed);
            return;
        }

        var defaultEditor = Services.Library.FindDefault();
        VersionNumber? version = defaultEditor is not null && VersionNumber.TryParse(defaultEditor.VersionId, out var parsed)
            ? parsed
            : null;

        try
        {
            var project = ProjectCreator.Create(directory, name, version);
            var record = ProjectRecord.FromProject(project, defaultEditor?.Id);
            await Services.Projects.AddOrUpdateAsync(record);
            Services.Log.Info($"Created project '{directory}'.");
            await ShowInfoAsync(L.Projects.CreateTitle, string.Format(L.Projects.CreatedBody, record.Name));
        }
        catch (Exception ex) when (ex is ProjectException or IOException or UnauthorizedAccessException)
        {
            Services.Log.Error($"Could not create project '{directory}'.", ex);
            await ShowErrorAsync(L.Projects.CreateTitle, ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var picked = await FilePickers.PickProjectFileAsync(L.Projects.ImportTitle);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        // The picker returns project.godot itself; the store keys projects by folder.
        var directory = Directory.Exists(picked) ? picked : Path.GetDirectoryName(picked);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        await ImportDirectoryAsync(directory);
    }

    /// <summary>Adds a project that already exists on disk.</summary>
    public async Task ImportDirectoryAsync(string directory)
    {
        var file = Path.Combine(directory, GodotProjectFile.FileName);
        if (!File.Exists(file))
        {
            await ShowErrorAsync(L.Projects.ImportTitle, L.Projects.ImportFailed);
            return;
        }

        GodotProjectFile project;
        try
        {
            project = GodotProjectFile.Load(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Log.Error($"Could not read '{file}'.", ex);
            await ShowErrorAsync(L.Projects.ImportTitle, ex.Message);
            return;
        }

        var existing = Services.Projects.Find(directory);
        var editor = Services.FindEditorForProject(ProjectRecord.FromProject(project, existing?.EditorId));
        var record = existing is null
            ? ProjectRecord.FromProject(project, editor?.Id)
            : existing with
            {
                Name = project.DisplayName,
                VersionText = project.VersionText,
                EditorId = existing.EditorId ?? editor?.Id,
            };

        await Services.Projects.AddOrUpdateAsync(record);
        Services.Log.Info($"Imported project '{record.Path}' (Godot {project.VersionText ?? "unknown"}).");

        if (project.Version is not null && editor is null)
        {
            await _dialogs.ShowDialogAsync(new DialogViewModel(
                title: L.Projects.VersionMissingTitle,
                message: string.Format(L.Projects.VersionMissingBody, project.VersionText),
                primaryText: L.Dialog.Ok,
                cancelText: L.Dialog.Cancel,
                canCancel: false));
        }
    }

    /// <summary>Opens the project with the editor selected in its row.</summary>
    public void Launch(ProjectItemViewModel item)
    {
        var editor = item.SelectedEditor?.Installation;
        if (editor is null)
        {
            ShowVersionWarning(item);
            return;
        }

        try
        {
            Services.LaunchProject(item.Project, editor);
            Services.Log.Info($"Project '{item.Project.Name}' opened.");
        }
        catch (LaunchException ex)
        {
            Services.Log.Error($"Could not open project '{item.Project.Path}'.", ex);
            _ = ShowErrorAsync(L.Projects.OpenFailed, ex.Message);
        }
    }

    /// <summary>Removes a project from the list without touching the disk.</summary>
    public async void RemoveAsync(ProjectItemViewModel item) =>
        await Services.Projects.RemoveAsync(item.Project.Path, deleteFromDisk: false);

    /// <summary>Deletes the project folder after confirmation.</summary>
    public async void DeleteFromDiskAsync(ProjectItemViewModel item)
    {
        var answer = await _dialogs.ShowDialogAsync(new DialogViewModel(
            title: L.Projects.DeleteTitle,
            message: string.Format(L.Projects.DeleteBody, item.Project.Name),
            primaryText: L.Dialog.Delete,
            cancelText: L.Dialog.Cancel,
            isDanger: true));

        if (!answer.Confirmed)
        {
            return;
        }

        var removed = await Services.Projects.RemoveAsync(item.Project.Path, deleteFromDisk: true);
        if (removed)
        {
            Services.Log.Info($"Deleted project '{item.Project.Path}'.");
        }
    }

    /// <summary>Re-reads the project file and updates name and version.</summary>
    public async void ReloadInfoAsync(ProjectItemViewModel item)
    {
        var file = Path.Combine(item.Project.Path, GodotProjectFile.FileName);
        if (!File.Exists(file))
        {
            await ShowErrorAsync(L.Projects.ReloadTitle, L.Projects.MissingFile);
            return;
        }

        try
        {
            var project = GodotProjectFile.Load(file);
            await Services.Projects.UpdateFromProjectAsync(project);
            Services.Log.Info($"Reloaded project '{item.Project.Path}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(L.Projects.ReloadTitle, ex.Message);
        }
    }

    /// <summary>Explains why a project cannot be opened yet.</summary>
    public void ShowVersionWarning(ProjectItemViewModel item)
    {
        _ = _dialogs.ShowDialogAsync(new DialogViewModel(
            title: L.Projects.VersionMissingTitle,
            message: item.Project.VersionText is { Length: > 0 } version
                ? string.Format(L.Projects.VersionMissingBody, version)
                : L.Projects.VersionUnknownBody,
            primaryText: L.Dialog.Ok,
            cancelText: L.Dialog.Cancel,
            canCancel: false));
    }

    protected override void OnLanguageChanged() => Reload();

    protected override void OnInstallationsChanged() => Reload();

    private void Reload()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
            return;
        }

        _records.Clear();
        _records.AddRange(Services.Projects.Items);

        var filter = SearchText.Trim();
        var filtered = filter.Length == 0
            ? _records
            : _records.Where(record =>
                record.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                record.Path.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

        // Reuse rows so the editor picker keeps its state while typing.
        var existing = Items.ToDictionary(item => item.Project.Path, StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (var record in filtered)
        {
            if (existing.TryGetValue(record.Path, out var row))
            {
                row.Update(record);
                Items.Add(row);
            }
            else
            {
                Items.Add(new ProjectItemViewModel(Services, this, record));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptyHintText));
    }

    partial void OnSearchTextChanged(string value) => Reload();

    protected override void OnSettingsChanged()
    {
        // Listing is independent of settings, but the empty hint switches with the language.
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptyHintText));
    }

    private Task ShowInfoAsync(string title, string message) =>
        _dialogs.ShowDialogAsync(new DialogViewModel(
            title,
            message,
            Services.Loc.Dialog.Ok,
            Services.Loc.Dialog.Cancel,
            canCancel: false));

    private Task ShowErrorAsync(string title, string message) => ShowInfoAsync(title, message);
}
