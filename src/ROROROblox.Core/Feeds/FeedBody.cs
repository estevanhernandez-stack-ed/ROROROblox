namespace ROROROblox.Core.Feeds;

/// <summary>
/// Reads an anonymous feed's body up to a cap. Streams, so an oversized body is never buffered whole; a
/// declared Content-Length over the cap is refused before a byte is read. Null means "too large", which
/// each caller reports as a shape failure.
/// </summary>
internal static class FeedBody
{
    internal static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
