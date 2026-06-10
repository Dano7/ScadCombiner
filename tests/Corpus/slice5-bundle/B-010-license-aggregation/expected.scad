// Widget v1 - (c) 2026 Root Author, CC-BY-4.0
// ======== file headers & licenses aggregated by ScadBundler ========
// -------- include <a.scad> --------
// a.scad - MIT License
// -------- include <common.scad> --------
// common.scad - CC0 Public Domain
// -------- include <b.scad> --------
// b.scad - Apache-2.0
// -------- use <gears.scad> --------
// gears.scad - GPL-3.0
// ====================================================================
/* [Size] */
width = 10;  // [1:100]

// ======== use <gears.scad> ========
module gears__gear() cylinder(h = 1, r = 2);

// ======== include <common.scad> ========
module frame(w) cube(w);

// ======== include <a.scad> ========
module widget(w) frame(w);

// ======== include <b.scad> ========
module bracket() frame(2);

// ======== main.scad (continued) ========
widget(width);
bracket();
gears__gear();
