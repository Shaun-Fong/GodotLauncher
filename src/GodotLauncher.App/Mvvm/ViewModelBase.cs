using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Services;

namespace GodotLauncher.App.Mvvm;

/// <summary>
/// Base class for view models that expose localized strings and marshal change notifications.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(AppServices services)
    {
        Services = services;
        services.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Text));
            OnLanguageChanged();
        };
        services.SettingsChanged += (_, _) => OnSettingsChanged();
        services.InstallationChanged += (_, _) => OnInstallationsChanged();
    }

    protected AppServices Services { get; }

    /// <summary>Active localized strings, exposed for compiled bindings in XAML.</summary>
    public Strings Text => Services.Loc;

    protected Strings L => Services.Loc;

    protected virtual void OnLanguageChanged()
    {
    }

    protected virtual void OnSettingsChanged()
    {
    }

    protected virtual void OnInstallationsChanged()
    {
    }
}

public sealed record DialogResult(bool Confirmed, string? Text, bool OptionValue = true)
{
    public static DialogResult Cancel { get; } = new(false, null);
}

/// <summary>
/// Describes a modal asked by a view model. The view binds an overlay to it while it is set.
/// </summary>
public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<DialogResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogViewModel(
        string title,
        string message,
        string primaryText,
        string cancelText,
        bool isDanger = false,
        bool canCancel = true,
        bool isPrompt = false,
        string? initialText = null,
        string? placeholder = null,
        string? optionText = null,
        bool optionValue = true)
    {
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        CancelText = cancelText;
        IsDanger = isDanger;
        CanCancel = canCancel;
        IsPrompt = isPrompt;
        InputText = initialText;
        Placeholder = placeholder;
        OptionText = optionText;
        OptionValue = optionValue;
    }

    public string Title { get; }

    public string Message { get; }

    public string PrimaryText { get; }

    public string CancelText { get; }

    public bool IsDanger { get; }

    public bool CanCancel { get; }

    public bool IsPrompt { get; }

    public string? Placeholder { get; }

    public string? OptionText { get; }

    public bool HasOption => !string.IsNullOrWhiteSpace(OptionText);

    [ObservableProperty]
    private bool _optionValue;

    [ObservableProperty]
    private string? _inputText;

    public Task<DialogResult> Completion => _completion.Task;

    [RelayCommand]
    private void Confirm() => _completion.TrySetResult(new DialogResult(true, InputText, OptionValue));

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult(DialogResult.Cancel);

    internal void Dismiss() => _completion.TrySetResult(DialogResult.Cancel);
}

/// <summary>Implemented by the shell so pages can ask the window for a modal dialog.</summary>
public interface IDialogHost
{
    Task<DialogResult> ShowDialogAsync(DialogViewModel dialog);
}

/// <summary>
/// Holds the active window so view models can open native file and folder pickers
/// without depending on the view layer directly.
/// </summary>
public static class FilePickers
{
    public static TopLevel? Host { get; set; }

    public static async Task<string?> PickExecutableAsync(string title)
    {
        var storage = Host?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }]
                : null
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickProjectFileAsync(string title)
    {
        var storage = Host?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Godot project") { Patterns = ["project.godot"] }]
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickFolderAsync(string title)
    {
        var storage = Host?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
