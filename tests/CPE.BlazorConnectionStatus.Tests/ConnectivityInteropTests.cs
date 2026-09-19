using System.Text.Json;
using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus.Tests;

/// <summary>
/// The C# half of the JavaScript seam: what <see cref="ConnectivityInterop"/> actually sends.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this project replaces the interop with <see cref="FakeConnectivityInterop"/>,
/// so the options were only ever followed as far as the fake - one layer short of where they were
/// turned into something serializable. That layer is where 0.10.0 broke: an anonymous type that
/// serialized in Debug and threw <c>ConstructorContainsNullParameterNames</c> in every trimmed
/// Release publish, leaving the monitor stuck at its initial state.
/// </para>
/// <para>
/// A unit test cannot trim, so it cannot reproduce that exact failure. What it can pin is the
/// shape that makes the failure impossible - a dictionary of primitives - and the wire contract
/// with <c>connection-status.js</c>, which reads exactly three keys.
/// </para>
/// </remarks>
public class ConnectivityInteropTests
{
    // Mirrors what JSRuntime uses, so the serialization asserted here is the one that happens.
    private static readonly JsonSerializerOptions JsInteropLike = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly ConnectivityOptions Configured = new()
    {
        PingUrl = "/health",
        PingInterval = TimeSpan.FromSeconds(30),
        PingTimeout = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public void Start_arguments_are_a_dictionary_of_primitives_so_trimming_cannot_break_them()
    {
        object arguments = ConnectivityInterop.CreateArguments(Configured);

        // If this fails because someone "tidied" the dictionary back into an anonymous type,
        // a record or a class: read CreateArguments' remarks first. Each of those serializes
        // in a test and fails in a trimmed publish, one loudly and one silently.
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, object?>>(arguments);
        Assert.All(dictionary.Values, value =>
            Assert.True(value is null or string or int,
                $"'{value?.GetType().Name}' is not a primitive the trimmer is guaranteed to keep."));
    }

    [Fact]
    public void Start_arguments_serialize_to_exactly_the_keys_the_js_module_reads()
    {
        var json = JsonSerializer.Serialize<object>(ConnectivityInterop.CreateArguments(Configured), JsInteropLike);

        // connection-status.js create(): settings.pingUrl, settings.pingIntervalMs,
        // settings.pingTimeoutMs. A renamed key here would be read as undefined there and silently
        // replaced by the module's default - a ping URL of null means no probe at all.
        Assert.Equal("{\"pingUrl\":\"/health\",\"pingIntervalMs\":30000,\"pingTimeoutMs\":5000}", json);
    }

    [Fact]
    public void A_null_ping_url_is_sent_as_null_which_the_module_treats_as_browser_events_only()
    {
        var json = JsonSerializer.Serialize<object>(
            ConnectivityInterop.CreateArguments(new ConnectivityOptions()), JsInteropLike);

        Assert.Contains("\"pingUrl\":null", json);
    }

    [Fact]
    public async Task StartAsync_imports_the_module_and_passes_the_arguments_to_create()
    {
        var runtime = new RecordingJSRuntime();
        await using var interop = new ConnectivityInterop(runtime);
        var monitor = new ConnectivityMonitor(new FakeConnectivityInterop(), Configured);
        using var callback = DotNetObjectReference.Create(monitor);

        await interop.StartAsync(callback, Configured);

        Assert.Equal("import", runtime.Calls.Single().Identifier);

        var create = runtime.Module.Calls.Single(c => c.Identifier == "create");
        Assert.Same(callback, create.Args[0]);

        // The same instance CreateArguments builds - proving StartAsync goes through it rather
        // than constructing its own object inline, which is how the anonymous type got in.
        var sent = Assert.IsAssignableFrom<IDictionary<string, object?>>(create.Args[1]);
        Assert.Equal("/health", sent["pingUrl"]);
        Assert.Equal(30000, sent["pingIntervalMs"]);
        Assert.Equal(5000, sent["pingTimeoutMs"]);
    }

    /// <summary>Hands back a recording module for <c>import</c> and records every call.</summary>
    private sealed class RecordingJSRuntime : IJSRuntime
    {
        public RecordingJSObjectReference Module { get; } = new();

        public List<(string Identifier, object?[] Args)> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args ?? Array.Empty<object?>()));
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    /// <summary>Records calls, and returns another recording reference wherever one is expected.</summary>
    private sealed class RecordingJSObjectReference : IJSObjectReference
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args ?? Array.Empty<object?>()));
            return typeof(TValue) == typeof(IJSObjectReference)
                ? ValueTask.FromResult((TValue)(object)new RecordingJSObjectReference())
                : ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
