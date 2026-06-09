squares = [for (i = [0:5]) if (i % 2 == 0) i * i];
nested = [for (a = [0:2]) for (b = [0:2]) [a, b]];
cstyle = [for (i = 0; i < 5; i = i + 1) i];
select = [for (x = [0:3]) if (x > 1) x else -x];
letc = [let (k = 2) for (z = [0:1]) z * k];
flat = [each [1, 2], each other];
