namespace AmneziaGeo.Server.Tests;

/// <summary>
/// Leaves a test unfinished when the host cannot carry it.
/// </summary>
public static class Skip
{
    /// <summary>
    /// Ends the calling test quietly unless a condition holds.
    /// </summary>
    public static void IfNot(bool condition)
    {
        if (!condition)
        {
            throw new SkipException();
        }
    }
}

/// <summary>
/// Raised to end a test the host cannot carry.
/// </summary>
public sealed class SkipException : Exception;
