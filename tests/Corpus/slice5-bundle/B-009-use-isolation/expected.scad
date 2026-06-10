// ======== use <a.scad> ========
module a__helper() cube(1);
module a__foo() a__helper();

// ======== use <b.scad> ========
module b__helper() sphere(1);
module b__bar() b__helper();

// ======== main.scad ========
a__foo();
b__bar();
