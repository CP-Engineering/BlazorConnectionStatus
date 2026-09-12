# CPE.BlazorConnectionStatus

Connectivity monitoring for Blazor WebAssembly that answers the question an offline-first app
actually asks: **can I reach the server?**

`navigator.onLine` answers a different one. It reports whether the machine has a network adapter
attached, so a laptop on hotel Wi-Fi that never reaches the internet, a VPN that dropped, and an API
that is down all read as "online". This library combines the browser's own `online`/`offline`
events with an optional HTTP `HEAD` probe on an interval, and reports the combination.

Targets `net8.0` and `net10.0`. MIT.

**Versioning:** 0.x while the API settles. Breaking changes land in a minor bump rather than being
smuggled into a patch. 1.0.0 once it has run in a real app for a while.

### 0.10.0 — breaking

The public types moved from `ConnectionStatus*` to `Connectivity*` names. Members and behaviour are
unchanged; only the names moved.

| 0.9.0 | 0.10.0 |
| --- | --- |
| `ConnectionState` | `ConnectivityState` |
| `IConnectionStatusMonitor` | `IConnectivityMonitor` |
| `ConnectionStatusOptions` | `ConnectivityOptions` |

```diff
-if (Connection.State == ConnectionState.Online)
+if (Connection.State == ConnectivityState.Online)
```

`AddBlazorConnectionStatus`, the namespace, and the package id are unchanged.

**Why "Connectivity".** `ConnectionState` collided with `System.Data.ConnectionState`. Any file
with `using System.Data` got `CS0104: ambiguous reference` and had to alias one of the two — and in
an offline-first app with a local database, that's most files. The collision was ours to fix rather
than every consumer's to work around, so the enum was renamed and the rest of the public surface
followed it for consistency.

`BrowserConnectionState` was the other candidate and was rejected. The premise of this library is
that it does *not* report the browser's opinion: `navigator.onLine` is the browser's answer, and
the reason this package exists is that the browser's answer is the wrong one. Naming the type after
the thing it deliberately moved past would have been backwards.

The package and namespace keep `ConnectionStatus` because 0.9.0 was already published under it.
That's a small inconsistency, knowingly accepted — a package id is not worth abandoning over
vocabulary.

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
@inject IConnectivityMonitor Connection

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

    private void OnStatusChanged(object? sender, ConnectivityState state)
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
if (await Connection.CheckNowAsync() != ConnectivityState.Online)
{
    // queue it instead
}
```

## API

| Member | Notes |
| --- | --- |
| `ConnectivityState State` | `Unknown`, `Online` or `Offline`. `Unknown` until the first observation. |
| `bool IsOnline` | True only for `Online`. `Unknown` reads as false — read `State` if that matters. |
| `event EventHandler<ConnectivityState> StatusChanged` | Raised on change only, never on a repeat. |
| `Task StartAsync(CancellationToken)` | Loads the module and takes a first observation. Idempotent. |
| `Task<ConnectivityState> CheckNowAsync(CancellationToken)` | Probes now and completes with the answer. |
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
| `IConnectionStatusDetectorService` | `IConnectivityMonitor` |
| `bool IsOnline` only | `ConnectivityState State` with a real `Unknown`, plus `IsOnline` |
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
is `IConnectivityInterop` — the only part that needs a real browser.
