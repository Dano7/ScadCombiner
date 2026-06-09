using System.Globalization;
using System.Text;
using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// Produces a deterministic canonical string for an AST node that ignores <see cref="AstNode.Span"/>,
/// trivia, and <see cref="AstNode.BlankLineBefore"/> — the "content + signature" key the inliner uses
/// to recognize structurally-identical definitions arriving via multiple include/use paths (dedup,
/// SB5005). Two nodes share a key iff they are semantically identical. String fields are
/// length-prefixed so the encoding is unambiguous without escaping.
/// </summary>
internal sealed class StructuralKey : IAstVisitor<string>
{
    private static readonly StructuralKey Instance = new();

    private StructuralKey()
    {
    }

    /// <summary>The canonical key for <paramref name="node"/>.</summary>
    /// <param name="node">The node to key.</param>
    /// <returns>A deterministic span/trivia-free string.</returns>
    public static string Of(AstNode node) => node.Accept(Instance);

    public string Visit(ScadFile node) => $"File[{List(node.Statements)}]";

    public string Visit(IncludeStatement node) => $"Include[{Str(node.RawPath)}]";

    public string Visit(UseStatement node) => $"Use[{Str(node.RawPath)}]";

    public string Visit(ModuleDefinition node) =>
        $"Module[{Str(node.Name)};{List(node.Parameters)};{Child(node.Body)}]";

    public string Visit(FunctionDefinition node) =>
        $"Function[{Str(node.Name)};{List(node.Parameters)};{Child(node.Body)}]";

    public string Visit(AssignmentStatement node) =>
        $"Assign[{Str(node.Name)};{Child(node.Value)}]";

    public string Visit(ModuleInstantiation node) =>
        $"Inst[{Modifiers(node.Modifiers)};{Str(node.Name)};{List(node.Arguments)};{Child(node.Child)}]";

    public string Visit(BlockStatement node) => $"Block[{List(node.Statements)}]";

    public string Visit(IfStatement node) =>
        $"If[{Child(node.Condition)};{Child(node.Then)};{Child(node.Else)}]";

    public string Visit(ForStatement node) => $"For[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(IntersectionForStatement node) =>
        $"IntersectionFor[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(LetStatement node) => $"LetStmt[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(EmptyStatement node) => "Empty[]";

    public string Visit(NumberLiteral node) =>
        $"Num[{node.Value.ToString("R", CultureInfo.InvariantCulture)};{Str(node.RawText)}]";

    public string Visit(StringLiteral node) => $"Strn[{Str(node.Value)}]";

    public string Visit(BooleanLiteral node) => $"Bool[{(node.Value ? "1" : "0")}]";

    public string Visit(UndefLiteral node) => "Undef[]";

    public string Visit(Identifier node) => $"Ident[{Str(node.Name)}]";

    public string Visit(VectorExpression node) => $"Vector[{List(node.Elements)}]";

    public string Visit(RangeExpression node) =>
        $"Range[{Child(node.Start)};{Child(node.Step)};{Child(node.End)}]";

    public string Visit(BinaryExpression node) =>
        $"Binary[{node.Operator};{Child(node.Left)};{Child(node.Right)}]";

    public string Visit(UnaryExpression node) => $"Unary[{node.Operator};{Child(node.Operand)}]";

    public string Visit(ConditionalExpression node) =>
        $"Cond[{Child(node.Condition)};{Child(node.Then)};{Child(node.Else)}]";

    public string Visit(ParenthesizedExpression node) => $"Paren[{Child(node.Inner)}]";

    public string Visit(IndexExpression node) => $"Index[{Child(node.Target)};{Child(node.Index)}]";

    public string Visit(MemberExpression node) => $"Member[{Child(node.Target)};{Str(node.Member)}]";

    public string Visit(FunctionCallExpression node) =>
        $"Call[{Child(node.Callee)};{List(node.Arguments)}]";

    public string Visit(LetExpression node) => $"LetExpr[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(AssertExpression node) =>
        $"Assert[{List(node.Arguments)};{Child(node.Body)}]";

    public string Visit(EchoExpression node) => $"Echo[{List(node.Arguments)};{Child(node.Body)}]";

    public string Visit(FunctionLiteral node) =>
        $"Lambda[{List(node.Parameters)};{Child(node.Body)}]";

    public string Visit(ForComprehension node) =>
        $"ForC[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(ForCComprehension node) =>
        $"ForCStyle[{List(node.Init)};{Child(node.Condition)};{List(node.Update)};{Child(node.Body)}]";

    public string Visit(IfComprehension node) =>
        $"IfC[{Child(node.Condition)};{Child(node.Then)};{Child(node.Else)}]";

    public string Visit(LetComprehension node) =>
        $"LetC[{List(node.Bindings)};{Child(node.Body)}]";

    public string Visit(EachExpression node) => $"Each[{Child(node.Value)}]";

    public string Visit(Parameter node) =>
        $"Param[{Str(node.Name)};{Child(node.DefaultValue)}]";

    public string Visit(Argument node) => $"Arg[{Str(node.Name)};{Child(node.Value)}]";

    public string Visit(Binding node) => $"Bind[{Str(node.Name)};{Child(node.Value)}]";

    private static string Str(string? value) =>
        value is null ? "_" : $"{value.Length.ToString(CultureInfo.InvariantCulture)}#{value}";

    private string Child(AstNode? node) => node is null ? "_" : node.Accept(this);

    private string List<T>(IReadOnlyList<T> nodes)
        where T : AstNode
    {
        var builder = new StringBuilder("{");
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(nodes[i].Accept(this));
        }

        return builder.Append('}').ToString();
    }

    private static string Modifiers(IReadOnlyList<InstantiationModifier> modifiers) =>
        string.Join("+", modifiers);
}
