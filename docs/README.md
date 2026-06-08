# ScadBundler

A robust, AST-based OpenSCAD file bundler/inliner for single-file platforms like Thingiverse.

**No half-measures.** Hand-written parser, high-quality C# implementation.

**Companion Project**: [ScadBundler Live](https://github.com/.../scad-bundler-live) — A user-friendly web interface for drag-and-drop bundling.

## Quick Start
```bash
dotnet tool install --global ScadBundler
scadbundler bundle myproject.scad -o bundled.scad
```

## Documentation

Start with **[Design.md](Design.md)** — architecture overview + the full document map. Key references: [Constitution.md](Constitution.md) (principles), [Spec.md](Spec.md) (semantics), [AST-Reference.md](AST-Reference.md), [Parser-Planning.md](Parser-Planning.md) (precedence), [Diagnostics.md](Diagnostics.md), [Builtins-Reference.md](Builtins-Reference.md), [Test-Corpus.md](Test-Corpus.md), [UX.md](UX.md). Implementation plan + per-slice specs: [Development-Slices.md](Development-Slices.md) → [slices/](slices/).
