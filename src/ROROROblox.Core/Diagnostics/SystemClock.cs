using System;
namespace ROROROblox.Core.Diagnostics;
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
