// ======== use <gear_a.scad> ========
module gear_a__gear() cube(1);

// ======== use <gear_b.scad> ========
module gear_b__gear() sphere(1);

// ======== main.scad ========
gear_b__gear();
