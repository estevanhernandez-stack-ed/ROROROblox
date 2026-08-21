using ROROROblox.Core.Discord;
using Xunit;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The owner's contract (F-013 prerequisite). Every test here pins a guarantee the old
/// two-writers-serialized-by-modality design could not make: mutations compose instead of racing,
/// a reader sees a mute the instant the mutate completes, and a failed persist changes nothing
/// anywhere. The modality comment this replaces said it plainly — "it is the modality, not the
/// code, that makes this safe today." This class is the code making it safe instead.
/// </summary>
public class DiscordConfigServiceTests
{
    /// <summary>
    /// A store the tests control: park a save mid-flight, fail on demand, and show what landed.
    /// </summary>
    private sealed class ScriptedStore : IDiscordConfigStore
    {
        public DiscordConfig Stored { get; set; } = new();

        /// <summary>When set, the next SaveAsync awaits this before landing.</summary>
        public TaskCompletionSource? HoldSave { get; set; }

        public Exception? FailSaveWith { get; set; }

        public int SaveCount { get; private set; }

        public Task<DiscordConfig> LoadAsync() => Task.FromResult(Stored);

        public async Task SaveAsync(DiscordConfig config)
        {
            if (HoldSave is { } hold)
            {
                HoldSave = null;
                await hold.Task;
            }

            if (FailSaveWith is { } ex) throw ex;
            SaveCount++;
            Stored = config;
        }
    }

    [Fact]
    public async Task InterleavedMutations_BothLand()
    {
        // The lost update this owner exists to kill: writer A's save is still in flight when
        // writer B starts. Under the old snapshot design B reads pre-A state and A's edit is
        // erased. Under the gate, B waits, reads A's published record, and both edits survive.
        var store = new ScriptedStore();
        var service = new DiscordConfigService(store);
        var accountId = Guid.NewGuid();

        var parkedSave = new TaskCompletionSource();
        store.HoldSave = parkedSave;

        var mute = service.MutateAsync(c => c with { MutedAccountIds = [accountId] });
        var webhook = service.MutateAsync(c => c with { MineWebhookUrl = "https://discord.com/api/webhooks/1/x" });

        Assert.False(mute.IsCompleted); // parked inside its save — the interleave is real
        parkedSave.SetResult();
        await mute;
        await webhook;

        Assert.Equal([accountId], store.Stored.MutedAccountIds);
        Assert.Equal("https://discord.com/api/webhooks/1/x", store.Stored.MineWebhookUrl);
        Assert.Equal(store.Stored, service.Current);
    }

    [Fact]
    public async Task AMutation_IsVisibleToASynchronousReader_TheMomentItCompletes()
    {
        // The dispatcher reads Current per dispatch. The old design had a second cache the row
        // mute never wrote, so a context-menu mute was invisible to alert routing until a
        // Preferences close or a restart. One slot, one write path: nothing to forget.
        var store = new ScriptedStore();
        var service = new DiscordConfigService(store);
        var accountId = Guid.NewGuid();

        await service.MutateAsync(c => c with { MutedAccountIds = [accountId] });

        Assert.Contains(accountId, service.Current.MutedAccountIds);
    }

    [Fact]
    public async Task AFailedPersist_PublishesNothingAndRaisesNothing()
    {
        var store = new ScriptedStore { Stored = new DiscordConfig { PresenceEnabled = true } };
        var service = new DiscordConfigService(store);
        await service.InitializeAsync();

        store.FailSaveWith = new IOException("disk says no");
        var changedFired = false;
        service.Changed += (_, _) => changedFired = true;

        await Assert.ThrowsAsync<IOException>(
            () => service.MutateAsync(c => c with { PresenceEnabled = false }));

        Assert.True(service.Current.PresenceEnabled); // unchanged — disk and memory agree
        Assert.False(changedFired);

        // And the gate is released: the next mutation goes through.
        store.FailSaveWith = null;
        await service.MutateAsync(c => c with { JoinEnabled = true });
        Assert.True(service.Current.JoinEnabled);
    }

    [Fact]
    public async Task Changed_FiresOncePerMutation_InWriteOrder_WithThePublishedRecord()
    {
        var store = new ScriptedStore();
        var service = new DiscordConfigService(store);
        var seen = new List<DiscordConfig>();
        service.Changed += (_, config) => seen.Add(config);

        await service.MutateAsync(c => c with { PresenceEnabled = true });
        await service.MutateAsync(c => c with { JoinEnabled = true });

        Assert.Equal(2, seen.Count);
        Assert.True(seen[0].PresenceEnabled);
        Assert.False(seen[0].JoinEnabled);
        Assert.True(seen[1].JoinEnabled);
        Assert.True(seen[1].PresenceEnabled); // second event carries the first edit too
    }

    [Fact]
    public async Task Initialize_LoadsTheStoredRecord_AndRaisesNoEvent()
    {
        var store = new ScriptedStore { Stored = new DiscordConfig { PresenceEnabled = true } };
        var service = new DiscordConfigService(store);
        var changedFired = false;
        service.Changed += (_, _) => changedFired = true;

        await service.InitializeAsync();

        Assert.True(service.Current.PresenceEnabled);
        Assert.False(changedFired);
    }

    [Fact]
    public async Task AMutationBeforeInitialize_ReadsTheDiskFirst_AndWipesNothing()
    {
        // A startup-ordering mistake must not be a data wipe: mutating an owner nobody
        // initialized composes against what is on disk, not against defaults.
        var store = new ScriptedStore
        {
            Stored = new DiscordConfig
            {
                PresenceEnabled = true,
                MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
            },
        };
        var service = new DiscordConfigService(store);

        await service.MutateAsync(c => c with { JoinEnabled = true });

        Assert.True(store.Stored.PresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/tok", store.Stored.MineWebhookUrl);
        Assert.True(store.Stored.JoinEnabled);
    }

    [Fact]
    public async Task ANullReturningMutation_IsRejected_AndPersistsNothing()
    {
        var store = new ScriptedStore();
        var service = new DiscordConfigService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MutateAsync(_ => null!));

        Assert.Equal(0, store.SaveCount);
    }
}
