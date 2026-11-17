namespace ROROROblox.Core;

/// <summary>Why <c>RORORO_DATA_DIR</c> was or was not used. See <see cref="AppDataRoot.Decide"/>.</summary>
public enum DataRootDecisionReason
{
    /// <summary>The variable is null, empty or whitespace: nothing to apply, nothing to say.</summary>
    Unset,

    /// <summary>The process is MSIX-packaged or laid out by Velopack. Shipped builds ignore the variable.</summary>
    NotADevBuild,

    /// <summary>The value is not a fully qualified absolute path. A relative one would depend on the working directory.</summary>
    NotAnAbsolutePath,

    /// <summary>A dev build with an absolute path: <see cref="DataRootDecision.Folder"/> is the folder to use.</summary>
    Used,
}

/// <summary>
/// The outcome of the override decision: the folder to use (only when <see cref="Reason"/> is
/// <see cref="DataRootDecisionReason.Used"/>, otherwise null) and why.
/// </summary>
public sealed record DataRootDecision(DataRootDecisionReason Reason, string? Folder)
{
    /// <summary>No override. Also what the helper reports before anything has configured it.</summary>
    public static DataRootDecision Unset { get; } = new(DataRootDecisionReason.Unset, null);
}
