using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Projects;

namespace GodotLauncher.App.ViewModels;

/// <summary>An installed editor offered in the per-project version picker.</summary>
public sealed record EditorOption(InstalledVersion Installation, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>One project row: the project itself, its editor picker and its actions.</summary>
public sealed partial class ProjectItemViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ProjectsViewModel _owner;
    private bool _suppressPersist;

    public ProjectItemViewModel(AppServices services, ProjectsViewModel owner, ProjectRecord project)
    {
        _services = services;
        _owner = owner;
        Project = project;

        Editors = new ObservableCollection<EditorOption>(
            services.Library.Items.Select(installation => new EditorOption(installation, Describe(installation))));

        _suppressPersist = true;
        SelectedEditor = ResolveEditor();
        _suppressPersist = false;
    }

    /// <summary>The stored project record.</summary>
    public ProjectRecord Project { get; private set; }

    /// <summary>Installed editors offered by the picker.</summary>
    public ObservableCollection<EditorOption> Editors { get; }

    public string Title => Project.Name;

    public string PathText => Project.Path;

    public Strings L => _services.Loc;

    /// <summary>Editor version recorded by the project file, e.g. "Godot 4.3".</summary>
    public string VersionText => Project.VersionText is { Length: > 0 } version
        ? $"Godot {version}"
        : L.Projects.UnknownVersion;

    /// <summary>Explains that the expected editor version is missing.</summary>
    public string WarningText
    {
        get
        {
            if (SelectedEditor is not null || !Project.TryGetVersion(out var version))
            {
                return string.Empty;
            }

            return string.Format(L.Projects.MissingVersion, Project.VersionText ?? version.ToString());
        }
    }

    public bool HasWarning => WarningText.Length > 0;

    public bool HasEditors => Editors.Count > 0;

    public bool CanLaunch => true;

    [ObservableProperty]
    private EditorOption? _selectedEditor;

    /// <summary>True while the owner is loading the project list.</summary>
    public bool IsMissing => !Project.Exists;

    partial void OnSelectedEditorChanged(EditorOption? value)
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(HasWarning));
        LaunchCommand.NotifyCanExecuteChanged();

        if (_suppressPersist)
        {
            return;
        }

        _ = _services.Projects.SetEditorAsync(Project.Path, value?.Installation.Id);
    }

    /// <summary>Refreshes the row after the record or the installation list changed.</summary>
    public void Update(ProjectRecord project)
    {
        Project = project;

        var editors = _services.Library.Items.Select(installation => new EditorOption(installation, Describe(installation))).ToList();
        Editors.Clear();
        foreach (var editor in editors)
        {
            Editors.Add(editor);
        }

        _suppressPersist = true;
        SelectedEditor = ResolveEditor();
        _suppressPersist = false;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(HasEditors));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Launch() => _owner.Launch(this);

    [RelayCommand]
    private void Remove() => _owner.RemoveAsync(this);

    [RelayCommand]
    private void DeleteFromDisk() => _owner.DeleteFromDiskAsync(this);

    [RelayCommand]
    private void OpenFolder() => _services.OpenExternally(Project.Path);

    [RelayCommand]
    private void ReloadInfo() => _owner.ReloadInfoAsync(this);

    private EditorOption? ResolveEditor()
    {
        if (Project.EditorId is { Length: > 0 } editorId)
        {
            var stored = Editors.FirstOrDefault(
                editor => string.Equals(editor.Installation.Id, editorId, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                return stored;
            }
        }

        if (Project.TryGetVersion(out var projectVersion))
        {
            EditorOption? best = null;
            VersionNumber bestVersion = default;
            foreach (var editor in Editors)
            {
                if (!VersionNumber.TryParse(editor.Installation.VersionId, out var version) ||
                    version.Major != projectVersion.Major ||
                    version.Minor != projectVersion.Minor)
                {
                    continue;
                }

                if (best is null ||
                    version.CompareTo(bestVersion) > 0 ||
                    (version.CompareTo(bestVersion) == 0 &&
                     editor.Installation.VersionId.EndsWith("-stable", StringComparison.OrdinalIgnoreCase) &&
                     !best.Installation.VersionId.EndsWith("-stable", StringComparison.OrdinalIgnoreCase)))
                {
                    best = editor;
                    bestVersion = version;
                }
            }

            return best;
        }

        var fallback = _services.Library.FindDefault();
        return fallback is null
            ? Editors.FirstOrDefault()
            : Editors.FirstOrDefault(editor => editor.Installation.Id == fallback.Id) ?? Editors.FirstOrDefault();
    }

    private static string Describe(InstalledVersion installation) =>
        $"{installation.DisplayName}{(installation.Edition == GodotEdition.Mono ? " · .NET" : string.Empty)} · {installation.Architecture.ToDisplayName()}";
}
