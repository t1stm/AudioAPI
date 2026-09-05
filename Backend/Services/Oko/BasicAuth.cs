using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Oko;

/// <summary>
///     The operator's side of the door: HTTP Basic against <c>ADMIN_USERNAME</c> / <c>ADMIN_PASSWORD</c>.
/// </summary>
/// <remarks>
///     Basic rather than a login page because the browser already implements the whole flow — the
///     challenge, the prompt, the credential cache, replaying it on every request. That last part is
///     load-bearing here: <c>EventSource</c> cannot set an <c>Authorization</c> header, so a scheme the
///     browser does not replay by itself would leave the live feed unauthenticated or unreachable.
///     <para>
///         What Basic does not do is hide the password from the wire. ADMIN_PLAN.md says it and it is
///         worth repeating: put TLS in front of this, or reach it through an SSH tunnel.
///     </para>
/// </remarks>
public static class BasicAuth
{
    /// <summary>
    ///     Whether this request carries the operator's credentials. Both fields are compared in fixed
    ///     time — the username is as guessable as the password when the answer arrives sooner for a
    ///     longer shared prefix.
    /// </summary>
    public static bool Matches(string? header, string username, string password)
    {
        if (string.IsNullOrEmpty(header)) return false;
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)) return false;
        if (!"Basic".Equals(parsed.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (parsed.Parameter is null) return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        // Split on the first colon only: a password may contain one, a username may not.
        var separator = decoded.IndexOf(':');
        if (separator < 0) return false;

        return Equal(decoded[..separator], username) & Equal(decoded[(separator + 1)..], password);
    }

    private static bool Equal(string given, string expected)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}
