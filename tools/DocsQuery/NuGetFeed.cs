using System.Net;
using System.Text.Json;

namespace DocsQuery;

/// <summary>
/// The versions nuget.org lists for a package, from its flat container (<c>v3-flatcontainer/{id}/index.json</c>), for
/// <c>due --nuget</c>. The one command that reaches the network, and only when asked; nothing in CI asks.
/// </summary>
internal static class NuGetFeed
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Every version of <paramref name="packageId"/>, or null where nuget.org has no package of that id.</summary>
    public static IReadOnlyList<string>? Versions(string packageId)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(packageId.ToLowerInvariant())}/index.json";

        using var response = Http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(response.Content.ReadAsStream());
        return [.. document.RootElement.GetProperty("versions").EnumerateArray().Select(version => version.GetString() ?? "")];
    }
}
