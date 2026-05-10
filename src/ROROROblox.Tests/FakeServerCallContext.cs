using Grpc.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Minimal ServerCallContext fake shared across plugin-host gRPC tests.
/// Grpc.Core.Testing.TestServerCallContext exists, but pulling that whole
/// package in just for a no-op context is heavier than this 20-line stub.
/// The host RPCs do not read most context fields in v1, but the
/// CapabilityInterceptor reads <c>Method</c> to look up the required
/// capability — hence the <see cref="Create(string)"/> overload.
/// </summary>
internal sealed class FakeServerCallContext : ServerCallContext
{
    private readonly string _method;

    private FakeServerCallContext(string method)
    {
        _method = method;
    }

    public static FakeServerCallContext Create() => new("Test");
    public static FakeServerCallContext Create(string method) => new(method);

    protected override string MethodCore => _method;
    protected override string HostCore => "test";
    protected override string PeerCore => "peer";
    protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
    protected override Metadata RequestHeadersCore => new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new("anonymous", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        => throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
