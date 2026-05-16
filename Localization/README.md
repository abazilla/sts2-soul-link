# Soul Link Localization

This folder owns everything related to translating Soul Link UI strings.

## Folder layout

```
Localization/
├── Loc.cs                # public API used by the rest of the mod
├── LocFontResolver.cs    # picks a Latin font + CJK fallback chain
├── LocTableLoader.cs     # lazy-loads locale JSON into the game's LocManager
├── locales/
│   ├── en.json           # source of truth (English) — add keys here first
│   ├── zh-CN.json        # Simplified Chinese stub (English placeholders)
│   └── …                 # other locales as they're added
└── README.md             # this file
```

The DLL alone is not enough — `locales/` is copied next to the DLL by
`scripts/deploy*.sh` and read at runtime via `Assembly.Location`.

## Public API

| Call                 | Use it when                                 |
|----------------------|---------------------------------------------|
| `Loc.T("key")`       | You need a plain `string` (label, button)   |
| `Loc.S("key")`       | You need a `LocString` (e.g. `HoverTip`)    |
| `Loc.CurrentLocale`  | Branch behavior on locale                   |

Both `T` and `S` call `LocTableLoader.EnsureLoaded()` first, so tables are
guaranteed loaded before lookup — this fixes the q60 NRE where the eager
`Initialize()` merge ran before `LocManager.Instance` existed.

## Key convention

* Lowercase, dot-separated, surface-prefixed: `settings.hp_mode.shared_pool`.
* JSON nesting is flattened on load — `settings.hp_mode.shared_pool` may
  be written as either `"settings.hp_mode.shared_pool": "…"` or nested.
* Underscores for multi-word segments (`split_max_hp`), not camelCase.
* Keys starting with `_` (e.g. `_meta`) are skipped — use for comments.

## Adding a language

1. Copy `locales/en.json` to `locales/{locale}.json` (BCP-47 code, e.g. `ja`, `zh-TW`).
2. Translate every value. Leave English in for keys you can't translate yet —
   the loader takes whatever's there; missing keys fall back to `en.json`.
3. Make sure `LocFontResolver` can find a font that renders your script. Latin
   locales need nothing extra; CJK locales rely on the fallback probe.
4. Test in-game by switching the game's language and entering a run.

## Fonts

`UI/SoulLinkFont.cs` delegates to `LocFontResolver.Resolve()`, which returns
**Kreon Bold (Latin)** with a **CJK font attached as a Godot `Fallbacks` entry**.
Godot transparently looks up any missing glyph in the fallback chain, so a single
font reference works for both English and Chinese without runtime branching.

The CJK probe order (see `LocFontResolver.CjkCandidates`):

1. `$SOULLINK_CJK_FONT` env override (testing/diagnostics).
2. Likely game-bundled paths under `res://fonts/` and `res://Sts2/Assets/Fonts/`.
   STS2 itself supports Simplified Chinese, so it bundles a CJK font — we reuse
   it instead of duplicating ~10 MB of TTF in the mod.
3. `user://SoulLink/NotoSansSC-Bold.ttf` — only relevant if we later decide to
   bundle our own. Drop the TTF there and the resolver will pick it up.

If the probe fails, the log line `LocFontResolver: no CJK fallback found` is
emitted and CJK glyphs render as tofu boxes. The fix is to confirm the real
asset path via GodotExplorer and add it to `CjkCandidates`.

## Open questions / TODOs

* Confirm `LocManager` exposes a locale-aware `MergeWith(string locale, dict)`
  overload. `LocTableLoader.MergeIntoLocManager` tries that path via reflection
  and falls back to merging only the active locale's strings. Once the real API
  is known, simplify.
* Audit remaining hardcoded UI strings (bd issue `sts2-soul-link-p20`) and
  route them through `Loc.T` / `Loc.S`.
* Confirm the real path of STS2's bundled CJK font and trim `CjkCandidates`.
