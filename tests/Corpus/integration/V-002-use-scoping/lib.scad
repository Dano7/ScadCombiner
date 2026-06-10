// V-002 library: lib_size() must read THIS file's LIB_UNIT (7), not the consumer's (99).
LIB_UNIT = 7;
function lib_size() = LIB_UNIT;
module lib_ball() { sphere(r = lib_size()); }
