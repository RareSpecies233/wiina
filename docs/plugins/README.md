# Plugins — not implemented (Stage 9 skipped)

Wiina v1.2.4 **does not ship a plugin system**. Stage 9 of the implementation
plan (plugin host, `Info.json`, permissions, WebView2-based plugin UI) was
intentionally skipped by the maintainer.

Consequences:

- There is no JavaScript/TypeScript plugin API and no plugin manifest parser.
- There is no WebView2 dependency, plugin sidebar rendering, or overlay window.
- Settings → Plugins and the Plugins menu item are present but **disabled** and
  state that Stage 9 is not implemented; they are not clickable placeholders.
- `%LOCALAPPDATA%\Wiina\plugins` may exist as an empty data directory, but no
  executable plugin code is loaded from it.

Third-party plugins therefore cannot be installed, and none are bundled. Do not
publish plugins against a Wiina plugin API for v1; there is no compatibility
surface to target yet.

If plugin support is revisited, it needs a new design covering the IPC boundary,
permissions, sandboxing, and GPL-compatible distribution. It must not be added
as a hidden or fake feature to v1.
