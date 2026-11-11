namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Carries the trusted TLS identity of endpoint services.
/// </summary>
public sealed record ServiceTlsIdentity(string Name, string Pin);
