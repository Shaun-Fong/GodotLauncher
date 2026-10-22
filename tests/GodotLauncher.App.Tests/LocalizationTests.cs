using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotLauncher.App.Localization;

namespace GodotLauncher.App.Tests;

/// <summary>
/// Guards the JSON catalogs: a missing key or a stale translation would otherwise surface as a
/// dotted key or the wrong language inside the app.
/// </summary>
public sealed class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);

    private static string CatalogPath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Localization", file);

    private static Dictionary<string, string> ReadCatalog(string file)
    {
        var path = CatalogPath(file);
        Assert.True(File.Exists(path), $"Missing localization catalog at {path}.");

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            Assert.False(values.ContainsKey(property.Name), $"{file} defines '{property.Name}' twice.");
            values[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return values;
    }

    [Fact]
    public void Catalogs_DefineTheSameKeys()
    {
        var english = ReadCatalog("en.json");
        var chinese = ReadCatalog("zh-CN.json");

        var missingInChinese = english.Keys.Except(chinese.Keys, StringComparer.Ordinal).OrderBy(key => key).ToList();
        var missingInEnglish = chinese.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(key => key).ToList();

        Assert.True(missingInChinese.Count == 0, "Only in en.json: " + string.Join(", ", missingInChinese));
        Assert.True(missingInEnglish.Count == 0, "Only in zh-CN.json: " + string.Join(", ", missingInEnglish));
    }

    [Fact]
    public void Catalogs_HaveNoEmptyValues()
    {
        foreach (var file in new[] { "en.json", "zh-CN.json" })
        {
            var catalog = ReadCatalog(file);
            var empty = catalog.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();
            Assert.True(empty.Count == 0, $"{file} has empty values: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Catalogs_KeepTheSamePlaceholders()
    {
        var english = ReadCatalog("en.json");
        var chinese = ReadCatalog("zh-CN.json");

        foreach (var (key, value) in english)
        {
            var expected = Placeholder.Matches(value).Select(match => match.Value).Distinct().Order().ToList();
            var actual = Placeholder.Matches(chinese[key]).Select(match => match.Value).Distinct().Order().ToList();
            Assert.True(
                expected.SequenceEqual(actual),
                $"'{key}' uses placeholders [{string.Join(", ", expected)}] in en.json but [{string.Join(", ", actual)}] in zh-CN.json.");
        }
    }

    [Fact]
    public void Strings_GroupsResolveEveryKeyInBothLanguages()
    {
        foreach (var file in new[] { "en.json", "zh-CN.json" })
        {
            var catalog = ReadCatalog(file);
            var strings = new Strings(catalog);

            foreach (var groupProperty in typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (groupProperty.GetValue(strings) is not StringGroup group)
                {
                    continue;
                }

                foreach (var property in group.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.PropertyType != typeof(string))
                    {
                        continue;
                    }

                    var key = $"{groupProperty.Name}.{property.Name}";
                    Assert.True(
                        catalog.TryGetValue(key, out var value),
                        $"{file} is missing '{key}' (exposed by {group.GetType().Name}.{property.Name}).");
                    Assert.False(
                        string.IsNullOrWhiteSpace(value),
                        $"{key} in {file} is blank.");
                }
            }
        }
    }

    [Fact]
    public void Languages_ExposeTheirOwnDisplayNames()
    {
        foreach (var file in new[] { "en.json", "zh-CN.json" })
        {
            var catalog = ReadCatalog(file);
            Assert.True(catalog.TryGetValue("Language.Code", out var code), $"{file} has no Language.Code.");
            Assert.True(catalog.TryGetValue("Language.Name", out var name), $"{file} has no Language.Name.");
            Assert.False(string.IsNullOrWhiteSpace(code));
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
