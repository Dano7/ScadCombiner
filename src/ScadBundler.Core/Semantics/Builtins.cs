namespace ScadBundler.Core.Semantics;

/// <summary>
/// Recognition tables for OpenSCAD's built-in names, so the analyzer does not flag <c>cube</c>,
/// <c>sin</c>, <c>PI</c>, etc. as unknown. Extracted from <c>Builtins::init(...)</c> in
/// <c>openscad-2019.05-3933</c> (see <c>docs/Builtins-Reference.md</c>). Per the version-robustness
/// rule these tables are for <i>recognition</i> only — an unknown name is treated as a user/library
/// symbol, never a hard error. Deprecated names are included so they are not misreported as unknown
/// (the inliner normalizes or preserves them in Slice 5).
/// </summary>
internal static class Builtins
{
    /// <summary>True when <paramref name="name"/> is a special (dynamically-scoped) variable: any
    /// <c>$</c>-prefixed identifier. These never resolve to a renameable symbol.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns><c>true</c> for a <c>$</c>-prefixed name.</returns>
    public static bool IsSpecialVariable(string name) => name.StartsWith('$');

    /// <summary>True when <paramref name="name"/> is a built-in module (or a recognized deprecated one).</summary>
    /// <param name="name">The module name.</param>
    /// <returns><c>true</c> when recognized.</returns>
    public static bool IsModule(string name) => Modules.Contains(name);

    /// <summary>True when <paramref name="name"/> is a built-in function (or a recognized deprecated one).</summary>
    /// <param name="name">The function name.</param>
    /// <returns><c>true</c> when recognized.</returns>
    public static bool IsFunction(string name) => Functions.Contains(name);

    /// <summary>True when <paramref name="name"/> is a built-in constant read as an identifier (<c>PI</c>).</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns><c>true</c> when recognized.</returns>
    public static bool IsConstant(string name) => Constants.Contains(name);

    private static readonly HashSet<string> Modules = new(StringComparer.Ordinal)
    {
        // CSG booleans / transforms / hull / CGAL
        "union", "difference", "intersection",
        "translate", "rotate", "scale", "mirror", "multmatrix", "resize", "color", "offset",
        "hull", "minkowski", "fill", "render",
        // extrude / 2D↔3D
        "linear_extrude", "rotate_extrude", "projection", "roof",
        // primitives
        "cube", "sphere", "cylinder", "polyhedron", "square", "circle", "polygon", "text",
        // import / data
        "import", "surface",
        // control / meta (also keywords; here for the statement-call forms)
        "for", "intersection_for", "if", "let", "echo", "assert", "children", "group",
        // deprecated (recognized; normalized/preserved by the inliner)
        "assign", "child",
        "import_stl", "import_dxf", "import_off", "dxf_linear_extrude", "dxf_rotate_extrude",
    };

    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        // math
        "abs", "sign", "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
        "floor", "ceil", "round", "ln", "log", "pow", "sqrt", "exp",
        "min", "max", "norm", "cross", "rands",
        // string / list / data
        "len", "concat", "lookup", "str", "chr", "ord", "search", "textmetrics", "fontmetrics",
        // type predicates
        "is_undef", "is_list", "is_num", "is_bool", "is_string", "is_function", "is_object",
        // meta / version
        "version", "version_num", "parent_module",
        // object (experimental) + import (function form)
        "object", "has_key", "import",
        // DXF
        "dxf_dim", "dxf_cross",
    };

    private static readonly HashSet<string> Constants = new(StringComparer.Ordinal)
    {
        "PI",
    };
}
