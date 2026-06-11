// included: shares scope with the root; tube() reads the private constant FUDGE.
FUDGE = 1;
module tube(d, w) {
    difference() {
        cylinder(h = w, d = d);
        cylinder(h = w, d = d - w - FUDGE);
    }
}
