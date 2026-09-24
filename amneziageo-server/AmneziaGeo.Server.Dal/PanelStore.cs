using AmneziaGeo.Server.Core.Panel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What saving the settings of the panel produced.
/// </summary>
public enum PanelOutcome
{
    Ok = 0,
    Invalid = 1,
}

/// <summary>
/// The settings a command produced, and why it was refused.
/// </summary>
public sealed record PanelResult(PanelOutcome Outcome, string Code, string Message, PanelSettings? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == PanelOutcome.Ok;

    /// <summary>
    /// Returns the settings a command produced.
    /// </summary>
    public static PanelResult Done(PanelSettings settings) => new(PanelOutcome.Ok, string.Empty, string.Empty, settings);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static PanelResult No(PanelFault fault) => new(PanelOutcome.Invalid, fault.Code, fault.Message, null);
}

/// <summary>
/// Reads and saves the settings of the panel.
/// </summary>
public sealed class PanelStore
{
    private const long Row = 1;

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public PanelStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the settings the panel holds, the ones it starts with when there are none.
    /// </summary>
    public async Task<PanelSettings> ReadAsync(CancellationToken ct)
    {
        var held = await _db.Set<PanelEntity>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == Row, ct)
            .ConfigureAwait(false);

        return held is null ? PanelDefaults.Settings : Read(held);
    }

    /// <summary>
    /// Saves the settings, refusing the ones the rules do not allow.
    /// </summary>
    public async Task<PanelResult> SaveAsync(PanelSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (PanelRules.Check(settings) is { } broken)
        {
            return PanelResult.No(broken);
        }

        var held = await _db.Set<PanelEntity>().FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);
        if (held is null)
        {
            held = new PanelEntity { Id = Row };
            _db.Add(held);
        }

        Write(held, settings);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return PanelResult.Done(Read(held));
    }

    /// <summary>
    /// Writes down the settings the panel started under when it holds none.
    /// </summary>
    public async Task<PanelSettings> SeedAsync(PanelSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var held = await _db.Set<PanelEntity>().FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);
        if (held is not null)
        {
            return Read(held);
        }

        var row = new PanelEntity { Id = Row, UpdatedUtc = _time.GetUtcNow() };
        Write(row, settings);
        _db.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Read(row);
    }

    /// <summary>
    /// Tells whether the services of an endpoint that is turned on answer on a TCP port, before the server is built.
    /// </summary>
    public static bool ServesOn(string path, int port)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                "select count(*) from Configs where IsEnabled = 1 and (ServicesPort = $port or (ServicesPort = 0 and ListenPort = $port))";
            command.Parameters.AddWithValue("$port", port);

            return command.ExecuteScalar() is long count && count > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the settings the database holds before the server is built, null when it holds none.
    /// </summary>
    public static PanelSettings? Held(string path)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                "select Listen, Domains, Port, Path, Certificate, CertificateKey, Language from Panel where Id = " + Row;

            using var reader = command.ExecuteReader();

            return reader.Read()
                ? new PanelSettings
                {
                    Listen = PanelList.Split(reader.GetString(0)),
                    Domains = PanelList.Split(reader.GetString(1)),
                    Port = reader.GetInt32(2),
                    Path = reader.GetString(3),
                    Certificate = reader.GetString(4),
                    CertificateKey = reader.GetString(5),
                    Language = reader.GetString(6),
                }
                : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static PanelSettings Read(PanelEntity row) => new()
    {
        Listen = PanelList.Split(row.Listen),
        Domains = PanelList.Split(row.Domains),
        Port = row.Port,
        Opened = row.Opened,
        Path = row.Path,
        Certificate = row.Certificate,
        CertificateKey = row.CertificateKey,
        Language = row.Language,
        Prereleases = row.Prereleases,
        NameTemplate = row.NameTemplate.Length > 0 ? row.NameTemplate : ConfigName.Default,
    };

    private static void Write(PanelEntity row, PanelSettings settings)
    {
        row.Listen = PanelList.Line(settings.Listen);
        row.Domains = PanelList.Line(settings.Domains);
        row.Port = settings.Port;
        row.Opened = settings.Opened;
        row.Path = settings.Path;
        row.Certificate = settings.Certificate;
        row.CertificateKey = settings.CertificateKey;
        row.Language = settings.Language;
        row.Prereleases = settings.Prereleases;
        row.NameTemplate = settings.NameTemplate;
    }
}
