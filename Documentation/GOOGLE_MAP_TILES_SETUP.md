# Google Photorealistic 3D Tiles setup

This proof uses Google's documented 3D Tiles endpoint through Cesium for Unity:

```text
https://tile.googleapis.com/v1/3dtiles/root.json?key=YOUR_API_KEY
```

Cesium streams the child tiles as the view changes. The scene uses `CesiumDataSource.FromUrl`, enables `createPhysicsMeshes`, and leaves `showCreditsOnScreen` enabled. The tileset URL and key exist only in Play Mode memory.

## Cloud Console checklist

1. Create a Google Cloud project and attach the billing account you intend to use.
2. Enable **Map Tiles API** for the project.
3. Create a new API key.
4. Set API restrictions to **Map Tiles API only**.
5. Apply the strongest application restriction available for your test target, and set a conservative quota/budget alert. For an editor/native prototype, choose an application restriction only if it is compatible with the way the Unity editor/player makes requests; do not weaken restrictions on a key used by another application.
6. Paste the key in the runtime's masked field. Alternatively, set `GOOGLE_MAP_TILES_API_KEY` before launching Unity, or put the key in the ignored local file:

   ```text
   Assets/WorldPvp/StreamingAssets/google-tiles-key.local.txt
   ```

   The tracked `.example.txt` file is a template only. Never replace it with a real credential in a commit.

## Separate Google Maps JavaScript / Places key for Phase 3

Places search and click-to-select map use the Google Maps JavaScript API from the browser. They do **not** use the Map Tiles key above. Enable **Maps JavaScript API** and **Places API (New)**, create a second key, restrict it to those APIs, and apply HTTP-referrer restrictions for the exact local/deployment origins. The browser will necessarily receive this key, so treat it as public: do not use a server key or rely on obscuring it in a password field. The Places search field/results show Google's official Maps attribution logo from `Assets/WorldPvp/Resources/GoogleMaps_Attribution_White.png`; do not remove, crop, recolour, or obscure it.

Optional local Editor configuration reads `GOOGLE_PLACES_API_KEY` or `Assets/WorldPvp/StreamingAssets/google-places-key.local.txt`; the tracked `google-places-key.local.example.txt` is only a template. Browser builds accept and save it separately from the Map Tiles key. Both keys remain out of `/join/{code}` URLs. Autocomplete requests reuse a Places session token through the selected Place Details lookup, following the [Autocomplete Data API session flow](https://developers.google.com/maps/documentation/javascript/place-autocomplete-data). The search field/results retain the official Google Maps attribution logo; its source is the [official attribution asset bundle](https://developers.google.com/static/maps/documentation/images/Google_Maps_Attribution_Assets.zip).

Google Maps Platform is pay-as-you-go and has per-SKU free usage thresholds and quotas; Places autocomplete/details and Maps JavaScript usage are distinct from Photorealistic 3D Tiles. Check Google's current [pricing list](https://developers.google.com/maps/billing-and-pricing/pricing), billing account, API usage, quotas, and budgets before testing. Do not assume the service or free usage is unlimited.

## Troubleshooting

- **No tiles / authorization failure:** verify billing, Map Tiles API enablement, API restriction, key spelling, and Cloud Console request metrics. Project scripts do not include the key or full URL in their own diagnostics; Cesium/Unity may emit lower-level request details, so treat Console logs as sensitive.
- **Tiles render but the character never leaves Loading:** confirm **Create Physics Meshes** is enabled, wait for the local tile collision to arrive, inspect Cesium/Unity errors, and check the location's Photorealistic 3D Tiles coverage.
- **Map looks less detailed at a location:** photorealistic surface coverage varies by location. Try the Hyde Park open-area preset before testing a less-covered location.
- **Attribution:** Cesium's on-screen credit display is enabled on the tileset. Do not obscure or remove Google/data attribution.

## Usage and licensing guardrails

This phase streams content for visual rendering; it does not download or retain a map database, extract buildings, or derive gameplay data from Google tiles. Respect Google's current Map Tiles API policies, cache headers, attribution rules, and the terms applicable to the project's billing address and intended use. Re-check those terms before distributing a build.
