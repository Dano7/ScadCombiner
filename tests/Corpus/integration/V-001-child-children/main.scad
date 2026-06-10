// V-001 — Integration Verification Backlog V1: child() ≡ children(0), child(n) ≡ children(n).
// The original renders via deprecated child() (OpenSCAD 2021.01 still evaluates it, with a
// DEPRECATED warning the bundle is allowed to shed); the bundle rewrites it to children(...)
// (SB5002) and must render byte-identical CSG.
include <lib.scad>

// Two children disambiguate child() from children(): child() must select ONLY the first.
first_only() { cube([1, 2, 3]); sphere(9); }
pick_second() { sphere(r = 4); cube(5); }
