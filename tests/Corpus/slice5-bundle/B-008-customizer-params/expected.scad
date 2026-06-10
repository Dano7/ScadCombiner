/* [Box] */
width = 10;  // [1:100]
height = 20;
ratio = width / height;

/* [Hidden] */
// ======== include <lib.scad> ========
LIBCONST = 5;
module part(h) cube([LIBCONST, LIBCONST, h]);

// ======== main.scad (continued) ========
part(ratio);
