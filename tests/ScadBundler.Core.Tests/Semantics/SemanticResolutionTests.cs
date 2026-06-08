using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// Single-file reference resolution (§5): top-level reads bind to renameable symbols; locals,
/// parameters, special variables, and built-ins bind to <c>null</c>; and <c>ReferencesTo</c> returns
/// exactly the references bound to a declaration.
/// </summary>
public sealed class SemanticResolutionTests
{
    [Fact]
    public void TopLevelVariableRead_ResolvesToVariableSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("WALL = 2;\nx = WALL;");
        var read = (Identifier)((AssignmentStatement)ast.Statements[1]).Value;

        Symbol? symbol = result.Model.Resolve(read);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal("WALL", symbol.Name);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void ParameterRead_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = 5;\ny = x;\nmodule m(x) cube(x);");
        var topLevelRead = (Identifier)((AssignmentStatement)ast.Statements[1]).Value;
        var module = (ModuleDefinition)ast.Statements[2];
        var cube = (ModuleInstantiation)module.Body;
        var parameterRead = (Identifier)cube.Arguments[0].Value;

        Assert.NotNull(result.Model.Resolve(topLevelRead)); // top-level x
        Assert.Null(result.Model.Resolve(parameterRead));   // shadowed by the parameter → local
    }

    [Fact]
    public void LetBinding_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("n = 9;\nv = let (n = 1) n;");
        var let = (LetExpression)((AssignmentStatement)ast.Statements[1]).Value;
        var bodyRead = (Identifier)let.Body;

        Assert.Null(result.Model.Resolve(bodyRead));
    }

    [Fact]
    public void ForBinding_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("i = 9;\nfor (i = [0:2]) cube(i);");
        var loop = (ForStatement)ast.Statements[1];
        var cube = (ModuleInstantiation)loop.Body;
        var loopRead = (Identifier)cube.Arguments[0].Value;

        Assert.Null(result.Model.Resolve(loopRead));
    }

    [Fact]
    public void LetBindingValue_SeesOuterScope_NotItsOwnBinding()
    {
        // In `let (n = n) …` the binding's RHS is the OUTER n (sequential binding), so it must bind to
        // the top-level symbol — the inliner has to rewrite it on rename, but not the body's local n.
        var (ast, result) = SemanticHelper.AnalyzeFile("n = 9;\nv = let (n = n) n;");
        var let = (LetExpression)((AssignmentStatement)ast.Statements[1]).Value;
        var bindingRhs = (Identifier)let.Bindings[0].Value;
        var bodyRead = (Identifier)let.Body;

        Assert.NotNull(result.Model.Resolve(bindingRhs)); // outer n
        Assert.Null(result.Model.Resolve(bodyRead));      // let-local n
    }

    [Fact]
    public void SpecialVariableRead_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = $fn;");
        var read = (Identifier)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(read));
    }

    [Fact]
    public void BuiltinModuleCall_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("cube(5);");
        Assert.Null(result.Model.Resolve(ast.Statements[0]));
    }

    [Fact]
    public void BuiltinFunctionCall_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = sin(30);");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(call.Callee));
    }

    [Fact]
    public void UserModuleCall_ResolvesToModuleSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module m() cube(1);\nm();");
        Symbol? symbol = result.Model.Resolve(ast.Statements[1]);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Module, symbol.Kind);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void UserFunctionCall_ResolvesToFunctionSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("function f() = 1;\nx = f();");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[1]).Value;
        Symbol? symbol = result.Model.Resolve(call.Callee);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void ForwardReference_ResolvesToLaterDefinition()
    {
        // OpenSCAD collects all top-level definitions before evaluation, so calls may precede defs.
        var (ast, result) = SemanticHelper.AnalyzeFile("m();\nmodule m() cube(1);");
        Symbol? symbol = result.Model.Resolve(ast.Statements[0]);
        Assert.NotNull(symbol);
        Assert.Same(ast.Statements[1], symbol.Declaration);
    }

    [Fact]
    public void Recursion_BindsToOwnTopLevelSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module m() m();");
        var module = (ModuleDefinition)ast.Statements[0];
        var selfCall = (ModuleInstantiation)module.Body;
        Symbol? symbol = result.Model.Resolve(selfCall);
        Assert.NotNull(symbol);
        Assert.Same(module, symbol.Declaration);
    }

    [Fact]
    public void NestedDefinition_ShadowsTopLevel_CallResolvesToNull()
    {
        // A module defined inside another's body is local; a call to it is not a renameable symbol.
        var (ast, result) = SemanticHelper.AnalyzeFile("module inner() sphere(1);\nmodule outer() { module inner() cube(1); inner(); }");
        var outer = (ModuleDefinition)ast.Statements[1];
        var block = (BlockStatement)outer.Body;
        var innerCall = (ModuleInstantiation)block.Statements[1];
        Assert.Null(result.Model.Resolve(innerCall));
    }

    [Fact]
    public void ReferencesTo_ReturnsExactlyTheBoundReferences()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("WALL = 2;\nGAP = 9;\na = WALL;\nb = WALL;\nc = GAP;");
        var firstRead = (Identifier)((AssignmentStatement)ast.Statements[2]).Value;
        Symbol wall = result.Model.Resolve(firstRead)!;

        IReadOnlyList<AstNode> references = result.Model.ReferencesTo(wall);
        Assert.Equal(2, references.Count);
        Assert.All(references, r => Assert.Equal("WALL", Assert.IsType<Identifier>(r).Name));

        var gapRead = (Identifier)((AssignmentStatement)ast.Statements[4]).Value;
        Assert.DoesNotContain(gapRead, references);
    }

    [Fact]
    public void ReferencesTo_UnreferencedDeclaration_IsEmpty()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module unused() cube(1);\ncube(2);");
        var unused = (ModuleDefinition)ast.Statements[0];
        var symbol = new Symbol(SymbolKind.Module, "unused", ast.Source, unused);
        Assert.Empty(result.Model.ReferencesTo(symbol));
    }

    [Fact]
    public void ImmediatelyInvokedFunctionLiteral_CalleeWalkedNotResolvedAsName()
    {
        // `(function (x) x)(5)` — the callee is an expression, not a name; analysis must not throw and
        // must not invent a symbol for it.
        var (ast, result) = SemanticHelper.AnalyzeFile("v = (function (x) x)(5);");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(call.Callee));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "SB3005");
    }
}
