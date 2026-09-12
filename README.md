# CPE.BlazorConnectionStatus

Connectivity monitoring for Blazor WebAssembly that answers the question an offline-first app
actually asks: **can I reach the server?**

`navigator.onLine` answers a different one. It reports whether the machine has a network adapter
attached, so a laptop on hotel Wi-Fi that never reaches the internet, a VPN that dropped, and an API
that is down all read as "online". This library combines the browser's own `online`/`offline`
events with an optional HTTP `HEAD` probe on an interval, and reports the combination.

Targets `net8.0` and `net10.0`. MIT.

**Versioning:** 0.x while the API settles. It is complete and tested, but no application has shipped
on it yet — breaking changes will land in a minor bump rather than be smuggled into a patch. 1.0.0
once it has run in a real app for a while.

## Install

```
dotnet add package CPE.BlazorConnectionStatus
```

There is no script tag to add. The library imports its own ES module.

## Use

```csharp
// Program.cs
builder.Services.AddBlazorConnectionStatus(options =>
{
    options.PingUrl = "https://api.example.com/healthz";
    options.PingInterval = TimeSpan.FromSeconds(30);
    options.PingTimeout = TimeSpan.FromSeconds(5);
});
```

```razor
@implements IAsyncDisposable
@inject IConnectionStatusMonitor Connection

<span class="badge @(Connection.IsOnline ? "bg-success" : "bg-danger")">
    @Connection.State
</span>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        Connection.StatusChanged += OnStatusChanged;
        await Connection.StartAsync();
    }

    private void OnStatusChanged(object? sender, ConnectionState state)
        => InvokeAsync(StateHasChanged);

    public async ValueTask DisposeAsync()
    {
        Connection.StatusChanged -= OnStatusChanged;
        await Connection.DisposeAsync();
    }
}
```

Before something that must not be half-done offline:

```csharp
if (await Connection.CheckNowAsync() != ConnectionState.Online)
{
    // queue it instead
}
```

## API

| Member | Notes |
| --- | --- |
| `ConnectionState State` | `Unknown`, `Online` or `Offline`. `Unknown` until the first observation. |
| `bool IsOnline` | True only for `Online`. `Unknown` reads as false — read `State` if that matters. |
| `event EventHandler<ConnectionState> StatusChanged` | Raised on change only, never on a repeat. |
| `Task StartAsync(CancellationToken)` | Loads the module and takes a first observation. Idempotent. |
| `Task<ConnectionState> CheckNowAsync(CancellationToken)` | Probes now and completes with the answer. |
| `ValueTask DisposeAsync()` | Tears down the timer, listeners and module. |

### Options

| Option | Default | Notes |
| --- | --- | --- |
| `PingUrl` | `null` | Leave null to trust browser events alone — weaker, but no traffic. Any 2xx counts as reachable. |
| `PingInterval` | 30s | The only thing that notices a connection that stopped reaching the server. |
| `PingTimeout` | 5s | Must be shorter than `PingInterval`, or probes overlap. Validated at registration. |

`AddBlazorConnectionStatus` throws `ArgumentException` at startup for an unusable combination,
rather than failing silently inside a browser later.

## Things worth knowing

**Nothing touches JavaScript until `StartAsync`.** Construction and DI registration are safe during
prerendering; `StartAsync` belongs in `OnAfterRenderAsync`, not a constructor.

**The monitor is `IAsyncDisposable` only.** It owns a JS handle, so there is no correct synchronous
teardown. Blazor disposes scopes asynchronously, so this is a non-issue in an app — but a host that
calls a container's synchronous `Dispose()` while holding a resolved monitor will get
`InvalidOperationException: ... only implements IAsyncDisposable`. Use `DisposeAsync`.

**The probe needs CORS if it is cross-origin.** A `HEAD` to another origin without the right
headers fails, and a failed probe means offline. Point `PingUrl` at your own API, and allow `HEAD`.

**Probes are cache-defeating.** Each request appends a `_cs=<timestamp>` parameter and sends
`cache: 'no-store'`, so a service worker or proxy cannot answer "reachable" on the server's behalf.

## Migrating from Blazor.ConnectionStatusDetector

This is a clean break, not a drop-in replacement.

| Before | Now |
| --- | --- |
| `<script src="_content/Blazor.ConnectionStatusDetector/connection.js">` in `index.html` | Delete it. The module loads itself. |
| `IConnectionStatusDetectorService` | `IConnectionStatusMonitor` |
| `bool IsOnline` only | `ConnectionState State` with a real `Unknown`, plus `IsOnline` |
| Interop started in the constructor | Explicit `StartAsync()` |
| Wait for the next interval | `CheckNowAsync()` |
| `window.Connection` global | ES module, one instance per monitor |

## Development

```
dotnet test          # the state machine, both target frameworks
npm install --include=dev
npm test             # the ES module, under jsdom with fetch stubbed
```

The C# tests fake the JS boundary; the vitest suite drives the real module with a stubbed `fetch`,
including timeout, error status, network failure and adapter-drop paths. The boundary between them
is `IConnectionStatusInterop` — the only part that needs a real browser.
