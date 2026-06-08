using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Parsing;

/// <summary>
/// A hand-written recursive-descent parser with precedence climbing for expressions. It turns the
/// lexer's token stream into an immutable <see cref="ScadFile"/> AST. The parser never throws on
/// malformed input — every error is reported as a diagnostic and recovered from via panic-mode
/// recovery to a synchronization point (<c>;</c>, <c>}</c>, EOF, or a statement-start token).
/// </summary>
/// <remarks>
/// Trivia is propagated onto <b>statement</b> nodes (leading/blank-line from the first token,
/// trailing from the last token); operator/composite expression nodes carry span only, so a
/// comment never attaches to two nodes at once. See <c>docs/slices/Slice-2-Parser.md</c> §9.
/// </remarks>
public sealed class Parser
{
    private readonly SourceFile _source;
    private readonly TokenCursor _cursor;
    private readonly DiagnosticBag _diagnostics = new();

    private Parser(SourceFile source, IReadOnlyList<Token> tokens)
    {
        _source = source;
        _cursor = new TokenCursor(tokens);
    }

    /// <summary>Parses a token stream (the lexer's output) into a <see cref="ScadFile"/>.</summary>
    /// <param name="source">The source file the tokens were lexed from.</param>
    /// <param name="tokens">The token stream, terminated by exactly one EOF token.</param>
    /// <returns>The parse result (root + parser diagnostics).</returns>
    public static ParseResult Parse(SourceFile source, IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tokens);
        return new Parser(source, tokens).ParseFile(null);
    }

    /// <summary>Convenience: lexes <paramref name="source"/> then parses it, merging lexer diagnostics first.</summary>
    /// <param name="source">The source file to lex and parse.</param>
    /// <returns>The parse result (root + lexer-then-parser diagnostics).</returns>
    public static ParseResult Parse(SourceFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        LexResult lex = Lexer.Lex(source);
        return new Parser(source, lex.Tokens).ParseFile(lex.Diagnostics);
    }

    // ---------------------------------------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------------------------------------

    private ParseResult ParseFile(IReadOnlyList<Diagnostic>? lexerDiagnostics)
    {
        Token firstToken = _cursor.Current;
        var statements = new List<Statement>();
        while (!_cursor.AtEnd)
        {
            int before = _cursor.Position;
            Statement? statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }

            if (_cursor.Position == before)
            {
                _cursor.Advance(); // guarantee forward progress on any stuck recovery
            }
        }

        Token eof = _cursor.Current;
        var root = new ScadFile(_source, statements)
        {
            Span = new SourceSpan(_source, firstToken.Span.Start, eof.Span.End),

            // End-of-file comments live on the EOF token's leading trivia; preserve them.
            TrailingTrivia = eof.LeadingTrivia,
        };

        IReadOnlyList<Diagnostic> parserDiagnostics = _diagnostics.ToList();
        IReadOnlyList<Diagnostic> all = lexerDiagnostics is null || lexerDiagnostics.Count == 0
            ? parserDiagnostics
            : [.. lexerDiagnostics, .. parserDiagnostics];
        return new ParseResult(root, all);
    }

    // ---------------------------------------------------------------------------------------------
    // Statements
    // ---------------------------------------------------------------------------------------------

    private Statement? ParseStatement()
    {
        if (_cursor.AtEnd)
        {
            return null;
        }

        Token start = _cursor.Current;
        switch (start.Kind)
        {
            case TokenKind.Use:
                return ParseUseOrInclude(isUse: true);
            case TokenKind.Include:
                return ParseUseOrInclude(isUse: false);
            case TokenKind.Module:
                return ParseModuleDefinition();
            case TokenKind.Function:
                return ParseFunctionDefinition();
            case TokenKind.LBrace:
                return ParseBlock();
            case TokenKind.Semicolon:
                return Leaf(new EmptyStatement(), _cursor.Advance());
            case TokenKind.If:
                return ParseIf();
            case TokenKind.Star or TokenKind.Not or TokenKind.Hash or TokenKind.Percent:
                return ParseModifiedInstantiation();
            case TokenKind.Identifier or TokenKind.For or TokenKind.Let
                or TokenKind.Assert or TokenKind.Echo or TokenKind.Each:
                return ParseAssignmentOrInstantiation();
            default:
                Report(DiagnosticCode.UnexpectedToken, $"Unexpected {Unexpected(start)}.", start.Span);
                _cursor.Advance();
                Synchronize();
                return null;
        }
    }

    private Statement ParseUseOrInclude(bool isUse)
    {
        Token keyword = _cursor.Advance();
        if (_cursor.Check(TokenKind.FilePath))
        {
            Token path = _cursor.Advance();
            return isUse
                ? Leaf(new UseStatement(path.Text), keyword)
                : Leaf(new IncludeStatement(path.Text), keyword);
        }

        Report(DiagnosticCode.ExpectedToken, $"Expected 'file path' but found '{Found(_cursor.Current)}'.", _cursor.Current.Span);
        return isUse
            ? Leaf(new UseStatement(string.Empty), keyword)
            : Leaf(new IncludeStatement(string.Empty), keyword);
    }

    private ModuleDefinition ParseModuleDefinition()
    {
        Token keyword = _cursor.Advance();
        ExpectIdentifier(out string name);
        ExpectLParen();
        IReadOnlyList<Parameter> parameters = ParseParameterList();
        ExpectRParen();
        Statement body = ParseRequiredStatement();
        return Composite(new ModuleDefinition(name, parameters, body), keyword);
    }

    private FunctionDefinition ParseFunctionDefinition()
    {
        Token keyword = _cursor.Advance();
        ExpectIdentifier(out string name);
        ExpectLParen();
        IReadOnlyList<Parameter> parameters = ParseParameterList();
        ExpectRParen();
        ExpectAssign();
        Expression body = ParseExpression();
        ExpectSemicolon("function definition");
        return Leaf(new FunctionDefinition(name, parameters, body), keyword);
    }

    private BlockStatement ParseBlock()
    {
        Token open = _cursor.Advance(); // '{'
        var statements = new List<Statement>();
        while (!_cursor.Check(TokenKind.RBrace) && !_cursor.AtEnd)
        {
            int before = _cursor.Position;
            Statement? statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }

            if (_cursor.Position == before)
            {
                _cursor.Advance();
            }
        }

        ExpectRBrace();
        return Leaf(new BlockStatement(statements), open);
    }

    private IfStatement ParseIf()
    {
        Token keyword = _cursor.Advance(); // 'if'
        ExpectLParen();
        Expression condition = ParseExpression();
        ExpectRParen();
        Statement then = ParseRequiredStatement();
        Statement? elseBranch = null;
        if (_cursor.Check(TokenKind.Else))
        {
            _cursor.Advance();
            elseBranch = ParseRequiredStatement();
        }

        return Composite(new IfStatement(condition, then, elseBranch), keyword);
    }

    private Statement ParseModifiedInstantiation()
    {
        Token first = _cursor.Current;
        var modifiers = new List<InstantiationModifier>();
        while (_cursor.Kind is TokenKind.Star or TokenKind.Not or TokenKind.Hash or TokenKind.Percent)
        {
            modifiers.Add(_cursor.Kind switch
            {
                TokenKind.Star => InstantiationModifier.Disable,
                TokenKind.Not => InstantiationModifier.Root,
                TokenKind.Hash => InstantiationModifier.Highlight,
                _ => InstantiationModifier.Background,
            });
            _cursor.Advance();
        }

        return ParseInstantiationLike(modifiers, first);
    }

    private Statement ParseAssignmentOrInstantiation()
    {
        Token start = _cursor.Current;

        // Assignment is recognized only as `Identifier =` (an Assign token, not `==`).
        if (_cursor.Check(TokenKind.Identifier) && _cursor.Peek().Kind == TokenKind.Assign)
        {
            Token name = _cursor.Advance();
            _cursor.Advance(); // '='
            Expression value = ParseExpression();
            ExpectSemicolon("assignment");
            return Leaf(new AssignmentStatement(name.Text, value), name);
        }

        return ParseInstantiationLike([], start);
    }

    /// <summary>
    /// Parses <c>name ( arguments ) child</c>, applying name-recognition for the control-flow forms
    /// (<c>for</c>/<c>intersection_for</c>/<c>let</c>) per AST-Reference §15.2; everything else
    /// (<c>echo</c>/<c>assert</c>/<c>children</c>/<c>assign</c>/built-ins/user modules) becomes a
    /// generic <see cref="ModuleInstantiation"/>.
    /// </summary>
    private Statement ParseInstantiationLike(IReadOnlyList<InstantiationModifier> modifiers, Token spanStart)
    {
        if (_cursor.Kind is not (TokenKind.Identifier or TokenKind.For or TokenKind.Let
            or TokenKind.Assert or TokenKind.Echo or TokenKind.Each))
        {
            Report(DiagnosticCode.UnexpectedToken, $"Unexpected {Unexpected(_cursor.Current)}.", _cursor.Current.Span);
            Synchronize();
            return Composite(new ModuleInstantiation(modifiers, string.Empty, [], null), spanStart);
        }

        Token nameToken = _cursor.Advance();
        string name = nameToken.Text;
        ExpectLParen();
        IReadOnlyList<Argument> arguments = ParseArgumentList();
        ExpectRParen();

        switch (name)
        {
            case "for":
                return Composite(new ForStatement(ToBindings(arguments), ParseRequiredStatement()), spanStart);
            case "intersection_for":
                return Composite(new IntersectionForStatement(ToBindings(arguments), ParseRequiredStatement()), spanStart);
            case "let":
                return Composite(new LetStatement(ToBindings(arguments), ParseRequiredStatement()), spanStart);
            default:
                Statement? child = ParseInstantiationChild();
                return child is null
                    ? Leaf(new ModuleInstantiation(modifiers, name, arguments, null), spanStart)
                    : Composite(new ModuleInstantiation(modifiers, name, arguments, child), spanStart);
        }
    }

    /// <summary>Parses the child of a module instantiation: <c>;</c> → null, <c>{…}</c> → block, else a nested instantiation.</summary>
    private Statement? ParseInstantiationChild()
    {
        if (_cursor.Check(TokenKind.Semicolon))
        {
            _cursor.Advance();
            return null;
        }

        if (IsChildStart(_cursor.Kind))
        {
            return ParseStatement();
        }

        Report(DiagnosticCode.MissingSemicolon, "Missing ';' after module instantiation.", _cursor.Current.Span);
        return null;
    }

    private static bool IsChildStart(TokenKind kind) => kind is TokenKind.LBrace or TokenKind.If
        or TokenKind.For or TokenKind.Let or TokenKind.Identifier or TokenKind.Assert
        or TokenKind.Echo or TokenKind.Each or TokenKind.Star or TokenKind.Not
        or TokenKind.Hash or TokenKind.Percent;

    /// <summary>Parses a required single statement, synthesizing an <see cref="EmptyStatement"/> if none is available.</summary>
    private Statement ParseRequiredStatement()
    {
        Statement? statement = _cursor.AtEnd ? null : ParseStatement();
        if (statement is not null)
        {
            return statement;
        }

        Token at = _cursor.Current;
        return new EmptyStatement { Span = new SourceSpan(_source, at.Span.Start, at.Span.Start) };
    }

    private static List<Binding> ToBindings(IReadOnlyList<Argument> arguments)
    {
        var bindings = new List<Binding>(arguments.Count);
        foreach (Argument argument in arguments)
        {
            bindings.Add(new Binding(argument.Name ?? string.Empty, argument.Value) { Span = argument.Span });
        }

        return bindings;
    }

    // ---------------------------------------------------------------------------------------------
    // Parameters & arguments
    // ---------------------------------------------------------------------------------------------

    private List<Parameter> ParseParameterList()
    {
        var parameters = new List<Parameter>();
        if (_cursor.Check(TokenKind.RParen))
        {
            return parameters;
        }

        while (true)
        {
            Parameter? parameter = ParseParameter();
            if (parameter is null)
            {
                Report(DiagnosticCode.InvalidParameterList, "Invalid parameter list.", _cursor.Current.Span);
                SkipMalformedList();
                break;
            }

            parameters.Add(parameter);
            if (_cursor.Check(TokenKind.Comma))
            {
                _cursor.Advance();
                if (_cursor.Check(TokenKind.RParen))
                {
                    break; // trailing comma
                }

                continue;
            }

            if (!_cursor.Check(TokenKind.RParen))
            {
                Report(DiagnosticCode.InvalidParameterList, "Invalid parameter list.", _cursor.Current.Span);
                SkipMalformedList();
            }

            break;
        }

        return parameters;
    }

    private Parameter? ParseParameter()
    {
        if (!_cursor.Check(TokenKind.Identifier))
        {
            return null;
        }

        Token name = _cursor.Advance();
        if (_cursor.Check(TokenKind.Assign))
        {
            _cursor.Advance();
            Expression defaultValue = ParseExpression();
            return Spanned(new Parameter(name.Text, defaultValue), name);
        }

        return Spanned(new Parameter(name.Text, null), name);
    }

    private List<Argument> ParseArgumentList() => ParseArgumentList(allowSemicolonTerminator: false);

    /// <summary>
    /// Parses a comma-separated argument list ending at <c>)</c>. When
    /// <paramref name="allowSemicolonTerminator"/> is set, a <c>;</c> also terminates the list without
    /// error — this is the C-style <c>for</c> form <c>for (init; cond; update)</c> (§6), where the
    /// init/update lists are <c>arguments</c> separated by <c>;</c>.
    /// </summary>
    private List<Argument> ParseArgumentList(bool allowSemicolonTerminator)
    {
        var arguments = new List<Argument>();
        if (IsArgumentListEnd(allowSemicolonTerminator))
        {
            return arguments;
        }

        while (true)
        {
            arguments.Add(ParseArgument());
            if (_cursor.Check(TokenKind.Comma))
            {
                _cursor.Advance();
                if (IsArgumentListEnd(allowSemicolonTerminator))
                {
                    break; // trailing comma
                }

                continue;
            }

            if (!IsArgumentListEnd(allowSemicolonTerminator))
            {
                Report(DiagnosticCode.InvalidArgumentList, "Invalid argument list.", _cursor.Current.Span);
                SkipMalformedList();
            }

            break;
        }

        return arguments;
    }

    private bool IsArgumentListEnd(bool allowSemicolonTerminator) =>
        _cursor.Check(TokenKind.RParen)
        || (allowSemicolonTerminator && _cursor.Check(TokenKind.Semicolon));

    private Argument ParseArgument()
    {
        Token start = _cursor.Current;
        if (_cursor.Check(TokenKind.Identifier) && _cursor.Peek().Kind == TokenKind.Assign)
        {
            Token name = _cursor.Advance();
            _cursor.Advance(); // '='
            Expression namedValue = ParseExpression();
            return Spanned(new Argument(name.Text, namedValue), start);
        }

        Expression value = ParseExpression();
        return Spanned(new Argument(null, value), start);
    }

    private void SkipMalformedList()
    {
        while (!_cursor.AtEnd && !_cursor.Check(TokenKind.RParen)
            && !_cursor.Check(TokenKind.Semicolon) && !_cursor.Check(TokenKind.RBrace))
        {
            _cursor.Advance();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Expressions (precedence climbing)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses an <c>expr</c>. The keyword-prefixed forms (<c>function</c>/<c>let</c>/<c>assert</c>/
    /// <c>echo</c>) are dispatched here, <b>before</b> the ternary/binary cascade, because the grammar
    /// places them at the top of <c>expr</c> (outside <c>logic_or</c>) — so they may start an
    /// expression but never appear as a binary operand or a ternary condition. Their bodies are
    /// right-greedy (parsed via <see cref="ParseExpression"/>), matching <c>parser.y</c>.
    /// </summary>
    private Expression ParseExpression()
    {
        switch (_cursor.Kind)
        {
            case TokenKind.Function:
                return ParseFunctionLiteral();
            case TokenKind.Let:
                return ParseLetExpression();
            case TokenKind.Assert:
                return ParseAssertExpression();
            case TokenKind.Echo:
                return ParseEchoExpression();
            default:
                return ParseTernary();
        }
    }

    private Expression ParseTernary()
    {
        Token start = _cursor.Current;
        Expression condition = ParseBinary(0);
        if (_cursor.Check(TokenKind.Question))
        {
            _cursor.Advance();
            Expression then = ParseExpression();
            ExpectColon();
            Expression elseValue = ParseExpression();
            return Spanned(new ConditionalExpression(condition, then, elseValue), start);
        }

        return condition;
    }

    private FunctionLiteral ParseFunctionLiteral()
    {
        Token keyword = _cursor.Advance(); // 'function'
        ExpectLParen();
        IReadOnlyList<Parameter> parameters = ParseParameterList();
        ExpectRParen();
        Expression body = ParseExpression();
        return Composite(new FunctionLiteral(parameters, body), keyword);
    }

    private LetExpression ParseLetExpression()
    {
        Token keyword = _cursor.Advance(); // 'let'
        ExpectLParen();
        IReadOnlyList<Argument> arguments = ParseArgumentList();
        ExpectRParen();
        Expression body = ParseExpression();
        return Composite(new LetExpression(ToBindings(arguments), body), keyword);
    }

    private AssertExpression ParseAssertExpression()
    {
        Token keyword = _cursor.Advance(); // 'assert'
        ExpectLParen();
        IReadOnlyList<Argument> arguments = ParseArgumentList();
        ExpectRParen();
        Expression? body = CanStartExpression(_cursor.Kind) ? ParseExpression() : null;
        return Composite(new AssertExpression(arguments, body), keyword);
    }

    private EchoExpression ParseEchoExpression()
    {
        Token keyword = _cursor.Advance(); // 'echo'
        ExpectLParen();
        IReadOnlyList<Argument> arguments = ParseArgumentList();
        ExpectRParen();
        Expression? body = CanStartExpression(_cursor.Kind) ? ParseExpression() : null;
        return Composite(new EchoExpression(arguments, body), keyword);
    }

    /// <summary>
    /// The first-set of <c>expr</c>, used to decide whether an <c>assert</c>/<c>echo</c> has a
    /// trailing body (grammar <c>expr_or_empty</c>). A body is present iff the next token can start an
    /// expression; closers/separators (<c>;</c>, <c>,</c>, <c>)</c>, <c>]</c>, <c>}</c>, EOF) mean none.
    /// </summary>
    private static bool CanStartExpression(TokenKind kind) => kind is
        TokenKind.Number or TokenKind.String or TokenKind.True or TokenKind.False
        or TokenKind.Undef or TokenKind.Identifier or TokenKind.LParen or TokenKind.LBracket
        or TokenKind.Minus or TokenKind.Plus or TokenKind.Not or TokenKind.Tilde
        or TokenKind.Let or TokenKind.Assert or TokenKind.Echo or TokenKind.Function;

    private Expression ParseBinary(int minBindingPower)
    {
        Token start = _cursor.Current;
        Expression left = ParseUnary();
        while (TryBinaryOperator(_cursor.Kind, out BinaryOperator op, out int lbp) && lbp >= minBindingPower)
        {
            _cursor.Advance();
            Expression right = ParseBinary(lbp + 1); // all binary operators here are left-associative
            left = Spanned(new BinaryExpression(op, left, right), start);
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (_cursor.Kind is TokenKind.Minus or TokenKind.Plus or TokenKind.Not or TokenKind.Tilde)
        {
            Token op = _cursor.Advance();
            UnaryOperator unary = op.Kind switch
            {
                TokenKind.Minus => UnaryOperator.Negate,
                TokenKind.Plus => UnaryOperator.Plus,
                TokenKind.Not => UnaryOperator.Not,
                _ => UnaryOperator.BitwiseNot,
            };
            Expression operand = ParseUnary();
            return Composite(new UnaryExpression(unary, operand), op);
        }

        return ParseExponent();
    }

    private Expression ParseExponent()
    {
        Token start = _cursor.Current;
        Expression left = ParsePostfix();
        if (_cursor.Check(TokenKind.Caret))
        {
            _cursor.Advance();

            // '^' is right-associative; its right operand is a unary (so `2^-1` and `a^b^c` work).
            Expression right = ParseUnary();
            return Spanned(new BinaryExpression(BinaryOperator.Power, left, right), start);
        }

        return left;
    }

    private Expression ParsePostfix()
    {
        Token start = _cursor.Current;
        Expression expression = ParsePrimary();
        while (true)
        {
            switch (_cursor.Kind)
            {
                case TokenKind.LParen:
                    _cursor.Advance();
                    IReadOnlyList<Argument> arguments = ParseArgumentList();
                    ExpectRParen();
                    expression = Spanned(new FunctionCallExpression(expression, arguments), start);
                    break;
                case TokenKind.LBracket:
                    _cursor.Advance();
                    Expression index = ParseExpression();
                    ExpectRBracket();
                    expression = Spanned(new IndexExpression(expression, index), start);
                    break;
                case TokenKind.Dot:
                    _cursor.Advance();
                    ExpectIdentifier(out string member);
                    expression = Spanned(new MemberExpression(expression, member), start);
                    break;
                default:
                    return expression;
            }
        }
    }

    private Expression ParsePrimary()
    {
        Token token = _cursor.Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                _cursor.Advance();
                return Leaf(new NumberLiteral(token.NumberValue ?? double.NaN, token.Text), token);
            case TokenKind.String:
                _cursor.Advance();
                return Leaf(new StringLiteral(token.StringValue ?? string.Empty, token.Text), token);
            case TokenKind.True:
                _cursor.Advance();
                return Leaf(new BooleanLiteral(true), token);
            case TokenKind.False:
                _cursor.Advance();
                return Leaf(new BooleanLiteral(false), token);
            case TokenKind.Undef:
                _cursor.Advance();
                return Leaf(new UndefLiteral(), token);
            case TokenKind.Identifier:
                _cursor.Advance();
                return Leaf(new Identifier(token.Text), token);
            case TokenKind.LParen:
                return ParseParenthesized();
            case TokenKind.LBracket:
                return ParseVectorOrRange();
            default:
                Report(DiagnosticCode.ExpectedExpression, "Expected an expression.", token.Span);

                // Recovery placeholder; not consumed so statement-level recovery can resynchronize.
                return new UndefLiteral { Span = new SourceSpan(_source, token.Span.Start, token.Span.Start) };
        }
    }

    private ParenthesizedExpression ParseParenthesized()
    {
        Token open = _cursor.Advance();
        Expression inner = ParseExpression();
        ExpectRParen();
        return Leaf(new ParenthesizedExpression(inner), open);
    }

    private Expression ParseVectorOrRange()
    {
        Token open = _cursor.Advance(); // '['
        if (_cursor.Check(TokenKind.RBracket))
        {
            _cursor.Advance();
            return Leaf(new VectorExpression([]), open);
        }

        Expression first = ParseVectorElement();

        // A range start is an `expr`; a `:` after the first element means this is a range, not a
        // vector. Comprehension generators never precede `:`, so detecting `:` here is unambiguous.
        if (_cursor.Check(TokenKind.Colon))
        {
            _cursor.Advance();
            Expression second = ParseExpression();
            if (_cursor.Check(TokenKind.Colon))
            {
                _cursor.Advance();
                Expression third = ParseExpression();
                ExpectRBracket();
                return Leaf(new RangeExpression(first, second, third), open);
            }

            ExpectRBracket();
            return Leaf(new RangeExpression(first, null, second), open);
        }

        var elements = new List<Expression> { first };
        while (_cursor.Check(TokenKind.Comma))
        {
            _cursor.Advance();
            if (_cursor.Check(TokenKind.RBracket))
            {
                break; // trailing comma
            }

            elements.Add(ParseVectorElement());
        }

        ExpectRBracket();
        return Leaf(new VectorExpression(elements), open);
    }

    // ---------------------------------------------------------------------------------------------
    // Vector elements & list-comprehension generators (Slice 3 §5–§6)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses a single <c>vector_element</c>: a list-comprehension generator (<c>for</c>, C-style
    /// <c>for</c>, <c>if</c>, <c>each</c>, <c>let</c>-comprehension) or — for any other lead — a plain
    /// <see cref="ParseExpression"/>. Generators are valid only inside <c>[ … ]</c>, so they live here
    /// and never in <see cref="ParseExpression"/>. Bodies recurse through this method, enabling
    /// chaining (<c>for(i) for(j) e</c>, <c>for(i) if(c) e</c>).
    /// </summary>
    private Expression ParseVectorElement()
    {
        switch (_cursor.Kind)
        {
            case TokenKind.For:
                return ParseForComprehension();
            case TokenKind.Each:
                return ParseEachComprehension();
            case TokenKind.If:
                return ParseIfComprehension();
            case TokenKind.Let:
                return ParseLetVectorElement();

            // `( for/each/if … )` is a parenthesized generator (grammar
            // `list_comprehension_elements_p`). `( let … )` and `( expr )` stay on the normal
            // expression path so postfix application — e.g. `(function(x) x)(5)` — still works.
            case TokenKind.LParen when _cursor.Peek().Kind
                is TokenKind.For or TokenKind.Each or TokenKind.If:
                return ParseParenthesizedGenerator();

            default:
                return ParseExpression();
        }
    }

    /// <summary>Parses <c>for ( arguments ) body</c> or the C-style <c>for ( init; cond; update ) body</c> (§6).</summary>
    private Expression ParseForComprehension()
    {
        Token keyword = _cursor.Advance(); // 'for'
        ExpectLParen();
        IReadOnlyList<Argument> first = ParseArgumentList(allowSemicolonTerminator: true);
        if (_cursor.Check(TokenKind.Semicolon))
        {
            _cursor.Advance();
            Expression condition = ParseExpression();
            ExpectSemicolon("for-comprehension condition");
            IReadOnlyList<Argument> update = ParseArgumentList();
            ExpectRParen();
            Expression cBody = ParseVectorElement();
            return Composite(
                new ForCComprehension(ToBindings(first), condition, ToBindings(update), cBody),
                keyword);
        }

        ExpectRParen();
        Expression body = ParseVectorElement();
        return Composite(new ForComprehension(ToBindings(first), body), keyword);
    }

    /// <summary>Parses <c>each vector_element</c>.</summary>
    private EachExpression ParseEachComprehension()
    {
        Token keyword = _cursor.Advance(); // 'each'
        Expression value = ParseVectorElement();
        return Composite(new EachExpression(value), keyword);
    }

    /// <summary>Parses <c>if ( expr ) then [ else else ]</c> as a comprehension (filter when no else).</summary>
    private IfComprehension ParseIfComprehension()
    {
        Token keyword = _cursor.Advance(); // 'if'
        ExpectLParen();
        Expression condition = ParseExpression();
        ExpectRParen();
        Expression then = ParseVectorElement();
        Expression? elseValue = null;
        if (_cursor.Check(TokenKind.Else))
        {
            _cursor.Advance();
            elseValue = ParseVectorElement();
        }

        return Composite(new IfComprehension(condition, then, elseValue), keyword);
    }

    /// <summary>
    /// Parses <c>let ( arguments ) body</c> in vector-element position, resolving the trailing-<c>let</c>
    /// ambiguity (§5): if the body is a generator the result is a <see cref="LetComprehension"/>;
    /// if it is an ordinary value it is a <see cref="LetExpression"/> element.
    /// </summary>
    private Expression ParseLetVectorElement()
    {
        Token keyword = _cursor.Advance(); // 'let'
        ExpectLParen();
        IReadOnlyList<Argument> arguments = ParseArgumentList();
        ExpectRParen();
        List<Binding> bindings = ToBindings(arguments);
        Expression body = ParseVectorElement();
        return IsComprehensionGenerator(body)
            ? Composite(new LetComprehension(bindings, body), keyword)
            : Composite(new LetExpression(bindings, body), keyword);
    }

    /// <summary>Parses <c>( generator )</c>, preserving the author's parentheses around a generator.</summary>
    private ParenthesizedExpression ParseParenthesizedGenerator()
    {
        Token open = _cursor.Advance(); // '('
        Expression inner = ParseVectorElement();
        ExpectRParen();
        return Leaf(new ParenthesizedExpression(inner), open);
    }

    /// <summary>
    /// True when <paramref name="expression"/> is a comprehension generator (seeing through a single
    /// layer of author parentheses), used by the trailing-<c>let</c> rule.
    /// </summary>
    private static bool IsComprehensionGenerator(Expression expression) => expression switch
    {
        ForComprehension or ForCComprehension or IfComprehension or LetComprehension or EachExpression => true,
        ParenthesizedExpression parenthesized => IsComprehensionGenerator(parenthesized.Inner),
        _ => false,
    };

    private static bool TryBinaryOperator(TokenKind kind, out BinaryOperator op, out int leftBindingPower)
    {
        switch (kind)
        {
            case TokenKind.Or: op = BinaryOperator.Or; leftBindingPower = 10; return true;
            case TokenKind.And: op = BinaryOperator.And; leftBindingPower = 20; return true;
            case TokenKind.Equal: op = BinaryOperator.Equal; leftBindingPower = 30; return true;
            case TokenKind.NotEqual: op = BinaryOperator.NotEqual; leftBindingPower = 30; return true;
            case TokenKind.Less: op = BinaryOperator.Less; leftBindingPower = 40; return true;
            case TokenKind.LessEqual: op = BinaryOperator.LessEqual; leftBindingPower = 40; return true;
            case TokenKind.Greater: op = BinaryOperator.Greater; leftBindingPower = 40; return true;
            case TokenKind.GreaterEqual: op = BinaryOperator.GreaterEqual; leftBindingPower = 40; return true;
            case TokenKind.Pipe: op = BinaryOperator.BitwiseOr; leftBindingPower = 50; return true;
            case TokenKind.Amp: op = BinaryOperator.BitwiseAnd; leftBindingPower = 60; return true;
            case TokenKind.ShiftLeft: op = BinaryOperator.ShiftLeft; leftBindingPower = 70; return true;
            case TokenKind.ShiftRight: op = BinaryOperator.ShiftRight; leftBindingPower = 70; return true;
            case TokenKind.Plus: op = BinaryOperator.Add; leftBindingPower = 80; return true;
            case TokenKind.Minus: op = BinaryOperator.Subtract; leftBindingPower = 80; return true;
            case TokenKind.Star: op = BinaryOperator.Multiply; leftBindingPower = 90; return true;
            case TokenKind.Slash: op = BinaryOperator.Divide; leftBindingPower = 90; return true;
            case TokenKind.Percent: op = BinaryOperator.Modulo; leftBindingPower = 90; return true;
            default: op = default; leftBindingPower = 0; return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Trivia / span attachment
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Finishes a node that owns both its first and last tokens: takes leading + blank-line from
    /// <paramref name="first"/> and trailing from the last consumed token. Used for literals,
    /// identifiers, bracketed/parenthesized expressions, and simple statements.
    /// </summary>
    private TNode Leaf<TNode>(TNode node, Token first)
        where TNode : AstNode =>
        node with
        {
            Span = new SourceSpan(_source, first.Span.Start, _cursor.Previous.Span.End),
            LeadingTrivia = first.LeadingTrivia,
            TrailingTrivia = _cursor.Previous.TrailingTrivia,
            BlankLineBefore = first.BlankLineBefore,
        };

    /// <summary>
    /// Finishes a node whose last token belongs to a child (so the child owns trailing trivia):
    /// takes leading + blank-line from its own first token only. Used for definitions, control flow,
    /// unary expressions, and chained instantiations.
    /// </summary>
    private TNode Composite<TNode>(TNode node, Token first)
        where TNode : AstNode =>
        node with
        {
            Span = new SourceSpan(_source, first.Span.Start, _cursor.Previous.Span.End),
            LeadingTrivia = first.LeadingTrivia,
            BlankLineBefore = first.BlankLineBefore,
        };

    /// <summary>
    /// Finishes a node that shares its first token with a child (so the child owns leading trivia):
    /// sets the span only. Used for binary/conditional/postfix expressions and supporting nodes,
    /// so a comment never attaches to two nodes.
    /// </summary>
    private TNode Spanned<TNode>(TNode node, Token first)
        where TNode : AstNode =>
        node with { Span = new SourceSpan(_source, first.Span.Start, _cursor.Previous.Span.End) };

    // ---------------------------------------------------------------------------------------------
    // Token expectations & recovery
    // ---------------------------------------------------------------------------------------------

    private bool ExpectIdentifier(out string name)
    {
        if (_cursor.Check(TokenKind.Identifier))
        {
            name = _cursor.Advance().Text;
            return true;
        }

        Report(DiagnosticCode.ExpectedToken, $"Expected 'identifier' but found '{Found(_cursor.Current)}'.", _cursor.Current.Span);
        name = string.Empty;
        return false;
    }

    private void ExpectLParen()
    {
        if (!_cursor.Match(TokenKind.LParen))
        {
            Report(DiagnosticCode.ExpectedToken, $"Expected '(' but found '{Found(_cursor.Current)}'.", _cursor.Current.Span);
        }
    }

    private void ExpectAssign()
    {
        if (!_cursor.Match(TokenKind.Assign))
        {
            Report(DiagnosticCode.ExpectedToken, $"Expected '=' but found '{Found(_cursor.Current)}'.", _cursor.Current.Span);
        }
    }

    private void ExpectColon()
    {
        if (!_cursor.Match(TokenKind.Colon))
        {
            Report(DiagnosticCode.ExpectedToken, $"Expected ':' but found '{Found(_cursor.Current)}'.", _cursor.Current.Span);
        }
    }

    private void ExpectRParen() => ExpectCloser(TokenKind.RParen, "(", ")");

    private void ExpectRBracket() => ExpectCloser(TokenKind.RBracket, "[", "]");

    private void ExpectRBrace() => ExpectCloser(TokenKind.RBrace, "{", "}");

    private void ExpectCloser(TokenKind close, string openSymbol, string closeSymbol)
    {
        if (!_cursor.Match(close))
        {
            Report(DiagnosticCode.UnclosedDelimiter, $"Unclosed '{openSymbol}'; expected '{closeSymbol}'.", _cursor.Current.Span);
        }
    }

    private void ExpectSemicolon(string construct)
    {
        if (!_cursor.Match(TokenKind.Semicolon))
        {
            Report(DiagnosticCode.MissingSemicolon, $"Missing ';' after {construct}.", _cursor.Current.Span);
        }
    }

    /// <summary>Panic-mode recovery: skip tokens until a synchronization point.</summary>
    private void Synchronize()
    {
        while (!_cursor.AtEnd)
        {
            switch (_cursor.Kind)
            {
                case TokenKind.Semicolon:
                    _cursor.Advance();
                    return;
                case TokenKind.RBrace:
                    return; // let the enclosing block consume it
                case TokenKind.Module or TokenKind.Function or TokenKind.If
                    or TokenKind.Use or TokenKind.Include
                    or TokenKind.Star or TokenKind.Not or TokenKind.Hash or TokenKind.Percent:
                    return;
                default:
                    _cursor.Advance();
                    break;
            }
        }
    }

    private void Report(string code, string message, SourceSpan span) =>
        _diagnostics.Error(code, message, span);

    private static string Found(Token token) =>
        token.Kind == TokenKind.Eof ? "end of file" : token.Text;

    private static string Unexpected(Token token) =>
        token.Kind == TokenKind.Eof ? "end of file" : $"'{token.Text}'";
}
