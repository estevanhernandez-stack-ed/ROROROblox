namespace ROROROblox.Core;

/// <summary>
/// Guards the one place a session cookie becomes an HTTP header. <c>Headers.Add("Cookie", v)</c> with a
/// CR, LF or other control character in <c>v</c> throws a <see cref="FormatException"/> whose message
/// echoes the whole value, so a cookie that carries one must be refused before it reaches a header, and
/// refused with a message that names no value.
/// </summary>
public static class CookieValidation
{
    /// <summary>True when the cookie contains any character in <c>'\0'..'\u001f'</c>.</summary>
    public static bool HasControlCharacters(string? cookie)
    {
        if (cookie is null)
        {
            return false;
        }
        foreach (var c in cookie)
        {
            if (c <= '\u001f')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Throws a value-free <see cref="ArgumentException"/> when the cookie holds a control character.</summary>
    public static void ThrowIfControlCharacters(string? cookie, string paramName)
    {
        if (HasControlCharacters(cookie))
        {
            throw new ArgumentException("Cookie must not contain control characters.", paramName);
        }
    }
}
