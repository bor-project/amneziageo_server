namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Reads the name a client asks for in the head of an HTTP request.
/// </summary>
public static class HttpHead
{
    /// <summary>
    /// The most bytes of a head the reader looks through.
    /// </summary>
    public const int Most = 8192;

    private static readonly string[] Methods =
        ["GET ", "POST ", "HEAD ", "PUT ", "DELETE ", "OPTIONS ", "PATCH ", "CONNECT ", "TRACE "];

    /// <summary>
    /// Reads the name out of what a client sent first over TCP: the request line and the headers.
    /// </summary>
    public static SeenName Read(ReadOnlySpan<byte> stream)
    {
        if (!Starts(stream, out var known))
        {
            return known ? SeenName.More : SeenName.None;
        }

        var at = stream.IndexOf((byte)'\n');
        while (at >= 0 && at + 1 < stream.Length)
        {
            var line = stream[(at + 1)..];
            var end = line.IndexOf((byte)'\n');
            if (line[0] is (byte)'\r' or (byte)'\n')
            {
                return SeenName.None;
            }

            if (line.Length >= 5 && (line[0] | 0x20) == 'h' && (line[1] | 0x20) == 'o' && (line[2] | 0x20) == 's'
                && (line[3] | 0x20) == 't' && line[4] == ':')
            {
                return end < 0 ? SeenName.More : Host(line[5..end]);
            }

            at = end < 0 ? -1 : at + 1 + end;
        }

        return stream.Length >= Most ? SeenName.None : SeenName.More;
    }

    // Tells whether the bytes start the way a request does; known is false once they cannot.
    private static bool Starts(ReadOnlySpan<byte> stream, out bool known)
    {
        foreach (var method in Methods)
        {
            var shared = Math.Min(method.Length, stream.Length);
            var same = true;
            for (var at = 0; at < shared && same; at++)
            {
                same = stream[at] == method[at];
            }

            if (same)
            {
                known = true;

                return shared == method.Length;
            }
        }

        known = false;

        return false;
    }

    // Returns the host of the header without the port and the spaces around it.
    private static SeenName Host(ReadOnlySpan<byte> value)
    {
        var host = value.Trim((byte)' ').TrimEnd((byte)'\r').Trim((byte)' ');
        if (host.Length > 0 && host[0] == (byte)'[')
        {
            return SeenName.None;
        }

        var colon = host.LastIndexOf((byte)':');

        return SeenName.Of(colon < 0 ? host : host[..colon]);
    }
}
