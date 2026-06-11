// used: definitions only, namespaced on import; half() and GAP are private to this file's context.
GAP = 3;
function half(x) = x / 2;
module scaled_block(s) { cube([half(s), half(s), GAP]); }
