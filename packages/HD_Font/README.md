# HD Font v1.0.0 (US) — `HD_Font_v1.0.0_US.t2mod`

HD remaster of the TMNT2 main game font (`MainFont`) with the original look: letter shapes traced
at 4× (1024×1024, 32-bit) with the black outline and drop shadow rebuilt to match the original,
and the PS2/Xbox/GameCube button icons redrawn as sharp circles in their original colours.
Text size, spacing and layout are unchanged.

* Package SHA-256: `43b22386128516b39fa3bd7d6a01713476e86c021237d8ca1862c6a367fd4b01`
* Data-only: replaces `TMNT.DAT` entries 0 (`fonts/mainfont.met`) and 1 (`fonts/fonts.lpac`);
  `TMNT2.exe` is not modified, so it combines with every composable core mod.
* Requires **T2 Mod Manager v0.2.5** or newer (older managers reject the `.met` payload).

| Entry | Vanilla SHA-256 | Replacement SHA-256 | Size |
| --- | --- | --- | --- |
| 0 `fonts/mainfont.met` | `85d07160…24dcb21` | see manifest | 4,585 → 4,714 |
| 1 `fonts/fonts.lpac` | `19a58d9b…1ce688f` | see manifest | 197,504 → 4,325,248 |

Why the metrics change: RenderWare Rt2d divides glyph pixel coordinates by the real texture size,
so a 4× texture needs 4× coordinates. The font height comes from the last line of the .met (an
8 px placeholder), which is left unchanged, so text keeps its size without an EXE change.

Tested: in game under Wine with the retail data and the Cool Lighting v1.0.0 core (title, main
menu, character select); install / combine with Cool Lighting / uninstall through the v0.2.5
manager code, with byte-exact vanilla restore.
