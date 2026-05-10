using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins;

/// <summary>
/// IHostedService that owns the Kestrel-hosted gRPC server on a per-user named pipe.
/// Started from App.OnStartup (item 15); torn down on App.OnExit. The pipe ACL is
/// inherited from the current Windows user's session, providing per-user isolation
/// per spec — a different Windows user on the same machine cannot connect.
///
/// Uses <see cref="WebApplication.CreateSlimBuilder"/> rather than CreateBuilder
/// because RoRoRo isn't an ASP.NET app: the slim builder skips the appsettings.json
/// + default logging stack we don't need, keeping startup time tight.
///
/// The pipe name is configurable so tests can use a unique-per-test name and avoid
/// clashes with any production server running on the same dev box.
/// </summary>
public sealed class PluginHostStartupService : IHostedService, IAsyncDisposable
{
    public const string DefaultPipeName = "rororo-plugin-host";

    private readonly PluginHostService _hostService;
    private readonly CapabilityInterceptor _interceptor;
    private readonly ILogger<PluginHostStartupService> _log;
    private readonly string _pipeName;
    private WebApplication? _webApp;

    public PluginHostStartupService(
        PluginHostService hostService,
        CapabilityInterceptor interceptor,
        ILogger<PluginHostStartupService> log,
        string? pipeName = null)
    {
        _hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _pipeName = pipeName ?? DefaultPipeName;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Register the singletons gRPC will resolve.
        builder.Services.AddSingleton(_hostService);
        builder.Services.AddSingleton(_interceptor);
        builder.Services.AddGrpc(o =>
        {
            o.Interceptors.Add<CapabilityInterceptor>();
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenNamedPipe(_pipeName, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        _webApp = builder.Build();
        _webApp.MapGrpcService<PluginHostService>();
        await _webApp.StartAsync(cancellationToken).ConfigureAwait(false);
        _log.LogInformation("PluginHost gRPC server listening on \\\\.\\pipe\\{Pipe}", _pipeName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApp is not null)
        {
            try
            {
                await _webApp.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await _webApp.DisposeAsync().ConfigureAwait(false);
                _webApp = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp is not null)
        {
            await _webApp.DisposeAsync().ConfigureAwait(false);
            _webApp = null;
        }
    }
}
