// V-002 — Integration Verification Backlog V2: a use'd definition sees its own file's
// constants, and the using file cannot override them. lib_size() must evaluate to 7
// (lib.scad's LIB_UNIT), never 99, on both the original and the bundle.
use <lib.scad>

LIB_UNIT = 99;

lib_ball();
translate([20, 0, 0]) cube([lib_size(), 1, 1]);
