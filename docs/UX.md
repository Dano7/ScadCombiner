# ScadBundler User Experience Design

**Target Users**: OpenSCAD power users maintaining multi-file libraries (BOSL2, NopSCADlib, dotSCAD, custom projects) who need Thingiverse/MakerWorld/Customizer compatibility.

## Core Command-Line Interface
```bash
scadbundler bundle <input.scad> [options]
```

**Primary Options**:
- `-o, --output <file>`: Output bundled file (default: input.bundled.scad)
- `-p, --library-path <paths>`: Additional search paths (comma-separated or multiple flags). Respects OPENSCADPATH env var.
- `--on-collision <strategy>`: `auto`|`prefix`|`error`|`keep-first`|`keep-last` (default: **`auto`** = origin-dependent — `keep-last` for `include` collisions to match OpenSCAD's native last-wins, `prefix` for `use`d-library collisions to preserve library isolation; see [Spec.md](Spec.md) "Collision-strategy implication"). Any value other than `auto` forces that one strategy everywhere.
- `--preserve-comments`: Keep all comments (default: true)
- `--[no-]bundle-licenses`: Aggregate every bundled file's leading header/license comments at the top of the output (encounter order, root first, deduplicated — moved, not copied) and insert one-line provenance banners (`// ======== include <lib.scad> ========`) between the inlined sections (default: **on**; SB5007). The downloader of a bundled model sees whose code each section is and under what terms — `--no-bundle-licenses` produces an unannotated bundle, and `--minify`/`--no-preserve-comments` drop the annotations like any comment.
- `--minify`: Remove unnecessary whitespace/comments
- `--dry-run`: Show what would be done without writing output
- `--verbose`: Detailed logging of inlined files and transformations
- `--diff`: Show diff between input and bundled output

## Expected Behavior
- Smart resolution of `include` and `use` with cycle detection.
- Clear progress and summary output.
- Excellent error messages with context (coded diagnostics — see [Diagnostics.md](Diagnostics.md)).
- Preserves Customizer parameter comments for platform compatibility.
- Normalizes deprecated constructs to modern equivalents with a warning (`assign`→`let`, `child`→`children`); preserves deprecated built-ins (e.g. `import_stl`) with an informational note.

## C# & OpenSCAD Community Alignment
- NuGet package + `dotnet tool` support.
- Zero-magic transformations with explanatory logs.
- Respect `use` (modules/functions) vs `include` (full execution) semantics.

## Future Extensions
- VS Code extension
- GitHub Action
- Library API for advanced use cases
- **ScadBundler Live** (separate project): A clean, modern web interface allowing drag-and-drop upload of multiple files/folders. Users can resolve naming collisions interactively, view clear explanations of errors, and one-click download or copy the bundled result. The core bundler will expose a clean HTTP/JSON API or WASM library mode to support this.
