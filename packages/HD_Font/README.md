# HD Font (US) — current: v1.1.0 `HD_Font_v1.1.0_US.t2mod`

The TMNT2 main game font (`MainFont`) rebuilt as **true vector shapes from the original 256×256
bitmap**. No AI upscaling and no generic smoothing; every glyph re-renders to the original pixels.

* Package SHA-256: `6cfa1248527c136233a880c9418e9ad8985ceeb81814c0941180bcc9b2b4b208`
* Data-only: replaces `TMNT.DAT` entries 0 (`fonts/mainfont.met`) and 1 (`fonts/fonts.lpac`);
  `TMNT2.exe` is not modified, so it combines with every composable core mod.
* Requires **T2 Mod Manager v0.2.5** or newer. Same mod id as v1.0.0 (`HD_Font`), so it replaces it.

## How the vectors were made
1. Exact pixel outline of every letter (texels with ≥ 50% white coverage).
2. Hand-placed diagonal stairs become the straight lines they depict, and grey corner texels become
   45° chamfers. Each change is kept only if it matches the original anti-aliased texels at least as
   well (exact area per texel).
3. Horizontal/vertical edges slide to the sub-pixel position the grey columns/rows indicate.
4. Hairline punctuation `( ) " , / %` is fitted as pen strokes, and the ® ring as an ellipse.
5. The black outline (1.1 texel, mitre) and drop shadow (1.75 texel) are vector offsets fitted to
   the original alpha.
Average error against the original pixels: 3.7% coverage per texel.

The texture is drawn from those vectors at 4× (1024×1024, 32-bit). Glyph metrics are scaled exactly
4×; the Rt2d line height (taken from the last, 8 px placeholder line of the .met) is unchanged, so
text size and layout are identical to the original.

## Fonts
`fonts/T2BattleNexus-Fill.ttf` (letters) and `fonts/T2BattleNexus-Shadow.ttf` (outline + shadow,
same metrics). Put Shadow in black under Fill to get the in-game look at any size, e.g. for
thumbnails. Game-specific slots map to real Unicode: `Ⅱ` U+2161, `→` U+2192.

## Tested
In game under Wine with the retail data and the Cool Lighting v1.0.0 core (data check screen, title,
main menu, character select). Install / combine with Cool Lighting / uninstall through the v0.2.5
manager code, with a byte-exact vanilla restore.

## History
* v1.1.0 — vector reconstruction (this).
* v1.0.0 — `HD_Font_v1.0.0_US.t2mod`: letters traced at 4× with smoothing (softer, rounded corners).
