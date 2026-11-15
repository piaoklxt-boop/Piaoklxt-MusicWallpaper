# Native lyric color source

Theme synchronization reads the QQ Music GF named-color property `SV_HighlightColor`. It does not capture a window, inspect rendered pixels, infer a color from artwork, or require a visible playback control. The wallpaper renderer still captures QQ Music for its separate desktop-mirror purpose.

## Source evidence

The verified QQ Music 22.81 module applies the same color argument to the lyric styles `SV_button_Theme_Theme`, `SV_Checkbox_Major`, and `SV_static_Theme`, then writes `SV_HighlightColor` through the GF theme service's named-color setter. This is the shared lyric tint, rather than the separate application-wide `themeColor` or black/white foreground `SV_Major`. The live property also matched the previously verified playback-button fill. No rendered comparison is used in production.

Automatic light/dark mode uses inverse luminance of `SV_Major`, the lyric foreground, independently of the highlight color. It therefore follows the native lyric palette rather than estimating the average brightness of the music picture.

## Verified private layout

This is a read-only private-layout adapter, not a public QQ Music API. Support is limited to this exact module pair; another version is rejected until independently verified.

| Module | SHA-256 | PE timestamp | Image size |
| --- | --- | --- | --- |
| `GF.dll` | `4585FA83FDC18220F9F1EC297325AE34D99EB84E1F78D30267F3626D96B5EB9F` | `6825CAF3` | `292000` |
| `QQMusic.dll` | `F41EFBC57FCC91F5B2216FE20BCFA1CF56305BB5B23BEBAA843A4B348730D6A2` | `6AED9823` | `E1C000` |

All offsets are relative to the loaded module or a validated native object, never fixed virtual addresses. WOW64 modules are discovered through Toolhelp's explicit 32-bit module flag.

- GF's cached service map is at RVA `258604`. The theme service key is GUID `7353BE16-493D-4829-8D38-9836DB56A4C0`.
- Its verified vtable is GF RVA `1EEAF0`; the named-color setter slot points to RVA `44B10`. The adapter validates these pointers but never calls the setter.
- The service's color map is at object offset `5C`. A color node contains a `CTXStringW` key at `10` and ARGB value at `14`. String byte length and capacity precede the character pointer at `-8` and `-4`.
- The QQ Music tint application routine at RVA `450F30` supplies its highlight argument to the lyric styles and writes the named property near RVA `451071`.

The reader opens QQ Music with query/read rights only. It neither writes memory nor injects a module or thread. It validates file hashes and loaded image headers, service identity, vtable, tree sizes, cycles, string lengths, terminators, and opaque color values. Only matching named properties are decoded; it does not scan unrelated process memory. Two matching palette reads reject an update observed midway through traversal. Every confirmed highlight change is applied, including one-channel changes; old screenshot-noise thresholds do not suppress native property updates. Invalid or unavailable data stops the read, preserving the last confirmed native color without any pixel fallback. The cache identity excludes values produced by older screenshot-based implementations.

## Validation

Background reads publish to the durable color cache only after the session generation is accepted. Clearing or disposing a session rejects its unfinished reads before they can change either the transient sample or durable color. Acceptance updates memory under the session lock; serialized disk persistence runs outside that lock and saves the latest accepted value. A temporary write failure preserves the in-memory color and retries on the next accepted read even if the color is unchanged. Repeated save failures produce one diagnostic until storage recovers.

`NativeLyricsColorRegression` exercises the production traversal and sampler with native-layout fixtures: independent highlight and text colors, updates without a renderer or visible control, wrong services, missing properties, malformed or cyclic trees, mixed concurrent reads, and loaded-binary mismatches. A structural regression rejects image parameters and window-capture imports in the color path. Live read-only verification establishes compatibility with the installed module pair; it does not establish support for other QQ Music builds.

To support another version, verify its actual named-color setter and consumer bindings, derive its layout and module identities, and validate reads before adding a profile. Never bypass the module checks or add a screenshot fallback.

Additional regressions inject an I/O failure at every step of a valid native traversal and verify recovery through the production sampler, reject duplicate keys and changing map headers, and check exact highlight/brightness independence across 1,000 deterministic recolorings. Session regressions use the production background-sampler factory with a blocked reader to check clear/dispose cancellation, a subsequent valid session, deferred persistence, and nonblocking reads while storage work is delayed. Cache recovery uses a real temporary directory creation failure, then verifies the same color survives reopening the cache.
