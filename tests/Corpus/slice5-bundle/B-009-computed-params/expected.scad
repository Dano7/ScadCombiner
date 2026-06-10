wall_thickness = 6;
tine_count = 2;
/* [Hidden] */
cleat_stagger = true;

/* [Hidden] */
// ======== include <goews.scad> ========
goews_fundamental_unit = 42;
goews_staggered_x_spacing = goews_fundamental_unit / 2;
module holder() cube([goews_staggered_x_spacing, 1, 1]);

// ======== main.scad (continued) ========
cleat_spacing_x = goews_staggered_x_spacing;

holder();
