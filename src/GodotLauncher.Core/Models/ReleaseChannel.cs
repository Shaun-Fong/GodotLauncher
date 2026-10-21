namespace GodotLauncher.Core.Models;

/// <summary>
/// The release channel a Godot build belongs to. Channels are ordered from the
/// least to the most stable so they can be compared directly.
/// </summary>
public enum ReleaseChannel
{
    /// <summary>Development snapshots and alpha builds.</summary>
    Dev = 0,

    /// <summary>Beta builds.</summary>
    Beta = 1,

    /// <summary>Release candidates.</summary>
    Rc = 2,

    /// <summary>Stable releases.</summary>
    Stable = 3,
}

/// <summary>Helpers for turning Godot "flavor" strings into channels.</summary>
public static class ReleaseChannelExtensions
{
    /// <summary>
    /// Classifies a Godot flavor string such as <c>stable</c>, <c>rc2</c>,
    /// <c>beta1</c>, <c>alpha3</c> or <c>dev7</c>.
    /// </summary>
    /// <returns>
    /// The matching channel plus the stage number embedded in the flavor
    /// (<c>rc2</c> yields 2, <c>stable</c> yields 0). Unknown flavors map to
    /// <see cref="ReleaseChannel.Dev"/> with number 0 so a future flavor still
    /// shows up in the list instead of disappearing.
    /// </returns>
    public static (ReleaseChannel Channel, int Number) ClassifyFlavor(string flavor)
    {
        if (string.IsNullOrWhiteSpace(flavor))
        {
            return (ReleaseChannel.Dev, 0);
        }

        var value = flavor.Trim();
        if (value.StartsWith("stable", StringComparison.OrdinalIgnoreCase))
        {
            return (ReleaseChannel.Stable, 0);
        }

        if (value.StartsWith("rc", StringComparison.OrdinalIgnoreCase))
        {
            return (ReleaseChannel.Rc, ReadTrailingNumber(value, "rc"));
        }

        if (value.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
        {
            return (ReleaseChannel.Beta, ReadTrailingNumber(value, "beta"));
        }

        // "devN" and "alphaN" both behave like development builds.
        if (value.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
        {
            return (ReleaseChannel.Dev, ReadTrailingNumber(value, "dev"));
        }

        if (value.StartsWith("alpha", StringComparison.OrdinalIgnoreCase))
        {
            return (ReleaseChannel.Dev, ReadTrailingNumber(value, "alpha"));
        }

        return (ReleaseChannel.Dev, 0);
    }

    /// <summary>Human readable name used in the UI and notifications.</summary>
    public static string ToDisplayName(this ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => "Stable",
        ReleaseChannel.Rc => "RC",
        ReleaseChannel.Beta => "Beta",
        ReleaseChannel.Dev => "Dev",
        _ => channel.ToString(),
    };

    private static int ReadTrailingNumber(string value, string prefix)
    {
        var digits = value.AsSpan(prefix.Length);
        return int.TryParse(digits, out var number) ? number : 0;
    }
}
