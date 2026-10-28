namespace GodotLauncher.Core.Models;

/// <summary>One entry of the official Godot blog, as shown on the blog page.</summary>
public sealed record BlogPost
{
    /// <summary>Headline of the post.</summary>
    public required string Title { get; init; }

    /// <summary>Absolute address opened in the browser.</summary>
    public required string Url { get; init; }

    /// <summary>Author name, empty when the page did not expose one.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Release date as published, for example "25 October 2026".</summary>
    public string DateText { get; init; } = string.Empty;

    /// <summary>Short teaser shown under the title.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Cover image address, when the post has one.</summary>
    public string? ImageUrl { get; init; }
}
