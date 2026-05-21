# Soul Link Localization

Everything related to translating Soul Link UI strings.

## Folder layout

```
Localization/
├── Loc.cs                # public API used by the rest of the mod
├── LocFontResolver.cs    # Kreon + CJK fallback chain
├── LocTableLoader.cs     # lazy + locale-change-aware JSON loader
├── locales/
│   ├── eng.locjson       # source of truth (English) — add keys here first
│   ├── zhs.locjson       # Simplified Chinese
│   └── …                 # other locales as they're added
└── README.md             # this file
```

The DLL alone is not enough — `locales/` is copied next to the DLL by
`scripts/deploy*.sh` and read at runtime via `Assembly.Location`.

**Extension is `.locjson`, not `.json`.** The files are still JSON. STS2's
`ModManager.ReadModsInDirRecursive` parses *every* `*.json` under `mods/` as a
mod manifest and rejects ours ("missing the 'id' field"). The 4-char `*.json`
glob can't match `.locjson`, so the scanner skips them; `LocTableLoader` globs
`*.locjson` explicitly. Do not rename them back to `.json`.

## Locale codes

STS2 uses **3-letter** language codes, **not** BCP-47. File names must match.
From `LocManager._weblateToGameLanguage`:

| Game code | Language               |
|-----------|------------------------|
| `eng`     | English (fallback)     |
| `zhs`     | Simplified Chinese     |
| `deu`     | German                 |
| `spa`     | Spanish (Spain)        |
| `esp`     | Spanish (LATAM)        |
| `fra`     | French                 |
| `ita`     | Italian                |
| `jpn`     | Japanese               |
| `kor`     | Korean                 |
| `pol`     | Polish                 |
| `ptb`     | Portuguese (Brazil)    |
| `rus`     | Russian                |
| `tha`     | Thai                   |
| `tur`     | Turkish                |

## Table strategy

Mods cannot register a new `LocTable` via the public API — `LocManager.GetTable`
throws on unknown names, and `_tables` is private and is fully replaced on
`SetLanguage` anyway. So we **piggyback on the game's existing `gameplay_ui`
table** and namespace every Soul Link key with `soullink.`:

* JSON keys are clean (`settings.header`).
* `LocTableLoader` adds the `soullink.` prefix at merge time.
* `Loc.T` / `Loc.S` / `Loc.Tf` add the same prefix at lookup time.

`SetLanguage` reloads `gameplay_ui` from disk on every language switch, wiping
our merged keys; the locale-change callback re-merges them.

## Public API

| Call                       | Use it when                              |
|----------------------------|------------------------------------------|
| `Loc.T("key")`             | Plain `string` (label, button)           |
| `Loc.S("key")`             | `LocString` (e.g. `HoverTip`)            |
| `Loc.Tf("key", a, b)`      | `string.Format` w/ `{0} {1}` placeholders|
| `Loc.CurrentLocale`        | Read `LocManager.Language`               |

Both `T` and `S` call `LocTableLoader.EnsureLoaded()` first, so tables are
guaranteed loaded before lookup — this fixes the q60 NRE (`LocManager.Instance`
was null at `Initialize()` time on the original eager-merge code).

`T` calls `LocString.GetFormattedText()` (the real SmartFormat resolve) — the
type has **no `ToString()` override**, so calling `.ToString()` directly returns
`"MegaCrit.Sts2.Core.Localization.LocString"` and leaks into the UI.

## Locale switching

`LocTableLoader` subscribes to `LocString.SubscribeToLocaleChange` on first
successful load. When the user changes language:

1. Game wipes & reloads its `LocTable._translations` for the new locale.
2. Our callback fires → re-merge our keys into the freshly-loaded table.
3. UI controls that hold a `LocString` (e.g. `HoverTip` title) re-resolve on
   next render → they update without intervention.

**Hot-switch wiring**:
- `Localization/LocaleBus.cs` wraps `LocString.SubscribeToLocaleChange` /
  `UnsubscribeToLocaleChange` with try/catch so callers don't have to.
- `SoulLinkSettingsPanel` subscribes in `Initialize`; on change it tears down its
  child subtree and re-runs `BuildUi` so captured-string controls (`Button.Text`,
  `OptionButton.AddItem`, descriptors) pick up the new strings. `_isClientMode`
  and visibility survive because they're fields, not children.
- `RunStatsPanel` and `CombatLogPanel` subscribe `Refresh` directly — their
  `DoRefresh` already re-applies every label from `Loc.T`, no rebuild needed.
- `DebugOverlay` polls via `_Process` so it auto-updates next frame; no
  subscription required.
- All panels unsubscribe in `_ExitTree`.

## Key convention

* Lowercase, dot-separated, surface-prefixed: `settings.hp_mode.shared_pool`.
* JSON nesting is flattened on load.
* Underscores for multi-word segments (`split_max_hp`), not camelCase.
* Keys starting with `_` (e.g. `_meta`) are skipped at top-level — use for comments.
* Placeholders use `{0}, {1}, …` (consumed by `Loc.Tf` via `string.Format`).
* BBCode tags inside values are passed through verbatim — translators must
  keep `[color=#...]…[/color]` and the hex codes intact.

## Adding a language

1. Copy `locales/eng.locjson` to `locales/{game_code}.locjson` (e.g. `jpn.locjson`).
2. Translate every value. Leave English in for keys you can't translate yet —
   missing keys fall back to `eng.json`.
3. Make sure `LocFontResolver` can find a font that renders your script.
   Latin locales need nothing extra; CJK locales rely on the fallback probe.
4. Test in-game: Settings → Language → select your locale.

## Fonts

`UI/SoulLinkFont.cs` delegates to `LocFontResolver.Resolve()`, which returns
Kreon Bold (Latin) with a CJK font attached as a Godot `Fallbacks` entry.
Godot looks up any missing glyph in the fallback chain transparently, so a
single font reference works for both English and Chinese.

CJK probe order (`LocFontResolver.CjkCandidates`):

1. `$SOULLINK_CJK_FONT` env override (testing/diagnostics).
2. Game-bundled CJK fonts under `res://fonts/` / `res://Sts2/Assets/Fonts/`.
   STS2 supports Simplified Chinese, so it bundles a CJK font — reuse it
   instead of duplicating ~10 MB of TTF.
3. `user://SoulLink/NotoSansSC-Bold.ttf` — only relevant if we ever bundle
   our own.

If probe fails, log line `LocFontResolver: no CJK fallback found` and CJK
glyphs render as tofu boxes. Fix by confirming the real asset path with
GodotExplorer and adding it to `CjkCandidates`.

## Open questions / TODOs

* Audit remaining hardcoded UI strings (bd issue `sts2-soul-link-p20`) and
  route through `Loc.T` / `Loc.S` — current pass covers Settings, Stats,
  Feed, Debug, HoverTip. CombatLog event strings still hardcoded.
* Confirm STS2's bundled CJK font path and trim `CjkCandidates`.
