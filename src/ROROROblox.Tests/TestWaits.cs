namespace ROROROblox.Tests;

/// <summary>
/// Shared bounds for "wait until this actually happens" in tests.
///
/// <para><b>Why this exists.</b> Three separate tests failed CI on 2026-08-13 for the same reason,
/// on the night of a release: a liveness bound of 2s that is generous on a desktop and marginal on
/// a CI runner roughly five times slower. Each looked like an isolated flake. The third one made it
/// a family, so it gets one home and one number instead of six literals drifting apart.</para>
///
/// <para><b>What a liveness bound is, and is not.</b> It exists so a wedged component FAILS the test
/// instead of hanging the run forever. It is not an assertion about speed. Nothing that waits this
/// long is measuring anything — so the only outcome a tight value can buy is a false failure on a
/// slow machine, which is exactly what it bought.</para>
///
/// <para><b>Do not use this where elapsed time is the assertion.</b> <c>AppStorageDefenderTests</c>
/// has two tests that assert a completion landed after one bound and before another; their waits are
/// deliberately left short and separate. Widening a wait that a later <c>Assert</c> depends on would
/// let a genuinely slow completion pass the check written to catch it.</para>
/// </summary>
internal static class TestWaits
{
    /// <summary>
    /// How long to wait for something that should happen almost immediately, before declaring it
    /// wedged. Long enough for the slowest CI runner observed, short enough that a real hang still
    /// ends the test rather than the job timeout.
    /// </summary>
    public static readonly TimeSpan Liveness = TimeSpan.FromSeconds(15);
}
