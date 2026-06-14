namespace ScadBundler.Core.Diagnostics;

/// <summary>
/// The canonical <c>SBnnnn</c> diagnostic codes. The first digit groups by pipeline phase
/// (1 = lexing, 2 = parsing, …). See <c>docs/Diagnostics.md</c> for the authoritative catalog.
/// Codes must never be invented at implementation time without being recorded there first.
/// </summary>
public static class DiagnosticCode
{
    // Lexer (SB1xxx)

    /// <summary>Unterminated string literal.</summary>
    public const string UnterminatedString = "SB1001";

    /// <summary>Unterminated block comment.</summary>
    public const string UnterminatedBlockComment = "SB1002";

    /// <summary>Unterminated <c>include</c>/<c>use</c> statement (<c>&lt;</c> with no <c>&gt;</c>).</summary>
    public const string UnterminatedIncludeUse = "SB1003";

    /// <summary>Unexpected/unrecognized character.</summary>
    public const string UnexpectedCharacter = "SB1004";

    /// <summary>Non-ASCII character outside a string or comment.</summary>
    public const string NonAsciiCharacter = "SB1005";

    /// <summary>Undefined string escape sequence.</summary>
    public const string UndefinedEscape = "SB1006";

    /// <summary>Integer/hex literal too large to be represented precisely as a double.</summary>
    public const string ImpreciseNumber = "SB1007";

    /// <summary>Identifier starting with a digit (deprecated).</summary>
    public const string DigitLeadingIdentifier = "SB1008";

    /// <summary>Newline inside an <c>include</c>/<c>use</c> path.</summary>
    public const string NewlineInIncludePath = "SB1009";

    // Parser (SB2xxx)

    /// <summary>A specific expected token is missing.</summary>
    public const string ExpectedToken = "SB2001";

    /// <summary>A token is valid nowhere here, or input ended unexpectedly.</summary>
    public const string UnexpectedToken = "SB2002";

    /// <summary>An opening <c>(</c>/<c>[</c>/<c>{</c> has no matching close.</summary>
    public const string UnclosedDelimiter = "SB2003";

    /// <summary>A statement or definition is not terminated by <c>;</c>.</summary>
    public const string MissingSemicolon = "SB2004";

    /// <summary>An expression was expected but none was found.</summary>
    public const string ExpectedExpression = "SB2005";

    /// <summary>A parameter list is malformed.</summary>
    public const string InvalidParameterList = "SB2006";

    /// <summary>An argument list is malformed.</summary>
    public const string InvalidArgumentList = "SB2007";

    // Semantic analysis (SB3xxx)
    // SB3001 (invalid member access) was retired: OpenSCAD never validates `.member` at compile time
    // — vectors expose .x/.y/.z, ranges .begin/.step/.end, and objects (textmetrics/fontmetrics)
    // arbitrary members; an unmatched member yields `undef` at runtime, not a static error.

    /// <summary>A list-comprehension generator appears outside a <see cref="Ast.VectorExpression"/>.</summary>
    public const string ComprehensionOutsideVector = "SB3002";

    /// <summary>A variable is reassigned within a scope; the last assignment wins.</summary>
    public const string VariableReassigned = "SB3003";

    /// <summary>A module or function is redefined within a scope; the last definition wins.</summary>
    public const string DefinitionRedefined = "SB3004";

    /// <summary>
    /// A reference resolves to nothing — not a built-in, special variable, local binding, or any
    /// reachable user declaration. Emitted conservatively (only when all files are loaded).
    /// </summary>
    public const string UnknownReference = "SB3005";

    // Source loading / path resolution (SB4xxx)

    /// <summary>An <c>include</c>/<c>use</c> path could not be resolved on the search path.</summary>
    public const string IncludeUseNotFound = "SB4001";

    /// <summary>A file appears in its own <c>include</c>/<c>use</c> ancestry (a cycle).</summary>
    public const string CircularReference = "SB4002";

    // Inlining / transformation (SB5xxx)

    /// <summary>A deprecated <c>assign(...)</c> was normalized to an equivalent <c>let(...)</c>.</summary>
    public const string AssignNormalized = "SB5001";

    /// <summary>A deprecated <c>child(...)</c> was normalized to <c>children(...)</c>.</summary>
    public const string ChildNormalized = "SB5002";

    /// <summary>A deprecated built-in (e.g. <c>import_stl</c>) was preserved verbatim, not rewritten.</summary>
    public const string DeprecatedBuiltinPreserved = "SB5003";

    /// <summary>A definition (or private constant) was renamed/namespaced to resolve a collision.</summary>
    public const string NameRenamed = "SB5004";

    /// <summary>Structurally-identical definitions arriving via multiple paths were deduplicated.</summary>
    public const string DuplicateMerged = "SB5005";

    /// <summary>
    /// A genuine name collision was found under <c>--on-collision error</c>; the bundle is failed and
    /// no output is produced (the strategy that turns every collision into a hard error).
    /// </summary>
    public const string CollisionError = "SB5006";

    /// <summary>
    /// Non-root file headers/licenses were hoisted into the bundle's aggregated top header block
    /// (the default-on <c>--bundle-licenses</c> attribution pass).
    /// </summary>
    public const string LicensesAggregated = "SB5007";

    /// <summary>
    /// A top-level assignment in the bundle reads a variable whose first top-level assignment comes
    /// later in the bundle. OpenSCAD evaluates top-level assignments in document order, so the read
    /// yields <c>undef</c> — a post-assembly safety net against ordering bugs in the inliner.
    /// </summary>
    public const string ForwardReference = "SB5008";

    /// <summary>
    /// Summary of a hardening profile run (<c>--minify</c>/<c>--obfuscate</c>): how many identifiers
    /// were renamed, definitions tree-shaken, and Customizer parameters aliased. Info-severity;
    /// emitted once per bundle when a profile ran.
    /// </summary>
    public const string Hardened = "SB5009";

    /// <summary>
    /// A hardening transform was skipped on a node by a safety guard (a construct it cannot prove
    /// CSG-equivalent — e.g. a string in a path/font position, or an expression carrying a side
    /// effect). Info-severity; the node is left unchanged.
    /// </summary>
    public const string TransformSkipped = "SB5010";

    // Emitting (SB6xxx)

    /// <summary>
    /// Emitter self-check failure: the emitted text did not re-parse to a structurally-equivalent AST.
    /// An internal emitter bug; enabled as a correctness guard in debug/tests and never expected to fire.
    /// </summary>
    public const string EmitterSelfCheckFailed = "SB6001";
}
