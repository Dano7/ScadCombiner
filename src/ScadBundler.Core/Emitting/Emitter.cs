using System.Text;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Emitting;

/// <summary>
/// Renders a <see cref="ScadFile"/> AST back to valid, well-formatted OpenSCAD text. The emitter is a
/// deterministic recursive walk over the tree: numbers and strings come from their preserved
/// <c>RawText</c>, author parentheses are kept, and minimal parentheses are inserted around any child
/// whose operator precedence is looser than its position allows so the re-parsed tree is identical
/// (needed for the synthesized rename/normalize nodes the inliner produces). Output is deterministic for
/// a given AST + <see cref="EmitOptions"/>, and idempotent: <c>Emit(Parse(Emit(ast))) == Emit(ast)</c>.
/// </summary>
public sealed class Emitter
{
    // Precedence levels, aligned with the parser's binding powers (docs/Parser-Planning.md). Higher
    // binds tighter. A child is parenthesized when its precedence is below the threshold its slot
    // requires; the natural parse of any author-written tree already satisfies every threshold, so
    // parentheses are only ever added around the looser synthesized subtrees a transform can create.
    private const int PrecPrimary = 120;
    private const int PrecPostfix = 110;
    private const int PrecPower = 100;
    private const int PrecUnary = 95;
    private const int PrecConditional = 5;
    private const int PrecKeywordExpr = 3;

    private readonly EmitOptions _options;
    private readonly StringBuilder _builder = new();

    private Emitter(EmitOptions options) => _options = options;

    private bool Min => _options.Minify;

    /// <summary>Renders <paramref name="file"/> to OpenSCAD text. Deterministic for a given AST + options.</summary>
    /// <param name="file">The AST to render (typically a bundle from the inliner).</param>
    /// <param name="options">Formatting options; <see cref="EmitOptions.Default"/> when <c>null</c>.</param>
    /// <returns>The emitted source text.</returns>
    public static string Emit(ScadFile file, EmitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new Emitter(options ?? EmitOptions.Default).Run(file);
    }

    /// <summary>
    /// The emitter self-check (SB6001 guard, debug/tests): emits <paramref name="file"/>, re-parses the
    /// text, and reports whether it round-trips to a structurally-identical AST (ignoring spans/trivia).
    /// </summary>
    /// <param name="file">The AST to round-trip.</param>
    /// <param name="options">The emit options to use.</param>
    /// <returns><c>true</c> when the emitted text re-parses to the same structure.</returns>
    internal static bool RoundTripsStructurally(ScadFile file, EmitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        string emitted = Emit(file, options);
        ParseResult reparsed = Parser.Parse(new SourceFile(file.Source.Path, emitted));
        return StructuralKey.Of(file) == StructuralKey.Of(reparsed.Root);
    }

