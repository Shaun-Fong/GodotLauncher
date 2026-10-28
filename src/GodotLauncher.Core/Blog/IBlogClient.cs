using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Blog;

/// <summary>Raised when the blog page cannot be read or does not look like the expected markup.</summary>
public sealed class BlogException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>One page of the blog archive.</summary>
public sealed record BlogPage
{
    /// <summary>One-based page number this result came from.</summary>
    public required int Number { get; init; }

    /// <summary>Posts in display order.</summary>
    public required IReadOnlyList<BlogPost> Posts { get; init; }

    /// <summary>True when the page links to a newer page.</summary>
    public bool HasNext { get; init; }
}

/// <summary>Reads the official Godot blog archive.</summary>
public interface IBlogClient
{
    /// <summary>Downloads and parses one page; page 1 is the newest.</summary>
    Task<BlogPage> GetPageAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Finds the number of the last page by probing from the newest page.</summary>
    Task<int> GetLastPageAsync(CancellationToken cancellationToken = default);
}
