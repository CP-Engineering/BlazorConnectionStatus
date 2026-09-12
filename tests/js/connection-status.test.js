import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { create } from '../../src/CPE.BlazorConnectionStatus/wwwroot/connection-status.js';

const CALLBACK = 'OnConnectionStatusObserved';

/** Lets queued promise callbacks run. Microtasks only, so fake timers do not block it. */
async function settle(turns = 20) {
    for (let i = 0; i < turns; i++) {
        await Promise.resolve();
    }
}

/** A stand-in for the DotNetObjectReference, recording every observation it is handed. */
function makeCallback() {
    const observations = [];
    return {
        observations,
        invokeMethodAsync: vi.fn((name, value) => {
            if (name === CALLBACK) {
                observations.push(value);
            }
            return Promise.resolve();
        }),
    };
}

/** Overrides navigator.onLine, which jsdom leaves read-only. */
function setBrowserOnline(value) {
    Object.defineProperty(window.navigator, 'onLine', {
        configurable: true,
        get: () => value,
    });
}

const OPTIONS = { pingUrl: '/healthz', pingIntervalMs: 30000, pingTimeoutMs: 5000 };

let instance = null;

beforeEach(() => {
    setBrowserOnline(true);
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve({ ok: true, status: 200 })));
});

afterEach(() => {
    instance?.dispose();
    instance = null;
    vi.unstubAllGlobals();
    vi.useRealTimers();
    vi.restoreAllMocks();
});

describe('create', () => {
    it('refuses to start without a .NET callback', () => {
        expect(() => create(null, OPTIONS)).toThrow(TypeError);
        expect(() => create({}, OPTIONS)).toThrow(TypeError);
    });

    it('takes a first observation immediately, so the app does not sit at Unknown', async () => {
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(callback.observations).toEqual([true]);
    });

    it('survives options being omitted', async () => {
        const callback = makeCallback();

        instance = create(callback, undefined);
        await settle();

        expect(callback.observations).toEqual([true]);
    });
});

describe('the probe', () => {
    it('is a cache-defeating HEAD request to the configured url', async () => {
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(fetch).toHaveBeenCalledTimes(1);
        const [url, init] = fetch.mock.calls[0];
        expect(url).toMatch(/^\/healthz\?_cs=\d+$/);
        expect(init.method).toBe('HEAD');
        expect(init.cache).toBe('no-store');
        expect(init.signal).toBeInstanceOf(AbortSignal);
    });

    it('adds its cache-buster with & when the url already has a query string', async () => {
        const callback = makeCallback();

        instance = create(callback, { ...OPTIONS, pingUrl: '/healthz?probe=1' });
        await settle();

        expect(fetch.mock.calls[0][0]).toMatch(/^\/healthz\?probe=1&_cs=\d+$/);
    });

    it('reports online when the server answers 2xx', async () => {
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(callback.observations).toEqual([true]);
    });

    it('reports offline when the server answers with an error status', async () => {
        vi.stubGlobal('fetch', vi.fn(() => Promise.resolve({ ok: false, status: 503 })));
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(callback.observations).toEqual([false]);
    });

    it('reports offline when the request fails outright', async () => {
        vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))));
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(callback.observations).toEqual([false]);
    });

    it('reports offline when the server stops answering, rather than hanging', async () => {
        vi.useFakeTimers();
        vi.stubGlobal('fetch', vi.fn((_url, init) => new Promise((_resolve, reject) => {
            init.signal.addEventListener('abort', () =>
                reject(new DOMException('Aborted', 'AbortError')));
        })));
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();
        expect(callback.observations).toEqual([]);

        await vi.advanceTimersByTimeAsync(OPTIONS.pingTimeoutMs);
        await settle();

        expect(callback.observations).toEqual([false]);
    });

    it('is skipped entirely when the browser already knows it is offline', async () => {
        setBrowserOnline(false);
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();

        expect(fetch).not.toHaveBeenCalled();
        expect(callback.observations).toEqual([false]);
    });

    it('is never made when no url is configured, and the browser is trusted instead', async () => {
        const callback = makeCallback();

        instance = create(callback, { pingUrl: null, pingIntervalMs: 30000, pingTimeoutMs: 5000 });
        await settle();

        expect(fetch).not.toHaveBeenCalled();
        expect(callback.observations).toEqual([true]);
    });
});

