namespace Cofre.Core.Security;

/// <summary>Exact HTTPS origins. No title matching, suffix matching or implicit subdomain trust.</summary>
public static class SiteOrigin
{
    public static string? Normalize(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo.Length != 0 || uri.Host.Length == 0) return null;
        return $"https://{uri.IdnHost.ToLowerInvariant()}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}";
    }

    public static bool Matches(string savedUrl, string requestedUrl)
    {
        var origin = Normalize(savedUrl);
        return origin is not null && origin == Normalize(requestedUrl);
    }
}
