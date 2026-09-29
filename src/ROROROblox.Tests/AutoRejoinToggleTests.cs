using System.IO;
using System.Linq;
using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Part B auto-rejoin, task 5 — <see cref="MainViewModel.ToggleAutoRejoinAsync"/>. Pins the two
/// load-bearing facts: an alt's flip persists, and the main can never be flagged even if something
/// calls the method directly (the context-menu item being hidden for the main is UI-layer only —
/// this is the rule's actual enforcement point). Mirrors <see cref="MainViewModelTests.Build"/>'s
/// real-<c>AccountStore</c>-over-a-temp-file harness.
/// </summary>
public class AutoRejoinToggleTests
{
    [Fact]
    public async Task Toggle_OnAnAlt_FlipsAndPersists()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c"));
            alt.IsMain = false; // first AddAsync into a fresh store auto-promotes to main; override for the VM's view (MainViewModelTests.TryResolveMainFriendSource_NoMain_ReturnsNull does the same)
            vm.Accounts.Add(alt);
            await vm.ToggleAutoRejoinAsync(alt);
            Assert.True(alt.AutoRejoin);
            Assert.True((await store.ListAsync()).Single(a => a.Id == alt.Id).AutoRejoin);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Toggle_OnTheMain_DoesNothing()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            var main = new AccountSummary(await store.AddAsync("Main", "", "c")) { IsMain = true };
            vm.Accounts.Add(main);
            await vm.ToggleAutoRejoinAsync(main);
            Assert.False(main.AutoRejoin);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