describe('the interval', () => {
    it('re-probes on schedule, which is the only thing that notices a silent drop', async () => {
        vi.useFakeTimers();
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();
        expect(fetch).toHaveBeenCalledTimes(1);

        await vi.advanceTimersByTimeAsync(OPTIONS.pingIntervalMs);
        await settle();
        expect(fetch).toHaveBeenCalledTimes(2);

        await vi.advanceTimersByTimeAsync(OPTIONS.pingIntervalMs);
        await settle();
        expect(fetch).toHaveBeenCalledTimes(3);
    });

    it('reports every observation, so .NET can answer a pending CheckNow', async () => {
        vi.useFakeTimers();
        const callback = makeCallback();

        instance = create(callback, OPTIONS);
        await settle();
        await vi.advanceTimersByTimeAsync(OPTIONS.pingIntervalMs);
        await settle();

        expect(callback.observations).toEqual([true, true]);
    });

    it('is not scheduled at all when no url is configured', async () => {
        vi.useFakeTimers();
        const callback = makeCallback();

        instance = create(callback, { pingUrl: null, pingIntervalMs: 1000, pingTimeoutMs: 500 });
        await settle();

        await vi.advanceTimersByTimeAsync(10000);
        await settle();

        expect(callback.observations).toEqual([true]);
    });
});

describe('browser events', () => {
    it('reports offline the moment the adapter drops, without waiting for a probe', async () => {
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();
        fetch.mockClear();

        setBrowserOnline(false);
        window.dispatchEvent(new Event('offline'));
        await settle();

        expect(fetch).not.toHaveBeenCalled();
        expect(callback.observations).toEqual([true, false]);
    });

    it('probes when the adapter returns, because an adapter is not a reachable server', async () => {
        const callback = makeCallback();
        setBrowserOnline(false);
        instance = create(callback, OPTIONS);
        await settle();

        setBrowserOnline(true);
        window.dispatchEvent(new Event('online'));
        await settle();

        expect(fetch).toHaveBeenCalledTimes(1);
        expect(callback.observations).toEqual([false, true]);
    });
});

describe('checkNow', () => {
    it('probes on demand and resolves once the observation has been reported', async () => {
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        await instance.checkNow();

        expect(fetch).toHaveBeenCalledTimes(2);
        expect(callback.observations).toEqual([true, true]);
    });

    it('carries the current answer, not the first one', async () => {
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        vi.stubGlobal('fetch', vi.fn(() => Promise.resolve({ ok: false, status: 502 })));
        await instance.checkNow();

        expect(callback.observations).toEqual([true, false]);
    });
});

describe('dispose', () => {
    it('stops the interval', async () => {
        vi.useFakeTimers();
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        instance.dispose();
        await vi.advanceTimersByTimeAsync(OPTIONS.pingIntervalMs * 3);
        await settle();

        expect(callback.observations).toEqual([true]);
    });

    it('stops listening to browser events', async () => {
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        instance.dispose();
        setBrowserOnline(false);
        window.dispatchEvent(new Event('offline'));
        await settle();

        expect(callback.observations).toEqual([true]);
    });

    it('reports nothing from a probe already in flight', async () => {
        let resolveFetch;
        vi.stubGlobal('fetch', vi.fn(() => new Promise((resolve) => { resolveFetch = resolve; })));
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        instance.dispose();
        resolveFetch({ ok: true, status: 200 });
        await settle();

        expect(callback.observations).toEqual([]);
    });

    it('is safe to call twice', async () => {
        const callback = makeCallback();
        instance = create(callback, OPTIONS);
        await settle();

        instance.dispose();
        expect(() => instance.dispose()).not.toThrow();
    });
});

describe('a .NET side that has gone away', () => {
    it('does not turn a rejected callback into an unhandled error', async () => {
        const callback = {
            invokeMethodAsync: vi.fn(() => Promise.reject(new Error('circuit disposed'))),
        };

        instance = create(callback, OPTIONS);
        await settle();

        expect(callback.invokeMethodAsync).toHaveBeenCalledTimes(1);
        await expect(instance.checkNow()).resolves.toBeUndefined();
    });
});
