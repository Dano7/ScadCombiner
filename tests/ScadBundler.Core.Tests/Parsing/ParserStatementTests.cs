using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Statement-grammar coverage: definitions, assignment, blocks, control flow, and the AST-Reference
/// §14 worked examples. Trees are asserted via the deterministic <see cref="AstDump"/>.
/// </summary>
public sealed class ParserStatementTests
{
    private static void AssertStatement(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Single(source)));

    private static void AssertFile(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Parse(source).Root));

    [Fact] // AST-Reference §14.3
    public void ModuleDefinition_WithDefaultsAndBracedBody()
    {
        AssertStatement(
            "module washer(d = 5, h = 2) {\n    cylinder(d = d, h = h);\n}",
            """
            ModuleDefinition Name="washer"
              Parameters:
                Parameter Name="d"
                  DefaultValue: NumberLiteral Value=5 RawText="5"
                Parameter Name="h"
                  DefaultValue: NumberLiteral Value=2 RawText="2"
              Body: BlockStatement
                Statements:
                  ModuleInstantiation Name="cylinder"
                    Arguments:
                      Argument Name="d"
                        Value: Identifier Name="d"
                      Argument Name="h"
                        Value: Identifier Name="h"
                    Child: null
            """);
    }

    [Fact]
    public void ModuleDefinition_WithSingleStatementBody()
    {
        AssertStatement(
            "module a() cube(1);",
            """
            ModuleDefinition Name="a"
              Parameters: []
              Body: ModuleInstantiation Name="cube"
                Arguments:
                  Argument
                    Value: NumberLiteral Value=1 RawText="1"
                Child: null
            """);
    }

    [Fact] // AST-Reference §14.4
    public void FunctionDefinition_WithTernaryBody()
    {
        AssertStatement(
            "function clamp(x, lo, hi) = x < lo ? lo : (x > hi ? hi : x);",
            """
            FunctionDefinition Name="clamp"
              Parameters:
                Parameter Name="x"
                Parameter Name="lo"
                Parameter Name="hi"
              Body: ConditionalExpression
                Condition: BinaryExpression Operator=Less
                  Left: Identifier Name="x"
                  Right: Identifier Name="lo"
                Then: Identifier Name="lo"
                Else: ParenthesizedExpression
                  Inner: ConditionalExpression
                    Condition: BinaryExpression Operator=Greater
                      Left: Identifier Name="x"
                      Right: Identifier Name="hi"
                    Then: Identifier Name="hi"
                    Else: Identifier Name="x"
            """);
    }

    [Fact] // AST-Reference §14.5
    public void IncludeAndUse_StoreRawPaths()
    {
        AssertFile(
            "include <BOSL2/std.scad>\nuse <helpers.scad>\n",
            """
            ScadFile
              IncludeStatement RawPath="BOSL2/std.scad"
              UseStatement RawPath="helpers.scad"
            """);
    }

    [Fact]
    public void Assignment_StoresNameAndValue()
    {
        AssertStatement(
            "x = 1 + 2;",
            """
            AssignmentStatement Name="x"
              Value: BinaryExpression Operator=Add
                Left: NumberLiteral Value=1 RawText="1"
                Right: NumberLiteral Value=2 RawText="2"
            """);
    }

    [Fact]
    public void EmptyStatement_IsRetained()
    {
        AssertStatement(";", "EmptyStatement");
    }

    [Fact]
    public void Block_GroupsStatements()
    {
        AssertStatement(
            "{ a(); b(); }",
            """
            BlockStatement
              Statements:
                ModuleInstantiation Name="a"
                  Arguments: []
                  Child: null
                ModuleInstantiation Name="b"
                  Arguments: []
                  Child: null
            """);
    }

    [Fact] // AST-Reference §14.8
    public void IfElseIfElse_ChainsThroughElseBranch()
    {
        AssertStatement(
            "if (n == 0) a();\nelse if (n == 1) b();\nelse c();",
            """
            IfStatement
              Condition: BinaryExpression Operator=Equal
                Left: Identifier Name="n"
                Right: NumberLiteral Value=0 RawText="0"
              Then: ModuleInstantiation Name="a"
                Arguments: []
                Child: null
              Else: IfStatement
                Condition: BinaryExpression Operator=Equal
                  Left: Identifier Name="n"
                  Right: NumberLiteral Value=1 RawText="1"
                Then: ModuleInstantiation Name="b"
                  Arguments: []
                  Child: null
                Else: ModuleInstantiation Name="c"
                  Arguments: []
                  Child: null
            """);
    }

    [Fact]
    public void If_WithoutElse_HasNullElse()
    {
        Statement statement = ParseHelper.Single("if (c) a();");
        var ifStatement = Assert.IsType<IfStatement>(statement);
        Assert.Null(ifStatement.Else);
    }

    [Fact]
    public void ParameterList_AllowsTrailingComma()
    {
        var definition = Assert.IsType<ModuleDefinition>(ParseHelper.Single("module a(x, y,) cube(1);"));
        Assert.Equal(2, definition.Parameters.Count);
    }
}
