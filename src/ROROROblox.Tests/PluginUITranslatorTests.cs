using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.Tests;

public class PluginUITranslatorTests
{
    [Fact]
    public void AddTrayMenuItem_DispatchesToHost_AndAssignsHandle()
    {
        var host = new FakePluginUIHost();
        var translator = new PluginUITranslator(host);

        var handle = translator.AddTrayMenuItem("626labs.test", new MenuItemSpec
        {
            Label = "Toggle auto-keys",
            Tooltip = "Start or stop the cycler",
            Enabled = true,
        });

        Assert.NotEmpty(handle.Id);
        Assert.Single(host.AddedMenuItems);
        Assert.Equal("Toggle auto-keys", host.AddedMenuItems[0].label);
    }

    [Fact]
    public void RemoveUI_DispatchesToHost_AndForgetsHandle()
    {
        var host = new FakePluginUIHost();
        var translator = new PluginUITranslator(host);
        var handle = translator.AddTrayMenuItem("626labs.test", new MenuItemSpec { Label = "x" });

        translator.RemoveUI("626labs.test", handle);

        Assert.Single(host.RemovedHandles);
        Assert.Equal(handle.Id, host.RemovedHandles[0]);
    }

    [Fact]
    public void RemoveUI_IgnoresWhenOwnerMismatch()
    {
        var host = new FakePluginUIHost();
        var translator = new PluginUITranslator(host);
        var handle = translator.AddTrayMenuItem("626labs.a", new MenuItemSpec { Label = "x" });

        translator.RemoveUI("626labs.b", handle); // wrong owner

        Assert.Empty(host.RemovedHandles);
    }

    private sealed class FakePluginUIHost : IPluginUIHost
    {
        public List<(string pluginId, string label)> AddedMenuItems { get; } = new();
        public List<string> RemovedHandles { get; } = new();
        private int _nextId = 1;
        public string AddTrayMenuItem(string pluginId, string label, string? tooltip, bool enabled, Action onClick)
        {
            var id = $"handle-{_nextId++}";
            AddedMenuItems.Add((pluginId, label));
            return id;
        }
        public string AddRowBadge(string pluginId, string text, string? colorHex, string? tooltip)
            => $"handle-{_nextId++}";
        public string AddStatusPanel(string pluginId, string title, string bodyMarkdown)
            => $"handle-{_nextId++}";
        public void Update(string handle, string newLabel) { }
        public void Remove(string handle) => RemovedHandles.Add(handle);
    }
}
