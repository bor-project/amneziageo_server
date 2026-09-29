namespace AmneziaGeo.Server.Core.Diagnostics;

/// <summary>
/// One record of the log of the panel.
/// </summary>
public sealed record JournalEntry(DateTimeOffset Time, string Level, string Category, string Message, string Fault);
