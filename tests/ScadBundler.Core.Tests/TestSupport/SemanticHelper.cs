using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>Convenience helpers for driving the <see cref="SemanticAnalyzer"/> in tests: single-file
/// analysis, multi-file <see cref="LoadGraph"/> construction (edges resolved by raw path), and AST
/// node search.</summary>
public static class SemanticHelper
{
    /// <summary>Parses <paramref name="source"/> and analyzes it as a single isolated file.</summary>
    public static SemanticResult Analyze(string source, string path = "main.scad") =>
        SemanticAnalyzer.Analyze(ParseFile(source, path));

    /// <summary>Parses <paramref name="source"/> and returns both the AST and the analysis result.</summary>
    public static (ScadFile Ast, SemanticResult Result) AnalyzeFile(string source, string path = "main.scad")
    {
        ScadFile ast = ParseFile(source, path);
        return (ast, SemanticAnalyzer.Analyze(ast));
    }

    /// <summary>Parses <paramref name="source"/> into a <see cref="ScadFile"/> (no analysis).</summary>
    public static ScadFile ParseFile(string source, string path = "main.scad") =>
        Parser.Parse(new SourceFile(path, source)).Root;

    /// <summary>
    /// Builds a <see cref="LoadGraph"/> from named files (the first is the root), resolving
    /// <c>include</c>/<c>use</c> edges by matching each statement's raw path to a file name. A
    /// <c>.ttf</c>/<c>.otf</c> <c>use</c> becomes a font pass-through; an unmatched path stays
    /// unresolved (mirroring a missing file).
    /// </summary>
    public static LoadGraph Graph(params (string Name, string Source)[] files)
    {
        var parsed = new Dictionary<string, ScadFile>(StringComparer.Ordinal);
        foreach ((string name, string source) in files)
        {
            parsed[name] = ParseFile(source, name);
        }

        var built = new Dictionary<string, LoadedFile>(StringComparer.Ordinal);

        LoadedFile Build(string name)
        {
            if (built.TryGetValue(name, out LoadedFile? existing))
            {
                return existing;
            }

            ScadFile ast = parsed[name];
            var includes = new List<IncludeEdge>();
            var uses = new List<UseEdge>();
            foreach (Statement statement in ast.Statements)
            {
                switch (statement)
                {
                    case IncludeStatement include:
                        includes.Add(new IncludeEdge(
                            include, parsed.ContainsKey(include.RawPath) ? Build(include.RawPath) : null));
                        break;
                    case UseStatement use when IsFont(use.RawPath):
                        uses.Add(new UseEdge(use, null, FontPassthrough: true));
                        break;
                    case UseStatement use:
                        uses.Add(new UseEdge(
                            use, parsed.ContainsKey(use.RawPath) ? Build(use.RawPath) : null));
                        break;
                }
            }

            var loaded = new LoadedFile(ast.Source, ast, includes, uses);
            built[name] = loaded;
            return loaded;
        }

        LoadedFile root = Build(files[0].Name);
        foreach ((string name, _) in files)
        {
            Build(name);
        }

        return new LoadGraph(root, built, []);
    }

    /// <summary>Builds the graph from <paramref name="files"/> and analyzes it.</summary>
    public static (LoadGraph Graph, SemanticResult Result) AnalyzeGraph(params (string Name, string Source)[] files)
    {
        LoadGraph graph = Graph(files);
        return (graph, SemanticAnalyzer.Analyze(graph));
    }

    /// <summary>Every diagnostic code in <paramref name="result"/>, in order.</summary>
    public static IReadOnlyList<string> Codes(SemanticResult result) =>
        [.. result.Diagnostics.Select(d => d.Code)];

    /// <summary>The node itself and all of its descendants, depth-first in source order.</summary>
    public static IEnumerable<AstNode> Descendants(AstNode node)
    {
        yield return node;
        foreach (AstNode child in Children(node))
        {
            foreach (AstNode descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>The first descendant of <paramref name="root"/> of type <typeparamref name="T"/>
    /// matching <paramref name="predicate"/>.</summary>
    public static T Find<T>(AstNode root, Func<T, bool>? predicate = null)
        where T : AstNode =>
        Descendants(root).OfType<T>().First(n => predicate is null || predicate(n));

    private static bool IsFont(string path) =>
        path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<AstNode> Children(AstNode node)
    {
        switch (node)
        {
            case ScadFile n: return n.Statements;
            case ModuleDefinition n: return [.. n.Parameters, n.Body];
            case FunctionDefinition n: return [.. n.Parameters, n.Body];
            case AssignmentStatement n: return [n.Value];
            case ModuleInstantiation n: return n.Child is null ? n.Arguments : [.. n.Arguments, n.Child];
            case BlockStatement n: return n.Statements;
            case IfStatement n: return n.Else is null ? [n.Condition, n.Then] : [n.Condition, n.Then, n.Else];
            case ForStatement n: return [.. n.Bindings, n.Body];
            case IntersectionForStatement n: return [.. n.Bindings, n.Body];
            case LetStatement n: return [.. n.Bindings, n.Body];
            case VectorExpression n: return n.Elements;
            case RangeExpression n: return n.Step is null ? [n.Start, n.End] : [n.Start, n.Step, n.End];
            case BinaryExpression n: return [n.Left, n.Right];
            case UnaryExpression n: return [n.Operand];
            case ConditionalExpression n: return [n.Condition, n.Then, n.Else];
            case ParenthesizedExpression n: return [n.Inner];
            case IndexExpression n: return [n.Target, n.Index];
            case MemberExpression n: return [n.Target];
            case FunctionCallExpression n: return [n.Callee, .. n.Arguments];
            case LetExpression n: return [.. n.Bindings, n.Body];
            case AssertExpression n: return n.Body is null ? n.Arguments : [.. n.Arguments, n.Body];
            case EchoExpression n: return n.Body is null ? n.Arguments : [.. n.Arguments, n.Body];
            case FunctionLiteral n: return [.. n.Parameters, n.Body];
            case ForComprehension n: return [.. n.Bindings, n.Body];
            case ForCComprehension n: return [.. n.Init, n.Condition, .. n.Update, n.Body];
            case IfComprehension n: return n.Else is null ? [n.Condition, n.Then] : [n.Condition, n.Then, n.Else];
            case LetComprehension n: return [.. n.Bindings, n.Body];
            case EachExpression n: return [n.Value];
            case Parameter n: return n.DefaultValue is null ? [] : [n.DefaultValue];
            case Argument n: return [n.Value];
            case Binding n: return [n.Value];
            default: return [];
        }
    }
}
