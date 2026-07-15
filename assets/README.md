# Application icons

`icon.svg` is the locked full-color source for the public tray and application icons. `icon-mono.svg` preserves the
approved optional monochrome/template fallback; it is not currently consumed at runtime or during packaging.

Generated and committed outputs are deliberately limited to actual consumers:

- `src/Shelfbound.Tray/Assets/tray.png` — 32×32 Avalonia tray and window icon.
- `assets/icon-256.png` — 256×256 Linux AppImage application icon.
- `assets/icon.ico` — Windows application/installer icon with 16, 24, 32, 48, 64, 128, and 256px frames.
- `assets/icon.icns` — macOS application icon with 16 through 1024px representations.

Run `pwsh scripts/icons.ps1` after changing the color source; ImageMagick is a build-only prerequisite. On macOS the
script uses `iconutil` for the ICNS. Non-Mac runs regenerate PNG/ICO outputs and validate the committed ICNS rather than
substituting a different encoder. Pass `-MagickPath` to use a portable ImageMagick executable, or use `-Check` to validate
committed sources and outputs without a renderer.

The color and mono source hashes are enforced by the exporter. See [TRADEMARKS.md](../TRADEMARKS.md) for the relationship
between the repository's AGPL-3.0-or-later copyright license and trademark rights.
