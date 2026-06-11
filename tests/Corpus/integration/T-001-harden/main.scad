// T-001 — hardening differential fixture.
// Minifying or obfuscating this bundle must render byte-identical CSG, emit identical ECHO, and add
// no new warnings (exercises: prologue params incl. a string, an echo'd string, an included private
// constant, a namespaced `use` library, an unused/tree-shakeable module).
wall = 2;          // [1:5]
size = 10;
part_name = "widget";
include <inc.scad>
use <used.scad>

module box(d) { cube([d, d, wall]); }
module unused_helper(n) { sphere(r = n); }   // never called -> tree-shaken away

echo("name:", part_name);
box(size);
translate([0, 0, wall]) tube(size, wall);
translate([size * 2, 0, 0]) scaled_block(size);
