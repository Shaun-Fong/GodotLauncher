using System.Net;
using System.Text.RegularExpressions;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Blog;

/// <summary>
/// Scrapes the public blog archive at https://godotengine.org/blog/. The site renders a fixed
/// <c>article-card</c> block per post, so the parser keys off that markup and fails loudly when the
/// page no longer contains it instead of silently showing an empty list.
/// </summary>
public sealed partial class BlogClient(HttpClient http, string baseUrl = "https://godotengine.org/blog/") : IBlogClient
{
    /// <summary>Base address of the newest page.</summary>
    public const string BaseUrl = "https://godotengine.org/blog/";

    /// <summary>Guards the binary search against a redirect loop or a runaway page number.</summary>
    private const int MaximumPage = 512;

    private readonly string _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

    private Uri SiteRoot => new(_baseUrl);

    public async Task<BlogPage> GetPageAsync(int page, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);

        var html = await GetHtmlAsync(PageUrl(page), cancellationToken).ConfigureAwait(false)
                   ?? throw new BlogException($"Blog page {page} does not exist.");
        var posts = ParsePosts(html, SiteRoot);
        if (posts.Count == 0)
        {
            throw new BlogException("The blog page did not contain any posts.");
        }

        return new BlogPage
        {
            Number = page,
            Posts = posts,
            HasNext = html.Contains("pagination-next", StringComparison.Ordinal)
        };
    }

    public async Task<int> GetLastPageAsync(CancellationToken cancellationToken = default)
    {
        var low = 1;
        var high = MaximumPage;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (await PageExistsAsync(middle, cancellationToken).ConfigureAwait(false))
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Extracts the cards from one archive page; visible for tests.</summary>
    public static IReadOnlyList<BlogPost> ParsePosts(string html, Uri? siteRoot = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        siteRoot ??= new Uri(BaseUrl);

        var posts = new List<BlogPost>();
        foreach (Match match in CardRegex().Matches(html))
        {
            var card = match.Groups["card"].Value;
            var title = Clean(TitleRegex().Match(card));
            if (title.Length == 0)
            {
                continue;
            }

            posts.Add(new BlogPost
            {
                Title = title,
                Url = AbsoluteUrl(match.Groups["url"].Value, siteRoot),
                Author = Clean(AuthorRegex().Match(card)),
                DateText = CleanReleaseDate(Clean(DateRegex().Match(card))),
                Summary = Clean(ExcerptRegex().Match(card)),
                ImageUrl = ReadImageUrl(card, siteRoot)
            });
        }

        return posts;
    }

    private async Task<string?> GetHtmlAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BlogException($"The blog answered with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PageExistsAsync(int page, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(PageUrl(page), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BlogException($"The blog answered with HTTP {(int)response.StatusCode}.");
        }

        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new BlogException("Could not reach the Godot blog.", ex);
        }
    }

    private Uri PageUrl(int page) => page == 1 ? new Uri(_baseUrl) : new Uri($"{_baseUrl}{page}/");

    private static string AbsoluteUrl(string href, Uri siteRoot)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(siteRoot, href).ToString();
    }

    private static string? ReadImageUrl(string card, Uri siteRoot)
    {
        var match = ImageRegex().Match(card);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim().Trim('\'', '"');
        return value.Length == 0 ? null : AbsoluteUrl(value, siteRoot);
    }

    private static string Clean(Match match)
    {
        if (!match.Success)
        {
            return string.Empty;
        }

        var text = TagRegex().Replace(match.Groups["value"].Value, " ");
        text = WebUtility.HtmlDecode(text);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static string CleanReleaseDate(string value)
    {
        // Cards render "&nbsp;-&nbsp;25 October 2026"; keep only the date part.
        return value.Trim('\u00a0', ' ', '-', '–', '—', '·', '|').Trim();
    }

    [GeneratedRegex("<a\\s+href=\"(?<url>/article/[^\"]+)\"[^>]*>\\s*<article class=\"article-card\">(?<card>.*?)</article>\\s*</a>", RegexOptions.Singleline)]
    private static partial Regex CardRegex();

    [GeneratedRegex("<h3>(?<value>.*?)</h3>", RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<p class=\"excerpt\">(?<value>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex ExcerptRegex();

    [GeneratedRegex("<span class=\"by\">(?<value>.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex AuthorRegex();

    [GeneratedRegex("<span class=\"date\">(?<value>.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex DateRegex();

    [GeneratedRegex("background-image:url\\((?<value>[^)]+)\\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
