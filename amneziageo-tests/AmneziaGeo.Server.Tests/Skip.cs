namespace AmneziaGeo.Server.Tests;

/// <summary>
/// Leaves a test unfinished when the host cannot carry it.
/// </summary>
public static class Skip
{
    /// <summary>
    /// Ends the calling test as skipped unless a condition holds.
    /// </summary>
    public static void IfNot(bool condition)
    {
        Assert.SkipUnless(condition, "the host cannot carry this test");
    }
}
