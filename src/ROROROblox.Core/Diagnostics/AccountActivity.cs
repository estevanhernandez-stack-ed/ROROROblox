using System;
namespace ROROROblox.Core.Diagnostics;
public readonly record struct AccountActivity(Guid AccountId, DateTimeOffset LastActivityAt, TimeSpan SinceActivity);
