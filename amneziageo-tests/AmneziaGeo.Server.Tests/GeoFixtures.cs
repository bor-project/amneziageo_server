using System.Net;
using System.Text;
using AmneziaGeo.Server.Geo.Files;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// Writes v2ray geoip and geosite files the tests read back.
/// </summary>
public static class GeoBuilder
{
    /// <summary>
    /// Builds a geoip database out of country codes and their ranges.
    /// </summary>
    public static byte[] Ip(params (string Country, string[] Cidrs)[] entries)
    {
        var file = new MemoryStream();
        foreach (var (country, cidrs) in entries)
        {
            var entry = new MemoryStream();
            Text(entry, 1, country);
            foreach (var cidr in cidrs)
            {
                var parts = cidr.Split('/');
                var range = new MemoryStream();
                Bytes(range, 1, IPAddress.Parse(parts[0]).GetAddressBytes());
                Varint(range, 2, ulong.Parse(parts[1]));
                Bytes(entry, 2, range.ToArray());
            }

            Bytes(file, 1, entry.ToArray());
        }

        return file.ToArray();
    }

    /// <summary>
    /// Builds a geosite database out of category codes and their domains, each written as a suffix.
    /// </summary>
    public static byte[] Site(params (string Category, string[] Domains)[] entries)
    {
        var file = new MemoryStream();
        foreach (var (category, domains) in entries)
        {
            var entry = new MemoryStream();
            Text(entry, 1, category);
            foreach (var domain in domains)
            {
                var one = new MemoryStream();
                Varint(one, 1, 2);
                Text(one, 2, domain);
                Bytes(entry, 2, one.ToArray());
            }

            Bytes(file, 1, entry.ToArray());
        }

        return file.ToArray();
    }

    private static void Text(Stream stream, int field, string value) =>
        Bytes(stream, field, Encoding.UTF8.GetBytes(value));

    private static void Bytes(Stream stream, int field, byte[] value)
    {
        Tag(stream, field, 2);
        Number(stream, (ulong)value.Length);
        stream.Write(value, 0, value.Length);
    }

    private static void Varint(Stream stream, int field, ulong value)
    {
        Tag(stream, field, 0);
        Number(stream, value);
    }

    private static void Tag(Stream stream, int field, int wireType) =>
        Number(stream, ((ulong)field << 3) | (uint)wireType);

    private static void Number(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}

/// <summary>
/// A file store held in memory.
/// </summary>
public sealed class MemoryGeoFiles : IGeoFileStore
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Puts a file in the store.
    /// </summary>
    public void Put(string name, byte[] data) => _files[name] = data;

    /// <summary>
    /// Tells whether the store carries a file.
    /// </summary>
    public bool Has(string name) => _files.ContainsKey(name);

    /// <inheritdoc/>
    public Stream? OpenRead(string name) => _files.TryGetValue(name, out var data) ? new MemoryStream(data) : null;

    /// <inheritdoc/>
    public long Size(string name) => _files.TryGetValue(name, out var data) ? data.Length : 0;

    /// <inheritdoc/>
    public Task WriteAsync(string name, byte[] data, CancellationToken ct = default)
    {
        _files[name] = data;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Remove(string name) => _files.Remove(name);
}
