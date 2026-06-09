module washer(d = 5, h = 2) {
    cylinder(d = d, h = h);
}

module ring(r) circle(r);

translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);

if(n == 0) a(); else if(n == 1) b(); else c();

for(i = [0:5]) translate([i, 0, 0]) sphere(1);

let(w = 3) cube(w);

#%cube(1);
