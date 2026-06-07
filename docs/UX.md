# ScadBundler User Experience Design

**Target Users**: OpenSCAD power users maintaining multi-file libraries (BOSL2, NopSCADlib, dotSCAD, custom projects) who need Thingiverse/MakerWorld/Customizer compatibility.

## Core Command-Line Interface
```bash
scadbundler bundle <input.scad> [options]
```

**Primary Options**:
- `-o, --output <file>`: Output bundled file (default: input.bundled.scad)
- `-p, --library-path <paths>`: Additional search paths (comma-separated or multiple flags). Respects OPENSCADPATH env var.
- `--on-collision <strategy>`: prefix|error|keep-first|keep-last (default: **origin-dependent** — `keep-last` for `include` collisions to match OpenSCAD's native last-wins, `prefix` for `use`d-library collisions to preserve library isolation; see [Spec.md](Spec.md) "Collision-strategy implication"). An explicit value forces one strategy everywhere.
- `--preserve-comments`: Keep all comments (default: true)
- `--bundle-licenses`: Aggregate license headers
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
