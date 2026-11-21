using System.Security.Cryptography;
using System.Text;

namespace Workbench.Service;

public static class SessionTokens
{
    public const string CookieName = "wb_session";

    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static bool IsAcceptable(string? token)
        => token is { Length: >= 16 and <= 128 } && token.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-');

    public static bool Equals(string provided, string expected)
    {
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static bool Authorized(HttpContext context, string expected)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.Ordinal) && Equals(header["Bearer ".Length..].Trim(), expected))
            return true;
        return context.Request.Cookies.TryGetValue(CookieName, out var cookie) && cookie is not null && Equals(cookie, expected);
    }
}
