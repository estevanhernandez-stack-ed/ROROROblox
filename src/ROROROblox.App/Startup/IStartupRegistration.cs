namespace ROROROblox.App.Startup;

public interface IStartupRegistration
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
