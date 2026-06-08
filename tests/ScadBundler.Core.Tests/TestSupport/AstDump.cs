using System.Globalization;
using System.Text;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// Renders an AST into the canonical golden-master text format used by the parser corpus fixtures
/// (<c>expected.ast</c>). The format is a deterministic, indented tree isomorphic to the
/// AST-Reference §14 notation: one node per line, two-space indentation per depth, scalar fields
/// inline in the header and child nodes on labelled following lines. Spans and comment trivia are
/// omitted; <c>BlankLineBefore</c> is shown only when set.
/// </summary>
public static class AstDump
{
    /// <summary>Renders a node (and its subtree) to the golden format.</summary>
    /// <param name="node">The node to render.</param>
    /// <returns>The normalized golden text.</returns>
    public static string Dump(AstNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node, 0, label: null);
        return Normalize(sb.ToString());
    }

    /// <summary>Normalizes line endings and trailing whitespace for golden-master comparison.</summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');

    private static void Write(StringBuilder sb, AstNode node, int depth, string? label)
    {
        Indent(sb, depth);
        if (label is not null)
        {
            sb.Append(label).Append(": ");
        }

        sb.Append(Header(node)).Append('\n');
        WriteChildren(sb, node, depth + 1);
    }

    private static void WriteChildren(StringBuilder sb, AstNode node, int depth)
    {
        switch (node)
        {
            case ScadFile n:
                foreach (Statement s in n.Statements)
                {
                    Write(sb, s, depth, label: null);
                }

                break;

            case ModuleDefinition n:
                List(sb, "Parameters", n.Parameters, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case FunctionDefinition n:
                List(sb, "Parameters", n.Parameters, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case AssignmentStatement n:
                ReqChild(sb, "Value", n.Value, depth);
                break;
            case ModuleInstantiation n:
                List(sb, "Arguments", n.Arguments, depth);
                ReqChild(sb, "Child", n.Child, depth);
                break;
            case BlockStatement n:
                List(sb, "Statements", n.Statements, depth);
                break;
            case IfStatement n:
                ReqChild(sb, "Condition", n.Condition, depth);
                ReqChild(sb, "Then", n.Then, depth);
                ReqChild(sb, "Else", n.Else, depth);
                break;
            case ForStatement n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case IntersectionForStatement n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case LetStatement n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;

            case VectorExpression n:
                List(sb, "Elements", n.Elements, depth);
                break;
            case RangeExpression n:
                ReqChild(sb, "Start", n.Start, depth);
                ReqChild(sb, "Step", n.Step, depth);
                ReqChild(sb, "End", n.End, depth);
                break;
            case BinaryExpression n:
                ReqChild(sb, "Left", n.Left, depth);
                ReqChild(sb, "Right", n.Right, depth);
                break;
            case UnaryExpression n:
                ReqChild(sb, "Operand", n.Operand, depth);
                break;
            case ConditionalExpression n:
                ReqChild(sb, "Condition", n.Condition, depth);
                ReqChild(sb, "Then", n.Then, depth);
                ReqChild(sb, "Else", n.Else, depth);
                break;
            case ParenthesizedExpression n:
                ReqChild(sb, "Inner", n.Inner, depth);
                break;
            case IndexExpression n:
                ReqChild(sb, "Target", n.Target, depth);
                ReqChild(sb, "Index", n.Index, depth);
                break;
            case MemberExpression n:
                ReqChild(sb, "Target", n.Target, depth);
                break;
            case FunctionCallExpression n:
                ReqChild(sb, "Callee", n.Callee, depth);
                List(sb, "Arguments", n.Arguments, depth);
                break;

            case LetExpression n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case AssertExpression n:
                List(sb, "Arguments", n.Arguments, depth);
                OptChild(sb, "Body", n.Body, depth);
                break;
            case EchoExpression n:
                List(sb, "Arguments", n.Arguments, depth);
                OptChild(sb, "Body", n.Body, depth);
                break;
            case FunctionLiteral n:
                List(sb, "Parameters", n.Parameters, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case ForComprehension n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case ForCComprehension n:
                List(sb, "Init", n.Init, depth);
                ReqChild(sb, "Condition", n.Condition, depth);
                List(sb, "Update", n.Update, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case IfComprehension n:
                ReqChild(sb, "Condition", n.Condition, depth);
                ReqChild(sb, "Then", n.Then, depth);
                ReqChild(sb, "Else", n.Else, depth);
                break;
            case LetComprehension n:
                List(sb, "Bindings", n.Bindings, depth);
                ReqChild(sb, "Body", n.Body, depth);
                break;
            case EachExpression n:
                ReqChild(sb, "Value", n.Value, depth);
                break;

            case Parameter n:
                OptChild(sb, "DefaultValue", n.DefaultValue, depth);
                break;
            case Argument n:
                ReqChild(sb, "Value", n.Value, depth);
                break;
            case Binding n:
                ReqChild(sb, "Value", n.Value, depth);
                break;

            default:
                // Leaf nodes (literals, identifier, include/use, empty) have no children.
                break;
        }
    }

    /// <summary>Emits a child field, rendering <c>null</c> explicitly when the child is absent.</summary>
    private static void ReqChild(StringBuilder sb, string label, AstNode? child, int depth)
    {
        if (child is null)
        {
            Indent(sb, depth);
            sb.Append(label).Append(": null\n");
        }
        else
        {
            Write(sb, child, depth, label);
        }
    }

    /// <summary>Emits a child field only when present (omitting the line entirely for <c>null</c>).</summary>
    private static void OptChild(StringBuilder sb, string label, AstNode? child, int depth)
    {
        if (child is not null)
        {
            Write(sb, child, depth, label);
        }
    }

    private static void List<T>(StringBuilder sb, string label, IReadOnlyList<T> items, int depth)
        where T : AstNode
    {
        Indent(sb, depth);
        if (items.Count == 0)
        {
            sb.Append(label).Append(": []\n");
            return;
        }

        sb.Append(label).Append(":\n");
        foreach (T item in items)
        {
            Write(sb, item, depth + 1, label: null);
        }
    }

    private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    private static string Header(AstNode node)
    {
        string header = BaseHeader(node);
        return node.BlankLineBefore ? header + " BlankLineBefore=true" : header;
    }

    private static string BaseHeader(AstNode node) => node switch
    {
        ScadFile => "ScadFile",

        IncludeStatement n => $"IncludeStatement RawPath={Q(n.RawPath)}",
        UseStatement n => $"UseStatement RawPath={Q(n.RawPath)}",
        ModuleDefinition n => $"ModuleDefinition Name={Q(n.Name)}",
        FunctionDefinition n => $"FunctionDefinition Name={Q(n.Name)}",
        AssignmentStatement n => $"AssignmentStatement Name={Q(n.Name)}",
        ModuleInstantiation n => n.Modifiers.Count > 0
            ? $"ModuleInstantiation Name={Q(n.Name)} Modifiers=[{string.Join(", ", n.Modifiers)}]"
            : $"ModuleInstantiation Name={Q(n.Name)}",
        BlockStatement => "BlockStatement",
        IfStatement => "IfStatement",
        ForStatement => "ForStatement",
        IntersectionForStatement => "IntersectionForStatement",
        LetStatement => "LetStatement",
        EmptyStatement => "EmptyStatement",

        NumberLiteral n => $"NumberLiteral Value={Fmt(n.Value)} RawText={Q(n.RawText)}",
        StringLiteral n => $"StringLiteral Value={Q(n.Value)} RawText={Q(n.RawText)}",
        BooleanLiteral n => $"BooleanLiteral Value={(n.Value ? "true" : "false")}",
        UndefLiteral => "UndefLiteral",
        Identifier n => $"Identifier Name={Q(n.Name)}",
        VectorExpression => "VectorExpression",
        RangeExpression => "RangeExpression",
        BinaryExpression n => $"BinaryExpression Operator={n.Operator}",
        UnaryExpression n => $"UnaryExpression Operator={n.Operator}",
        ConditionalExpression => "ConditionalExpression",
        ParenthesizedExpression => "ParenthesizedExpression",
        IndexExpression => "IndexExpression",
        MemberExpression n => $"MemberExpression Member={Q(n.Member)}",
        FunctionCallExpression => "FunctionCallExpression",
        LetExpression => "LetExpression",
        AssertExpression => "AssertExpression",
        EchoExpression => "EchoExpression",
        FunctionLiteral => "FunctionLiteral",
        ForComprehension => "ForComprehension",
        ForCComprehension => "ForCComprehension",
        IfComprehension => "IfComprehension",
        LetComprehension => "LetComprehension",
        EachExpression => "EachExpression",

        Parameter n => $"Parameter Name={Q(n.Name)}",
        Argument n => n.Name is null ? "Argument" : $"Argument Name={Q(n.Name)}",
        Binding n => $"Binding Name={Q(n.Name)}",

        _ => node.GetType().Name,
    };

    private static string Fmt(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Q(string text)
    {
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
