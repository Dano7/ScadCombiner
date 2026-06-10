// V-003 — Integration Verification Backlog V3: assign(...) ≡ let(...) for a binding-preserving
// rewrite (SB5001). OpenSCAD 2021.01 still evaluates assign() (with a DEPRECATED warning the
// bundle is allowed to shed); the bundle rewrites it to let(...) and must render identically.
assign(w = 5, h = 3) { cube([w, h, 1]); }
assign(d = 2) cylinder(r = d, h = 1);
