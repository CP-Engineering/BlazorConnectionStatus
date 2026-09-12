// CPE.BlazorConnectionStatus
// An ES module, imported by the library itself. Consumers add no script tag.
//
// Every observation is reported to .NET, changed or not, because the .NET side is what decides
// whether an observation is worth raising an event for, and because CheckNowAsync waits for one.

// PAIRED WITH C#: ConnectivityMonitor.OnConnectionStatusObserved. Nothing tests the two names
// against each other — the .NET tests call the method directly and these tests assert this
// constant — so a rename on one side alone breaks only at run time, in a browser. Deliberately
// left unrenamed when the public types moved to "Connectivity" names: it is a wire name a
// consumer never types, so the churn would have been all risk and no benefit.
const DOTNET_CALLBACK = 'OnConnectionStatusObserved';

/**
 * Appends a cache-busting parameter so intermediate caches cannot answer the probe for us.
 * @param {string} url
 * @returns {string}
 */
function bust(url) {
    const separator = url.includes('?') ? '&' : '?';
    return `${url}${separator}_cs=${Date.now()}`;
}

/**
 * Creates one monitor instance.
 * @param {{ invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown> }} callback
 *   A DotNetObjectReference to the ConnectivityMonitor.
 * @param {{ pingUrl: string|null, pingIntervalMs: number, pingTimeoutMs: number }} options
 * @returns {{ checkNow: () => Promise<void>, dispose: () => void }}
 */
export function create(callback, options) {
    if (!callback || typeof callback.invokeMethodAsync !== 'function') {
        throw new TypeError('A .NET callback reference is required.');
    }

    const settings = options || {};
    const pingUrl = settings.pingUrl || null;
    const pingIntervalMs = settings.pingIntervalMs || 30000;
    const pingTimeoutMs = settings.pingTimeoutMs || 5000;

    let disposed = false;
    let timerId = null;

    function report(isOnline) {
        if (disposed) {
            return Promise.resolve();
        }

        // A rejected callback means .NET went away mid-flight. Nothing here can fix that, and an
        // unhandled rejection in a timer would surface as noise in the consumer's console.
        return Promise.resolve(callback.invokeMethodAsync(DOTNET_CALLBACK, isOnline))
            .catch(() => { });
    }

    async function probe() {
        // The browser knowing it is offline is cheap and certain; skip the request entirely.
        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
            return false;
        }

        if (!pingUrl) {
            return typeof navigator === 'undefined' ? true : navigator.onLine !== false;
        }

        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), pingTimeoutMs);

        try {
            const response = await fetch(bust(pingUrl), {
                method: 'HEAD',
                cache: 'no-store',
                signal: controller.signal,
            });

            return response.ok === true;
        } catch {
            // Aborts, DNS failures, CORS rejections and dropped connections all mean the same
            // thing to a caller: the server did not answer.
            return false;
        } finally {
            clearTimeout(timeoutId);
        }
    }

    async function observe() {
        const isOnline = await probe();
        await report(isOnline);
    }

    function onBrowserOffline() {
        // The adapter is gone. No probe can succeed, so say so without waiting for one.
        report(false);
    }

    function onBrowserOnline() {
        // The adapter is back, which is not the same as the server being reachable.
        observe();
    }

    if (typeof window !== 'undefined') {
        window.addEventListener('online', onBrowserOnline);
        window.addEventListener('offline', onBrowserOffline);
    }

    if (pingUrl) {
        timerId = setInterval(observe, pingIntervalMs);
    }

    // The first observation, so the app does not sit at Unknown until the first interval elapses.
    observe();

    return {
        checkNow() {
            return observe();
        },

        dispose() {
            if (disposed) {
                return;
            }

            disposed = true;

            if (timerId !== null) {
                clearInterval(timerId);
                timerId = null;
            }

            if (typeof window !== 'undefined') {
                window.removeEventListener('online', onBrowserOnline);
                window.removeEventListener('offline', onBrowserOffline);
            }
        },
    };
}
