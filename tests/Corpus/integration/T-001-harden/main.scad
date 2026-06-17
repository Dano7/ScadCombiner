// T-001 — hardening differential fixture.
// Minifying or obfuscating this bundle must render byte-identical CSG, emit identical ECHO, and add
// no new warnings (exercises: prologue params incl. a string, an echo'd string, an included private
// constant, a namespaced `use` library, an unused/tree-shakeable module, and a dynamically-scoped
// `$`-special-variable default that tree-shaking must NOT drop — see inc.scad).
// Also exercises Customizer trivia (group marker, description, inline annotation) that must survive a
// comment-stripping emit and still be valid OpenSCAD (the header above is hoisted away from the param).
/* [Dimensions] */
// Wall thickness in mm
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
translate([0, size * 2, 0]) ribbed(size);    // reads the $ribs special-variable default via dynamic scope
