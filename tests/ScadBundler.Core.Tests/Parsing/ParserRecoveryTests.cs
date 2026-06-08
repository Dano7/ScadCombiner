using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Text;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Error recovery (Slice-2 §10): each parser diagnostic SB2001–SB2007 fires for its trigger and the
/// parser recovers via panic-mode, always returning a (possibly partial) <see cref="ScadFile"/> and
/// never throwing.
/// </summary>
public sealed class ParserRecoveryTests
{
    private static List<string> Codes(string source) =>
        ParseHelper.ParseTokensOnly(source).Diagnostics.Select(d => d.Code).ToList();

    [Fact] // SB2001
    public void MissingExpectedToken_ReportsSB2001()
    {
        IReadOnlyList<string> codes = Codes("function f() x;");
        Assert.Contains(DiagnosticCode.ExpectedToken, codes);
    }

    [Fact] // SB2002
    public void UnexpectedStatementToken_ReportsSB2002_AndRecovers()
    {
        ParseResult result = ParseHelper.ParseTokensOnly(": module m() cube(1);");
        Assert.Contains(DiagnosticCode.UnexpectedToken, result.Diagnostics.Select(d => d.Code));

        // Recovery resynchronizes to the module definition that follows.
        Assert.Contains(result.Root.Statements, s => s is ModuleDefinition);
    }

    [Theory] // SB2003 — unclosed (), [], {}
    [InlineData("x = (1;")]
    [InlineData("a = [1, 2;")]
    [InlineData("module a() { cube(1);")]
    public void UnclosedDelimiter_ReportsSB2003(string source)
    {
        Assert.Contains(DiagnosticCode.UnclosedDelimiter, Codes(source));
    }

    [Fact] // SB2004
    public void MissingSemicolon_ReportsSB2004_AndRecovers()
    {
        ParseResult result = ParseHelper.ParseTokensOnly("x = 1\ny = 2;");
        Assert.Contains(DiagnosticCode.MissingSemicolon, result.Diagnostics.Select(d => d.Code));

        // Both assignments are still recovered.
        Assert.Equal(2, result.Root.Statements.Count(s => s is AssignmentStatement));
    }

    [Fact] // SB2005
    public void MissingExpression_ReportsSB2005()
    {
        Assert.Contains(DiagnosticCode.ExpectedExpression, Codes("x = ;"));
    }

    [Fact] // SB2006
    public void MalformedParameterList_ReportsSB2006_AndRecovers()
    {
        ParseResult result = ParseHelper.ParseTokensOnly("module a(1) cube(1);");
        Assert.Contains(DiagnosticCode.InvalidParameterList, result.Diagnostics.Select(d => d.Code));
        Assert.Contains(result.Root.Statements, s => s is ModuleDefinition);
    }

    [Fact] // SB2007
    public void MalformedArgumentList_ReportsSB2007_AndRecovers()
    {
        ParseResult result = ParseHelper.ParseTokensOnly("cube(1 2);");
        Assert.Contains(DiagnosticCode.InvalidArgumentList, result.Diagnostics.Select(d => d.Code));
        Assert.Contains(result.Root.Statements, s => s is ModuleInstantiation);
    }

    [Fact] // SB2006 — parameters not comma-separated
    public void ParametersWithoutSeparator_ReportsSB2006()
    {
        Assert.Contains(DiagnosticCode.InvalidParameterList, Codes("module a(x y) cube(1);"));
    }

    [Fact] // SB2001 — ternary missing ':'
    public void TernaryMissingColon_ReportsSB2001()
    {
        Assert.Contains(DiagnosticCode.ExpectedToken, Codes("x = a ? b c;"));
    }

    [Fact] // White-box: a Use token not followed by a FilePath (the lexer never emits this).
    public void UseWithoutFilePath_ReportsSB2001_AndRecovers()
    {
        var file = new SourceFile("t.scad", "use");
        var pos = new SourcePosition(0, 1, 1);
        var span = new SourceSpan(file, pos, pos);
        Token[] tokens =
        [
            new Token { Kind = TokenKind.Use, Text = "use", Span = span },
            new Token { Kind = TokenKind.Eof, Text = string.Empty, Span = span },
        ];

        ParseResult result = Parser.Parse(file, tokens);
        Assert.Contains(DiagnosticCode.ExpectedToken, result.Diagnostics.Select(d => d.Code));
        var use = Assert.IsType<UseStatement>(Assert.Single(result.Root.Statements));
        Assert.Equal(string.Empty, use.RawPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("}}}]]])))")]
    [InlineData("module")]
    [InlineData("function f(")]
    [InlineData("x = = = ;")]
    [InlineData("if if if")]
    [InlineData("[[[[[[")]
    [InlineData("a(((b)))")]
    [InlineData("for for for")]
    [InlineData("* * * ;")]
    [InlineData("cube(1) cube(2) cube(3)")]
    [InlineData(".....")]
    [InlineData("1 + + + 2")]
    [InlineData("module m(a,,b) {}")]
    public void MalformedInput_NeverThrows_AndProducesARoot(string source)
    {
        ParseResult result = ParseHelper.Parse(source);
        Assert.NotNull(result.Root);
    }
}
