using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;

namespace GodotLauncher.App.Localization;

/// <summary>A language that ships with the launcher.</summary>
public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// Loads every localization file embedded in the assembly. Adding a language is a matter
/// of dropping a new JSON file into <c>Localization/</c> with the same keys: everything
/// else (language picker, fallbacks) is derived from the files themselves.
/// </summary>
public sealed class LocalizationService
{
    /// <summary>Preference value that follows the operating system language.</summary>
    public const string SystemLanguage = "system";

    private const string DefaultLanguage = "en";

    private static readonly Uri LocalizationFolder = new("avares://GodotLauncher/Localization/");

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs;
    private readonly List<LanguageOption> _languages;

    private LocalizationService(
        Dictionary<string, IReadOnlyDictionary<string, string>> catalogs,
        List<LanguageOption> languages)
    {
        _catalogs = catalogs;
        _languages = languages;
    }

    /// <summary>Languages discovered on disk, sorted by code.</summary>
    public IReadOnlyList<LanguageOption> Languages => _languages;

    /// <summary>Loads every JSON catalog embedded as an Avalonia resource.</summary>
    public static LocalizationService Load(Action<string, Exception?>? reportError = null)
    {
        var catalogs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var languages = new List<LanguageOption>();

        foreach (var uri in EnumerateCatalogUris())
        {
            try
            {
                using var stream = AssetLoader.Open(uri);
                using var document = JsonDocument.Parse(stream);

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        values[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }

                var code = values.TryGetValue("Language.Code", out var explicitCode) &&
                           !string.IsNullOrWhiteSpace(explicitCode)
                    ? explicitCode
                    : Path.GetFileNameWithoutExtension(uri.AbsolutePath);

                var name = values.TryGetValue("Language.Name", out var nativeName) &&
                           !string.IsNullOrWhiteSpace(nativeName)
                    ? nativeName
                    : code;

                if (catalogs.TryAdd(code, values))
                {
                    languages.Add(new LanguageOption(code, name));
                }
            }
            catch (Exception ex)
            {
                reportError?.Invoke($"Could not load the localization file '{uri}'.", ex);
            }
        }

        if (catalogs.Count == 0)
        {
            // Never leave the UI without any strings: fall back to returning keys verbatim.
            catalogs[DefaultLanguage] = new Dictionary<string, string>(StringComparer.Ordinal);
            languages.Add(new LanguageOption(DefaultLanguage, "English"));
        }

        languages.Sort(static (left, right) => string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase));
        return new LocalizationService(catalogs, languages);
    }

    /// <summary>Resolves a preference ("system", "en", "zh-CN", …) to a catalog.</summary>
    public Strings LoadStrings(string? preference)
    {
        var code = ResolveCode(preference);
        return new Strings(_catalogs[code]);
    }

    /// <summary>Resolves a preference to the language code that will actually be used.</summary>
    public string ResolveCode(string? preference)
    {
        if (!string.IsNullOrWhiteSpace(preference) &&
            !string.Equals(preference, SystemLanguage, StringComparison.OrdinalIgnoreCase) &&
            _catalogs.ContainsKey(preference))
        {
            return _catalogs.Keys.First(key => string.Equals(key, preference, StringComparison.OrdinalIgnoreCase));
        }

        return MatchCulture(CultureInfo.CurrentUICulture) ??
               MatchCulture(CultureInfo.CurrentCulture) ??
               FindDefaultCode();
    }

    private string? MatchCulture(CultureInfo culture)
    {
        if (culture.Name.Length > 0 && _catalogs.ContainsKey(culture.Name))
        {
            return _catalogs.Keys.First(key => string.Equals(key, culture.Name, StringComparison.OrdinalIgnoreCase));
        }

        var language = culture.TwoLetterISOLanguageName;
        return _languages
            .OrderByDescending(option => string.Equals(option.Code, language, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(option =>
                string.Equals(option.Code, language, StringComparison.OrdinalIgnoreCase) ||
                option.Code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
            ?.Code;
    }

    private string FindDefaultCode()
    {
        if (_catalogs.ContainsKey(DefaultLanguage))
        {
            return _catalogs.Keys.First(key => string.Equals(key, DefaultLanguage, StringComparison.OrdinalIgnoreCase));
        }

        return _languages[0].Code;
    }

    private static IEnumerable<Uri> EnumerateCatalogUris()
    {
        var uris = new List<Uri>();
        try
        {
            uris.AddRange(AssetLoader.GetAssets(LocalizationFolder, null));
        }
        catch (Exception)
        {
            // The folder may not be registered on some builds; probing known files below.
        }

        // Asset enumeration can be unavailable on trimmed builds: always probe the two
        // catalogs that ship with the app as a safety net.
        foreach (var known in new[] { "en.json", "zh-CN.json" })
        {
            var uri = new Uri(LocalizationFolder, known);
            if (!uris.Contains(uri) && AssetLoader.Exists(uri))
            {
                uris.Add(uri);
            }
        }

        return uris.Where(static uri => uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }
}
