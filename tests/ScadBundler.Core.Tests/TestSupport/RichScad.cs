namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// A single source file exercising every AST node kind, used to cover the whole-tree visitors
/// (<c>StructuralKey</c>) and the bundle rewriter across all node types in one fixture.
/// </summary>
public static class RichScad
{
    /// <summary>Source touching every statement and expression form (parses cleanly).</summary>
    public const string Source = """
        include <other.scad>
        use <font.ttf>

        $fn = 64;
        a = 1;
        b = 2.5e3;
        c = 0xFF;
        s = "hi\n";
        t = true;
        u = undef;
        v = [1, 2, 3];
        r = [0 : 2 : 10];
        bin = a + b * c - a / b % c ^ 2;
        cmp = a < b && b >= c || a == b;
        bit = a & b | a << 2 >> 1;
        neg = -a;
        pos = +a;
        notv = !t;
        bnot = ~c;
        cond = a ? b : c;
        par = (a + b);
        idx = v[0];
        mem = v.x;
        call = max(a, b);
        named = f1(p = a);
        lit = function (z) z * 2;
        letx = let (m = 1) m + a;
        assertx = assert(a > 0) a;
        echox = echo("x") a;
        comp = [for (i = [0 : 3]) i, for (j = 0; j < 3; j = j + 1) j, if (a > 0) a else b, let (k = 2) for (z = [0 : 1]) z * k, each v];

        function f1(p = 1) = p + 1;

        module m1(n = 3) {
            q = n + 1;
            module inner() cube(q);
            function g(y) = y;
            if (n > 0) inner(); else cube(1);
            for (i = [0 : n]) translate([i, 0, 0]) sphere(1);
            intersection_for (i = [0 : n]) rotate([0, 0, i]) cube(1);
            let (w = n) cube(w);
            ;
            inner();
        }

        #%cube(1);
        m1();
        """;
}
