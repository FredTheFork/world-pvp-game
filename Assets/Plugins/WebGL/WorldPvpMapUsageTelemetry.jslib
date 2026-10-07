mergeInto(LibraryManager.library, {
  WorldPvpMapUsageTelemetryStart: function () {
    if (typeof window === 'undefined' || !window.performance || !window.PerformanceObserver) {
      return 0;
    }

    var key = '__worldPvpMapUsageTelemetryV1';
    var state = window[key];
    if (state && state.started) {
      return state.supported ? 1 : 0;
    }

    state = {
      started: true,
      supported: false,
      rootRequestStarts: 0,
      rendererRequestStarts: 0,
      transferBytes: 0,
      transferSizedEntries: 0,
      mapResourceEntries: 0,
      observer: null
    };
    window[key] = state;

    var rootPrefix = 'https://tile.googleapis.com/v1/3dtiles/root.json';
    var rendererPrefix = 'https://tile.googleapis.com/v1/3dtiles/';
    var consume = function (entries) {
      for (var i = 0; i < entries.length; i++) {
        var entry = entries[i];
        var name = entry && entry.name ? String(entry.name) : '';
        if (name.indexOf(rendererPrefix) !== 0) {
          continue;
        }

        state.mapResourceEntries += 1;
        if (name.indexOf(rootPrefix) === 0) {
          state.rootRequestStarts += 1;
        } else {
          state.rendererRequestStarts += 1;
        }

        // transferSize is only meaningful when the browser exposes cross-origin timing data.
        // Never retain or return resource URLs: the root URL contains the API key.
        var bytes = entry.transferSize;
        if (typeof bytes === 'number' && isFinite(bytes) && bytes > 0) {
          state.transferBytes += bytes;
          state.transferSizedEntries += 1;
        }
      }
    };

    try {
      state.observer = new window.PerformanceObserver(function (list) {
        consume(list.getEntries());
      });
      state.observer.observe({ type: 'resource', buffered: true });
      state.supported = true;
    } catch (error) {
      state.supported = false;
      state.observer = null;
    }

    return state.supported ? 1 : 0;
  },

  WorldPvpMapUsageTelemetryGetRootRequestStarts: function () {
    var state = typeof window !== 'undefined' ? window.__worldPvpMapUsageTelemetryV1 : null;
    if (!state) return 0;
    if (state.observer) {
      var pending = state.observer.takeRecords();
      if (pending.length) {
        for (var i = 0; i < pending.length; i++) {
          var entry = pending[i];
          var name = entry && entry.name ? String(entry.name) : '';
          if (name.indexOf('https://tile.googleapis.com/v1/3dtiles/') !== 0) continue;
          state.mapResourceEntries += 1;
          if (name.indexOf('https://tile.googleapis.com/v1/3dtiles/root.json') === 0) {
            state.rootRequestStarts += 1;
          } else {
            state.rendererRequestStarts += 1;
          }
          var bytes = entry.transferSize;
          if (typeof bytes === 'number' && isFinite(bytes) && bytes > 0) {
            state.transferBytes += bytes;
            state.transferSizedEntries += 1;
          }
        }
      }
    }
    return Math.min(2147483647, Math.floor(state.rootRequestStarts));
  },

  WorldPvpMapUsageTelemetryGetRendererRequestStarts: function () {
    var state = typeof window !== 'undefined' ? window.__worldPvpMapUsageTelemetryV1 : null;
    if (!state) return 0;
    if (state.observer) {
      var pending = state.observer.takeRecords();
      if (pending.length) {
        for (var i = 0; i < pending.length; i++) {
          var entry = pending[i];
          var name = entry && entry.name ? String(entry.name) : '';
          if (name.indexOf('https://tile.googleapis.com/v1/3dtiles/') !== 0) continue;
          state.mapResourceEntries += 1;
          if (name.indexOf('https://tile.googleapis.com/v1/3dtiles/root.json') === 0) {
            state.rootRequestStarts += 1;
          } else {
            state.rendererRequestStarts += 1;
          }
          var bytes = entry.transferSize;
          if (typeof bytes === 'number' && isFinite(bytes) && bytes > 0) {
            state.transferBytes += bytes;
            state.transferSizedEntries += 1;
          }
        }
      }
    }
    return Math.min(2147483647, Math.floor(state.rendererRequestStarts));
  },

  WorldPvpMapUsageTelemetryGetTransferBytes: function () {
    var state = typeof window !== 'undefined' ? window.__worldPvpMapUsageTelemetryV1 : null;
    if (!state) return 0;
    return state.transferBytes;
  },

  WorldPvpMapUsageTelemetryGetSizedEntryCount: function () {
    var state = typeof window !== 'undefined' ? window.__worldPvpMapUsageTelemetryV1 : null;
    if (!state) return 0;
    return Math.min(2147483647, Math.floor(state.transferSizedEntries));
  },

  WorldPvpMapUsageTelemetryGetResourceEntryCount: function () {
    var state = typeof window !== 'undefined' ? window.__worldPvpMapUsageTelemetryV1 : null;
    if (!state) return 0;
    return Math.min(2147483647, Math.floor(state.mapResourceEntries));
  }
});
