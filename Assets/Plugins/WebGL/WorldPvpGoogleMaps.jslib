var WorldPvpGoogleMapsLibrary = {
  $WorldPvpGoogleMaps: {
    ensureMapsLoaded: function (key) {
      if (!key || !key.trim()) {
        return Promise.reject(new Error("Missing Google Maps JavaScript key"));
      }
      key = key.trim();

      var state = window.__worldPvpGoogleMapsState;
      if (!state) {
        state = { key: "", promise: null };
        window.__worldPvpGoogleMapsState = state;
      }

      if (window.google && google.maps && google.maps.importLibrary && state.key === key) {
        return Promise.resolve();
      }
      if (state.promise) {
        if (state.key !== key) {
          return Promise.reject(new Error("A different Maps key is already loaded in this page"));
        }
        return state.promise;
      }
      if (window.google && google.maps && google.maps.importLibrary && state.key && state.key !== key) {
        return Promise.reject(new Error("A different Maps key is already loaded in this page"));
      }

      state.key = key;
      state.promise = new Promise(function (resolve, reject) {
        var script = document.createElement("script");
        script.src = "https://maps.googleapis.com/maps/api/js?key=" + encodeURIComponent(key) + "&v=weekly&loading=async";
        script.async = true;
        script.defer = true;
        script.crossOrigin = "anonymous";
        script.onload = function () {
          if (window.google && google.maps && google.maps.importLibrary) {
            resolve();
          } else {
            reject(new Error("Maps library import is unavailable"));
          }
        };
        script.onerror = function () {
          reject(new Error("Maps JavaScript API script failed to load"));
        };
        document.head.appendChild(script);
      }).catch(function (error) {
        state.promise = null;
        state.key = "";
        throw error;
      });

      return state.promise;
    },

    sendToUnity: function (receiver, method, payload) {
      if (!window.worldPvpUnityInstance || !receiver || !method) return;
      try {
        window.worldPvpUnityInstance.SendMessage(receiver, method, payload || "");
      } catch (e) {
        // Never write API keys, invite URLs, or coordinates to the browser console.
      }
    },

    closeMapPicker: function () {
      var overlay = window.__worldPvpMapPickerOverlay;
      if (overlay && overlay.parentNode) {
        overlay.parentNode.removeChild(overlay);
      }
      window.__worldPvpMapPickerOverlay = null;
    },

    copyTextFallback: function (text) {
      try {
        var textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.style.cssText = "position:fixed;left:-9999px;top:0;";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
      } catch (e) {
        // Clipboard failure is non-fatal; the invite URL remains visible in the IMGUI field.
      }
    }
  },

  WorldPvp_RequestBrowserProfile: function (receiverPtr) {
    var receiver = UTF8ToString(receiverPtr);
    var profile = "";
    try {
      profile = window.sessionStorage.getItem("worldpvp-tab-profile") || "";
      if (!profile) {
        profile = "w" + Date.now().toString(36) + Math.random().toString(36).slice(2, 12);
        profile = profile.replace(/[^A-Za-z0-9_-]/g, "").slice(0, 30);
        window.sessionStorage.setItem("worldpvp-tab-profile", profile);
      }
    } catch (e) {
      profile = "w" + Date.now().toString(36) + Math.random().toString(36).slice(2, 12);
      profile = profile.replace(/[^A-Za-z0-9_-]/g, "").slice(0, 30);
    }
    WorldPvpGoogleMaps.sendToUnity(receiver, "OnBrowserProfileReady", profile);
  },

  WorldPvp_RequestPlacesPredictions: function (inputPtr, keyPtr, receiverPtr) {
    var input = UTF8ToString(inputPtr);
    var key = UTF8ToString(keyPtr);
    var receiver = UTF8ToString(receiverPtr);
    var state = window.__worldPvpPlacesState;
    if (!state) {
      state = { sessionToken: null, predictions: Object.create(null), requestSerial: 0 };
      window.__worldPvpPlacesState = state;
    }
    state.requestSerial += 1;
    var requestSerial = state.requestSerial;
    state.predictions = Object.create(null);

    WorldPvpGoogleMaps.ensureMapsLoaded(key).then(function () {
      return google.maps.importLibrary("places");
    }).then(function (placesLibrary) {
      if (!state.sessionToken) {
        state.sessionToken = new placesLibrary.AutocompleteSessionToken();
      }
      return placesLibrary.AutocompleteSuggestion.fetchAutocompleteSuggestions({
        input: input,
        sessionToken: state.sessionToken
      });
    }).then(function (response) {
      if (requestSerial !== state.requestSerial) return;
      var predictions = [];
      var suggestions = response && response.suggestions ? response.suggestions : [];
      for (var i = 0; i < suggestions.length && predictions.length < 8; i++) {
        var placePrediction = suggestions[i] && suggestions[i].placePrediction;
        if (!placePrediction || !placePrediction.placeId) continue;
        state.predictions[placePrediction.placeId] = placePrediction;
        predictions.push({
          placeId: placePrediction.placeId,
          description: placePrediction.text ? placePrediction.text.toString() : ""
        });
      }
      WorldPvpGoogleMaps.sendToUnity(receiver, "OnPlacesPredictionsJson", JSON.stringify({ predictions: predictions, error: "" }));
    }).catch(function (e) {
      if (requestSerial !== state.requestSerial) return;
      WorldPvpGoogleMaps.sendToUnity(receiver, "OnPlacesPredictionsJson", JSON.stringify({
        predictions: [],
        error: "Google Places search failed. Check Maps JavaScript/Places API enablement, billing, and HTTP-referrer restrictions."
      }));
    });
  },

  WorldPvp_ResolvePlace: function (placeIdPtr, keyPtr, receiverPtr) {
    var placeId = UTF8ToString(placeIdPtr);
    var key = UTF8ToString(keyPtr);
    var receiver = UTF8ToString(receiverPtr);
    var state = window.__worldPvpPlacesState;

    WorldPvpGoogleMaps.ensureMapsLoaded(key).then(function () {
      return google.maps.importLibrary("places");
    }).then(function (placesLibrary) {
      var prediction = state && state.predictions ? state.predictions[placeId] : null;
      var place = prediction && typeof prediction.toPlace === "function"
        ? prediction.toPlace()
        : new placesLibrary.Place({ id: placeId });
      if (state) {
        state.predictions = Object.create(null);
        state.sessionToken = null;
      }
      return place.fetchFields({ fields: ["displayName", "formattedAddress", "location"] }).then(function () {
        var location = place.location;
        if (!location || typeof location.lat !== "function" || typeof location.lng !== "function") {
          throw new Error("missing place coordinates");
        }
        WorldPvpGoogleMaps.sendToUnity(receiver, "OnPlaceDetailsJson", JSON.stringify({
          latitude: location.lat(),
          longitude: location.lng(),
          displayName: place.displayName || "",
          formattedAddress: place.formattedAddress || "",
          error: ""
        }));
      });
    }).catch(function (e) {
      if (state) {
        state.predictions = Object.create(null);
        state.sessionToken = null;
      }
      WorldPvpGoogleMaps.sendToUnity(receiver, "OnPlaceDetailsJson", JSON.stringify({
        latitude: 0,
        longitude: 0,
        displayName: "",
        formattedAddress: "",
        error: "Google Places could not resolve the selected location. Check the separate Places API key and configuration."
      }));
    });
  },

  WorldPvp_OpenMapPicker: function (keyPtr, latitude, longitude, receiverPtr) {
    var key = UTF8ToString(keyPtr);
    var receiver = UTF8ToString(receiverPtr);
    var startLatitude = Number(latitude);
    var startLongitude = Number(longitude);

    WorldPvpGoogleMaps.ensureMapsLoaded(key).then(function () {
      return google.maps.importLibrary("maps");
    }).then(function (mapsLibrary) {
      WorldPvpGoogleMaps.closeMapPicker();
      var overlay = document.createElement("div");
      overlay.id = "worldpvp-map-picker-overlay";
      overlay.style.cssText = "position:fixed;inset:0;z-index:2147483000;background:rgba(5,10,18,.78);display:flex;align-items:center;justify-content:center;padding:18px;box-sizing:border-box;font-family:system-ui,-apple-system,Segoe UI,sans-serif;";

      var card = document.createElement("div");
      card.style.cssText = "width:min(100%,1040px);height:min(92vh,760px);background:#101923;color:#eef4fb;border:1px solid #385064;border-radius:14px;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 20px 60px rgba(0,0,0,.55);";

      var header = document.createElement("div");
      header.style.cssText = "display:flex;align-items:center;gap:16px;padding:14px 18px;min-height:48px;box-sizing:border-box;";
      var title = document.createElement("div");
      title.textContent = "Select battle centre · click the map";
      title.style.cssText = "font-size:16px;font-weight:700;flex:1;";
      var cancel = document.createElement("button");
      cancel.type = "button";
      cancel.textContent = "Cancel";
      cancel.style.cssText = "border:1px solid #526a7d;background:#1b2936;color:#eef4fb;border-radius:8px;padding:8px 14px;font:inherit;cursor:pointer;";
      header.appendChild(title);
      header.appendChild(cancel);

      var help = document.createElement("div");
      help.textContent = "Move and zoom, choose a map type, then click once to set the centre. Altitude defaults to 0 m above the WGS84 ellipsoid.";
      help.style.cssText = "padding:0 18px 10px;color:#b7c7d5;font-size:13px;";
      var mapElement = document.createElement("div");
      mapElement.style.cssText = "flex:1;min-height:280px;margin:0 12px 12px;border-radius:9px;overflow:hidden;";
      card.appendChild(header);
      card.appendChild(help);
      card.appendChild(mapElement);
      overlay.appendChild(card);
      document.body.appendChild(overlay);
      window.__worldPvpMapPickerOverlay = overlay;

      cancel.addEventListener("click", function () {
        WorldPvpGoogleMaps.closeMapPicker();
      });

      var map = new mapsLibrary.Map(mapElement, {
        center: { lat: startLatitude, lng: startLongitude },
        zoom: 17,
        mapTypeId: "hybrid",
        mapTypeControl: true,
        streetViewControl: false,
        fullscreenControl: false,
        clickableIcons: false,
        gestureHandling: "greedy"
      });

      map.addListener("click", function (event) {
        if (!event || !event.latLng) return;
        var pickedLatitude = event.latLng.lat();
        var pickedLongitude = event.latLng.lng();
        WorldPvpGoogleMaps.closeMapPicker();
        WorldPvpGoogleMaps.sendToUnity(receiver, "OnMapPickJson", JSON.stringify({
          latitude: pickedLatitude,
          longitude: pickedLongitude,
          error: ""
        }));
      });
    }).catch(function (e) {
      WorldPvpGoogleMaps.sendToUnity(receiver, "OnMapPickJson", JSON.stringify({
        latitude: 0,
        longitude: 0,
        error: "Google Maps could not open. Check the separate Maps JavaScript key, API enablement, billing, and referrer restrictions."
      }));
    });
  },

  WorldPvp_CopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).catch(function () {
        WorldPvpGoogleMaps.copyTextFallback(text);
      });
    } else {
      WorldPvpGoogleMaps.copyTextFallback(text);
    }
  },

  WorldPvp_SetInvitePath: function (joinCodePtr) {
    var joinCode = UTF8ToString(joinCodePtr).trim().toUpperCase();
    if (!/^[A-Z0-9]{4,32}$/.test(joinCode)) return;
    try {
      window.history.replaceState({}, "", window.location.origin + "/join/" + joinCode);
    } catch (e) {
      // Sharing still works from the visible invite field if History API changes are unavailable.
    }
  },

  WorldPvp_ClearInvitePath: function () {
    try {
      window.history.replaceState({}, "", window.location.origin + "/");
    } catch (e) {
      // Leaving the session still works if the address bar cannot be changed.
    }
  }
};

autoAddDeps(WorldPvpGoogleMapsLibrary, "$WorldPvpGoogleMaps");
mergeInto(LibraryManager.library, WorldPvpGoogleMapsLibrary);
