namespace ROROROblox.Core;

/// <summary>
/// Result of a merge import (<see cref="IAccountStore.ImportMergeAsync"/>). Merge is by Roblox
/// userId — records whose userId is not already present locally are added; records whose userId
/// already exists are skipped (the existing local account is kept untouched). The UI reports this
/// back to the user verbatim: "Imported {Imported} accounts. Skipped {Skipped} already on this PC."
/// </summary>
/// <param name="Imported">Count of records added to the local store.</param>
/// <param name="Skipped">Count of records skipped because their userId already exists locally.</param>
public sealed record ImportMergeResult(int Imported, int Skipped);
