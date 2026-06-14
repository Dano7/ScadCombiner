// Member access is never validated statically: OpenSCAD resolves member validity at runtime
// (vectors expose .x/.y/.z, ranges .begin/.step/.end, objects arbitrary members). The analyzer
// accepts any `.ident` and emits no diagnostics (expected.diag absent).
v = [1, 2, 3];
comp = v.x;
swizzle = v.w;
r = [0:1:10];
range_member = r.begin;
metrics = v.advance;
nested = v.orient;
