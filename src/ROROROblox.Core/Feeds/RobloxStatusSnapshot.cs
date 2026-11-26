namespace ROROROblox.Core.Feeds;

/// <summary>One (group id, row id) pair and its Status.io code. Ids and an integer: no text from the page is kept.</summary>
public readonly record struct StatusCell(string ComponentId, string ContainerId, int Code);

public sealed record RobloxStatusSnapshot(IReadOnlyList<StatusCell> Cells);
