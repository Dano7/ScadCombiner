using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Validates the visitor pattern: every concrete node's <c>Accept</c> dispatches to the matching
/// <see cref="IAstVisitor{TResult}"/> overload, and a tree-walk visits the whole tree.
/// </summary>
public sealed class AstVisitorTests
{
    [Fact]
    public void Accept_DispatchesAndCountsTheWholeSubtree()
    {
        // ScadFile + AssignmentStatement + NumberLiteral = 3 nodes.
        ScadFile root = ParseHelper.Parse("x = 1;").Root;
        var visitor = new CountingVisitor();
        Assert.Equal(3, root.Accept(visitor));
        Assert.Equal(
            new HashSet<string> { "ScadFile", "AssignmentStatement", "NumberLiteral" },
            visitor.Visited.ToHashSet());
    }

    [Fact]
    public void Walk_VisitsEverySlice2NodeType()
    {
        const string source = """
            include <a.scad>
            use <b.scad>
            module m(p = 1) { cube(p); }
            function f(x) = x < 0 ? -x : x.y[0] + g(1);
            v = [1, "s", true, undef, a];
            r = [0:2:10];
            w = (a);
            for (i = [0:2]) cube(i);
            intersection_for (j = [0:1]) cube(j);
            let (k = 1) cube(k);
            if (a) b(); else c();
            ;
            """;

        ScadFile root = ParseHelper.Parse(source).Root;
        var visitor = new CountingVisitor();
        root.Accept(visitor);

        string[] expected =
        [
            "ScadFile", "IncludeStatement", "UseStatement", "ModuleDefinition", "FunctionDefinition",
            "AssignmentStatement", "ModuleInstantiation", "BlockStatement", "IfStatement", "ForStatement",
            "IntersectionForStatement", "LetStatement", "EmptyStatement", "NumberLiteral", "StringLiteral",
            "BooleanLiteral", "UndefLiteral", "Identifier", "VectorExpression", "RangeExpression",
            "BinaryExpression", "UnaryExpression", "ConditionalExpression", "ParenthesizedExpression",
            "IndexExpression", "MemberExpression", "FunctionCallExpression", "Parameter", "Argument", "Binding",
        ];

        foreach (string typeName in expected)
        {
            Assert.Contains(typeName, visitor.Visited);
        }
    }

    [Fact]
    public void Accept_DispatchesForTheSlice3ExpressionForms()
    {
        Expression a = new Identifier("a");
        AstNode[] nodes =
        [
            new LetExpression([], a),
            new AssertExpression([], null),
            new EchoExpression([], null),
            new FunctionLiteral([], a),
            new ForComprehension([], a),
            new ForCComprehension([], a, [], a),
            new IfComprehension(a, a, null),
            new LetComprehension([], a),
            new EachExpression(a),
        ];

        var visitor = new CountingVisitor();
        foreach (AstNode node in nodes)
        {
            node.Accept(visitor);
            Assert.Contains(node.GetType().Name, visitor.Visited);
        }
    }
}
