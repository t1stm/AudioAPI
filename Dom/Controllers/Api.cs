using Microsoft.AspNetCore.Mvc;

namespace Dom.Controllers;

/// <summary>
///     The two things every endpoint in Dom does: find the caller's token, and answer with the error
///     envelope the rest of the stack uses. One copy, so the two controllers cannot drift apart.
/// </summary>
internal static class Api
{
    /// <summary>The token from an <c>Authorization: Bearer …</c> header, if there is one.</summary>
    public static string? Bearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    public static JsonResult Error(int status, string code, string message) =>
        new(new { error = new { code, message } }) { StatusCode = status };
}
