using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Api.Configs;

internal static class ImportNames
{
    /// <summary>
    /// Reads names reserved by interfaces and routing objects.
    /// </summary>
    public static Task<string[]> ReadAsync(AppDbContext db, CancellationToken ct) => db.Configs.Select(one => one.Name)
        .Concat(db.Outbounds.Select(one => one.Name)).Concat(db.Balancers.Select(one => one.Name)).ToArrayAsync(ct);
}