    private string Run(ScadFile file)
    {
        EmitStatementList(file.Statements, 0);
        EmitFileTrailingTrivia(file);
        return _builder.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Statements
    // ---------------------------------------------------------------------------------------------

    private void EmitStatementList(IReadOnlyList<Statement> statements, int level)
    {
        foreach (Statement statement in statements)
        {
            if (Min)
            {
                EmitLeadingTrivia(statement, level); // sticky-only under minify: the license header + Customizer fence survive
                EmitStatementCore(statement, level);
                if (level == 0)
                {
                    // One top-level statement per line so OpenSCAD's line-based Customizer extraction
                    // (getLineToStop) still sees the hoisted parameter prologue above the first '{'.
                    _builder.Append('\n');
                }
            }
            else
            {
                EmitStatementLine(statement, level);
            }
        }
    }

    // A statement occupying its own line(s): blank-line marker, leading comments, indent, content, then
    // a line break. Suppresses the blank line at the very top of the output so files never start blank.
    private void EmitStatementLine(Statement statement, int level)
    {
        if (statement.BlankLineBefore && _builder.Length > 0)
        {
            NewLine();
        }

        EmitLeadingTrivia(statement, level);
        WriteIndent(level);
        EmitStatementCore(statement, level);
        NewLine();
    }

    // The statement's content plus its own trailing same-line comment. No leading trivia, indent, or
    // newline — so this also serves inline positions (a chained child, a single-statement body, an
    // else branch), where leading comments and line breaks would be wrong.
    private void EmitStatementCore(Statement statement, int level)
    {
        switch (statement)
        {
            case IncludeStatement include:
                Write($"include <{include.RawPath}>");
                break;
            case UseStatement use:
                Write($"use <{use.RawPath}>");
                break;
            case ModuleDefinition module:
                Write("module ");
                Write(module.Name);
                EmitParameterList(module.Parameters);
                EmitBodyClause(module.Body, level);
                break;
            case FunctionDefinition function:
                Write("function ");
                Write(function.Name);
                EmitParameterList(function.Parameters);
                WriteOperator("=");
                EmitExpression(function.Body, 0);
                Write(";");
                break;
            case AssignmentStatement assignment:
                Write(assignment.Name);
                WriteOperator("=");
                EmitExpression(assignment.Value, 0);
                Write(";");
                break;
            case ModuleInstantiation instantiation:
                EmitInstantiation(instantiation, level);
                break;
            case BlockStatement block:
                EmitBlock(block, level);
                break;
            case IfStatement branch:
                EmitIf(branch, level);
                break;
            case ForStatement loop:
                Write("for");
                EmitBindingList(loop.Bindings);
                EmitBodyClause(loop.Body, level);
                break;
            case IntersectionForStatement loop:
                Write("intersection_for");
                EmitBindingList(loop.Bindings);
                EmitBodyClause(loop.Body, level);
                break;
            case LetStatement let:
                Write("let");
                EmitBindingList(let.Bindings);
                EmitBodyClause(let.Body, level);
                break;
            case EmptyStatement:
                Write(";");
                break;
            default:
                throw new InvalidOperationException($"Unhandled statement node: {statement.GetType().Name}");
        }

        EmitTrailingTrivia(statement);
    }

    private void EmitInstantiation(ModuleInstantiation instantiation, int level)
    {
        foreach (InstantiationModifier modifier in instantiation.Modifiers)
        {
            Write(ModifierSymbol(modifier));
        }

        Write(instantiation.Name);
        EmitArguments(instantiation.Arguments);
        if (instantiation.Child is null)
        {
            Write(";");
        }
        else
        {
            EmitBodyClause(instantiation.Child, level);
        }
    }

    private void EmitIf(IfStatement branch, int level)
    {
        Write("if");
        Write("(");
        EmitExpression(branch.Condition, 0);
        Write(")");
        EmitBodyClause(branch.Then, level);
        if (branch.Else is not null)
        {
            if (Min)
            {
                Write("else");
            }
            else
            {
                Write(" ");
                Write("else");
            }

            EmitBodyClause(branch.Else, level);
        }
    }

    // The statement that follows a header on the same line: a braced body, a lone `;` (empty body), or
    // a single inline statement after one space.
    private void EmitBodyClause(Statement body, int level)
    {
        switch (body)
        {
            case BlockStatement block:
                EmitBraceClause(block, level);
                break;
            case EmptyStatement:
                Write(";");
                break;
            default:
                Space();
                EmitStatementCore(body, level);
                break;
        }
    }

    private void EmitBraceClause(BlockStatement block, int level)
    {
        if (!Min)
        {
            if (_options.BraceStyle == BraceStyle.SameLine)
            {
                Write(" ");
            }
            else
            {
                NewLine();
                WriteIndent(level);
            }
        }

        EmitBlock(block, level);
    }

    private void EmitBlock(BlockStatement block, int level)
    {
        Write("{");
        if (Min)
        {
            EmitStatementList(block.Statements, level + 1);
            Write("}");
            return;
        }

        NewLine();
        EmitStatementList(block.Statements, level + 1);
        WriteIndent(level);
        Write("}");
    }

    // ---------------------------------------------------------------------------------------------
    // Expressions
    // ---------------------------------------------------------------------------------------------

    // Emits an expression, wrapping it in parentheses when its precedence is below what its slot allows.
    private void EmitExpression(Expression expression, int minPrecedence)
    {
        bool parenthesize = Precedence(expression) < minPrecedence;
        if (parenthesize)
        {
            Write("(");
        }

        EmitExpressionCore(expression);

        if (parenthesize)
        {
            Write(")");
        }
    }

    private void EmitExpressionCore(Expression expression)
    {
        switch (expression)
        {
            case NumberLiteral number:
                Write(number.RawText);
                break;
            case StringLiteral text:
                Write(text.RawText);
                break;
            case BooleanLiteral boolean:
                Write(boolean.Value ? "true" : "false");
                break;
            case UndefLiteral:
                Write("undef");
                break;
            case Identifier identifier:
                Write(identifier.Name);
                break;
            case VectorExpression vector:
                EmitVector(vector);
                break;
            case RangeExpression range:
                EmitRange(range);
                break;
            case BinaryExpression binary:
                EmitBinary(binary);
                break;
            case UnaryExpression unary:
                Write(UnarySymbol(unary.Operator));
                EmitExpression(unary.Operand, PrecUnary);
                break;
            case ConditionalExpression conditional:
                EmitExpression(conditional.Condition, PrecConditional + 1);
                WriteOperator("?");
                EmitExpression(conditional.Then, 0);
                WriteOperator(":");
                EmitExpression(conditional.Else, 0);
                break;
            case ParenthesizedExpression parenthesized:
                Write("(");
                EmitExpression(parenthesized.Inner, 0);
                Write(")");
                break;
            case IndexExpression index:
                EmitExpression(index.Target, PrecPostfix);
                Write("[");
                EmitExpression(index.Index, 0);
                Write("]");
                break;
            case MemberExpression member:
                EmitExpression(member.Target, PrecPostfix);
                Write(".");
                Write(member.Member);
                break;
            case FunctionCallExpression call:
                EmitExpression(call.Callee, PrecPostfix);
                EmitArguments(call.Arguments);
                break;
            case LetExpression let:
                Write("let");
                EmitBindingList(let.Bindings);
                Space();
                EmitExpression(let.Body, 0);
                break;
            case AssertExpression assert:
                Write("assert");
                EmitArguments(assert.Arguments);
                if (assert.Body is not null)
                {
                    Space();
                    EmitExpression(assert.Body, 0);
                }

                break;
            case EchoExpression echo:
                Write("echo");
                EmitArguments(echo.Arguments);
                if (echo.Body is not null)
                {
                    Space();
                    EmitExpression(echo.Body, 0);
                }

                break;
            case FunctionLiteral literal:
                Write("function");
                EmitParameterList(literal.Parameters);
                Space();
                EmitExpression(literal.Body, 0);
                break;
            case ForComprehension comprehension:
                Write("for");
                EmitBindingList(comprehension.Bindings);
                Space();
                EmitExpression(comprehension.Body, 0);
                break;
            case ForCComprehension comprehension:
                EmitForC(comprehension);
                break;
            case IfComprehension comprehension:
                EmitIfComprehension(comprehension);
                break;
            case LetComprehension comprehension:
                Write("let");
                EmitBindingList(comprehension.Bindings);
                Space();
                EmitExpression(comprehension.Body, 0);
                break;
            case EachExpression each:
                Write("each");
                Space();
                EmitExpression(each.Value, 0);
                break;
            default:
                throw new InvalidOperationException($"Unhandled expression node: {expression.GetType().Name}");
        }
    }

    private void EmitBinary(BinaryExpression binary)
    {
        int bindingPower = BinaryBindingPower(binary.Operator);
        int leftThreshold;
        int rightThreshold;
        if (binary.Operator == BinaryOperator.Power)
        {
            // `^` is right-associative; its left operand parses at the postfix level and its right at
            // the unary level (so `2^-1` and `a^b^c` need no parentheses).
            leftThreshold = PrecPower + 1;
            rightThreshold = PrecUnary;
        }
        else
        {
            leftThreshold = bindingPower;
            rightThreshold = bindingPower + 1;
        }

        EmitExpression(binary.Left, leftThreshold);
        WriteOperator(BinarySymbol(binary.Operator));
        EmitExpression(binary.Right, rightThreshold);
    }

    private void EmitVector(VectorExpression vector)
    {
        Write("[");
        for (int i = 0; i < vector.Elements.Count; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            EmitExpression(vector.Elements[i], 0);
        }

        Write("]");
    }

    private void EmitRange(RangeExpression range)
    {
        Write("[");
        EmitExpression(range.Start, 0);
        Write(":");
        if (range.Step is not null)
        {
            EmitExpression(range.Step, 0);
            Write(":");
        }

        EmitExpression(range.End, 0);
        Write("]");
    }

    private void EmitForC(ForCComprehension comprehension)
    {
        Write("for");
        Write("(");
        EmitBindings(comprehension.Init);
        Write(";");
        Space();
        EmitExpression(comprehension.Condition, 0);
        Write(";");
        Space();
        EmitBindings(comprehension.Update);
        Write(")");
        Space();
        EmitExpression(comprehension.Body, 0);
    }

    private void EmitIfComprehension(IfComprehension comprehension)
    {
        Write("if");
        Write("(");
        EmitExpression(comprehension.Condition, 0);
        Write(")");
        Space();
        EmitExpression(comprehension.Then, 0);
        if (comprehension.Else is not null)
        {
            if (Min)
            {
                Write("else");
            }
            else
            {
                Write(" ");
                Write("else");
                Write(" ");
            }

            EmitExpression(comprehension.Else, 0);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Parameter / argument / binding lists
    // ---------------------------------------------------------------------------------------------

    private void EmitParameterList(IReadOnlyList<Parameter> parameters)
    {
        Write("(");
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            Write(parameters[i].Name);
            if (parameters[i].DefaultValue is not null)
            {
                WriteOperator("=");
                EmitExpression(parameters[i].DefaultValue!, 0);
            }
        }

        Write(")");
    }

    private void EmitArguments(IReadOnlyList<Argument> arguments)
    {
        Write("(");
        for (int i = 0; i < arguments.Count; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            if (arguments[i].Name is not null)
            {
                Write(arguments[i].Name!);
                WriteOperator("=");
            }

            EmitExpression(arguments[i].Value, 0);
        }

        Write(")");
    }

    private void EmitBindingList(IReadOnlyList<Binding> bindings)
    {
        Write("(");
        EmitBindings(bindings);
        Write(")");
    }

    private void EmitBindings(IReadOnlyList<Binding> bindings)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            if (bindings[i].Name.Length > 0)
            {
                Write(bindings[i].Name);
                WriteOperator("=");
            }

            EmitExpression(bindings[i].Value, 0);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Trivia
    // ---------------------------------------------------------------------------------------------

    private void EmitLeadingTrivia(AstNode node, int level)
    {
        bool stripping = Min || !_options.PreserveComments;
        foreach (Trivia trivia in node.LeadingTrivia)
        {
            if (trivia is not CommentTrivia comment || (stripping && !comment.Sticky))
            {
                continue; // stripping modes keep only sticky trivia (license header + Customizer fence)
            }

            if (!Min)
            {
                WriteIndent(level);
            }

            _builder.Append(comment.Text);
            _builder.Append('\n'); // a real newline even under minify: separates the sticky header/fence and terminates a line comment
        }
    }

    private void EmitTrailingTrivia(AstNode node)
    {
        bool stripping = Min || !_options.PreserveComments;
        foreach (Trivia trivia in node.TrailingTrivia)
        {
            if (trivia is not CommentTrivia comment || (stripping && !comment.Sticky))
            {
                continue; // stripping modes keep only sticky trivia (the Customizer parameter annotation)
            }

            _builder.Append("  ");
            _builder.Append(comment.Text);
        }
    }

    private void EmitFileTrailingTrivia(ScadFile file)
    {
        if (Min || !_options.PreserveComments)
        {
            return;
        }

        foreach (Trivia trivia in file.TrailingTrivia)
        {
            if (trivia is CommentTrivia comment)
            {
                _builder.Append(comment.Text);
                NewLine();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Low-level writers
    // ---------------------------------------------------------------------------------------------

    // Appends a token piece, inserting a separating space in minify mode when the previous and next
    // characters would otherwise merge into a single token (two identifier/number characters).
    private void Write(string text)
    {
        if (Min && _builder.Length > 0 && text.Length > 0
            && IsWordChar(_builder[^1]) && IsWordChar(text[0]))
        {
            _builder.Append(' ');
        }

        _builder.Append(text);
    }

    // A binary/assignment/ternary operator: spaced in pretty mode, bare in minify.
    private void WriteOperator(string op)
    {
        if (Min)
        {
            Write(op);
            return;
        }

        _builder.Append(' ');
        _builder.Append(op);
        _builder.Append(' ');
    }

    private void Space()
    {
        if (!Min)
        {
            _builder.Append(' ');
        }
    }

    private void NewLine()
    {
        if (!Min)
        {
            _builder.Append('\n');
        }
    }

    private void WriteIndent(int level)
    {
        if (Min)
        {
            return;
        }

        if (_options.IndentStyle == IndentStyle.Tabs)
        {
            _builder.Append('\t', level);
        }
        else
        {
            _builder.Append(' ', level * _options.IndentWidth);
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static int Precedence(Expression expression) => expression switch
    {
        BinaryExpression binary => BinaryBindingPower(binary.Operator),
        UnaryExpression => PrecUnary,
        ConditionalExpression => PrecConditional,
        IndexExpression or MemberExpression or FunctionCallExpression => PrecPostfix,
        LetExpression or AssertExpression or EchoExpression or FunctionLiteral => PrecKeywordExpr,
        ForComprehension or ForCComprehension or IfComprehension or LetComprehension or EachExpression
            => PrecKeywordExpr,
        _ => PrecPrimary, // literals, identifiers, vectors, ranges, parenthesized
    };

    private static int BinaryBindingPower(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => 10,
        BinaryOperator.And => 20,
        BinaryOperator.Equal or BinaryOperator.NotEqual => 30,
        BinaryOperator.Less or BinaryOperator.LessEqual
            or BinaryOperator.Greater or BinaryOperator.GreaterEqual => 40,
        BinaryOperator.BitwiseOr => 50,
        BinaryOperator.BitwiseAnd => 60,
        BinaryOperator.ShiftLeft or BinaryOperator.ShiftRight => 70,
        BinaryOperator.Add or BinaryOperator.Subtract => 80,
        BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => 90,
        BinaryOperator.Power => PrecPower,
        _ => PrecPrimary,
    };

    private static string BinarySymbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Power => "^",
        BinaryOperator.Less => "<",
        BinaryOperator.LessEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterEqual => ">=",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.And => "&&",
        BinaryOperator.Or => "||",
        BinaryOperator.BitwiseAnd => "&",
        BinaryOperator.BitwiseOr => "|",
        BinaryOperator.ShiftLeft => "<<",
        BinaryOperator.ShiftRight => ">>",
        _ => throw new InvalidOperationException($"Unhandled binary operator: {op}"),
    };

    private static string UnarySymbol(UnaryOperator op) => op switch
    {
        UnaryOperator.Negate => "-",
        UnaryOperator.Plus => "+",
        UnaryOperator.Not => "!",
        UnaryOperator.BitwiseNot => "~",
        _ => throw new InvalidOperationException($"Unhandled unary operator: {op}"),
    };

    private static string ModifierSymbol(InstantiationModifier modifier) => modifier switch
    {
        InstantiationModifier.Disable => "*",
        InstantiationModifier.Root => "!",
        InstantiationModifier.Highlight => "#",
        InstantiationModifier.Background => "%",
        _ => throw new InvalidOperationException($"Unhandled modifier: {modifier}"),
    };
}
