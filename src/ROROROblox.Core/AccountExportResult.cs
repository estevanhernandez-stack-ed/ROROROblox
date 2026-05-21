using ROROROblox.Core.Transport;

namespace ROROROblox.Core;

/// <summary>
/// Result of a bulk export read (<see cref="IAccountStore.ExportAccountsAsync"/>). Carries the
/// built <see cref="AccountExportRecord"/>s (cookies decrypted via the existing DPAPI path) plus
/// the ids that were requested but could not be exported because they have no
/// <see cref="Account.RobloxUserId"/> — the merge key on import requires a real userId, so an
/// account without one cannot travel. The UI surfaces <see cref="SkippedNoUserId"/> as a warning
/// ("these accounts can't be exported until they've been launched / resolved at least once").
/// </summary>
/// <param name="Records">Export records for every requested id that had a non-null userId.</param>
/// <param name="SkippedNoUserId">Requested ids dropped because their userId is null.</param>
public sealed record AccountExportResult(
    IReadOnlyList<AccountExportRecord> Records,
    IReadOnlyList<Guid> SkippedNoUserId);
