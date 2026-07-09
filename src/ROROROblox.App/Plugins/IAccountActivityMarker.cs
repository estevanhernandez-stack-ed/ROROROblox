namespace ROROROblox.App.Plugins;

/// <summary>Host-side sink for a plugin's MarkAccountActive RPC. Parses the plugin-facing
/// stringified account id and credits the Core activity monitor. Mirrors
/// <see cref="IActivitySnapshotProvider"/>'s adapter role.</summary>
public interface IAccountActivityMarker
{
    /// <summary>Credit the account (RoRoRo's stringified Guid) as active now. No-op on an
    /// unparseable or untracked id.</summary>
    void Mark(string accountId);
}
