using System.Collections.Generic;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MutexContestedWatcherTests
{
    private sealed class FakeMutex : IMutexHolder
    {
        public bool Held;
        public bool HeldElsewhere;
        public string MutexName => @"Local\fake";
        public bool IsHeld => Held;
        public bool IsHeldElsewhere() => HeldElsewhere;
        public bool Acquire() { Held = true; return true; }
        public void Release() { Held = false; }
        public event System.EventHandler? MutexLost { add { } remove { } }
    }

    [Fact]
    public void Poll_ContestedWhenNotHeldAndHeldElsewhere_FiresTrueOnce()
    {
        var mutex = new FakeMutex { Held = false, HeldElsewhere = true };
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();
        watcher.Poll(); // still contested → no repeat (edge-triggered)

        Assert.Equal(new[] { true }, fires);
    }

    [Fact]
    public void Poll_WhileWeHoldIt_NeverContested()
    {
        var mutex = new FakeMutex { Held = true, HeldElsewhere = true }; // held-elsewhere ignored while we hold
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();

        Assert.Empty(fires);
    }

    [Fact]
    public void Poll_ContestedThenFreed_FiresTrueThenFalse()
    {
        var mutex = new FakeMutex { Held = false, HeldElsewhere = true };
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();                    // true
        mutex.HeldElsewhere = false;
        watcher.Poll();                    // false (re-arm)
        mutex.HeldElsewhere = true;
        watcher.Poll();                    // true again

        Assert.Equal(new[] { true, false, true }, fires);
    }
}
