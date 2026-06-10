/* [Box] */
width = 10;  // [1:100]
height = 20;

/* [Hidden] */
// ======== include <lib.scad> ========
LIBCONST = 5;
module part(h) cube([LIBCONST, LIBCONST, h]);

// ======== main.scad (continued) ========
ratio = width / height;

part(ratio);
