using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Semantics;

/// <summary>
/// Builds symbol tables and resolves every reference under OpenSCAD's scoping rules, producing the
/// <see cref="ISemanticModel"/> the Slice 5 inliner consumes, plus semantic validation diagnostics
/// (SB3001–SB3005). The analyzer <b>never throws</b> — every problem is a diagnostic.
/// </summary>
/// <remarks>
/// Two passes over the load graph: pass 1 records each file's top-level declarations (flagging
/// within-scope duplicates, SB3003/SB3004); pass 2 walks each file in <i>its own</i> environment
/// (own + <c>include</c>-merged declarations for lookups; own <c>use</c>d libraries for calls,
/// last-<c>use</c>-wins), binding references to symbols and validating member access (SB3001),
/// comprehension position (SB3002), and unknown references (SB3005). Scoping/lookup order mirrors
/// <c>ScopeContext.cc</c>/<c>Context.cc</c>: own scope → built-ins → used libraries.
/// </remarks>
public sealed class SemanticAnalyzer
{
    private readonly LoadGraph _graph;
    private readonly DiagnosticBag _diagnostics = new();

    // Pass-1 outputs, keyed by file.
    private readonly Dictionary<SourceFile, FileScope> _scopes = [];
    private readonly Dictionary<SourceFile, LoadedFile> _loaded = [];

    // Pass-2 side tables (reference identity, per AST-Reference §15.6).
    private readonly Dictionary<AstNode, Symbol> _resolution = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AstNode, List<AstNode>> _referencesTo = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AstNode, List<Symbol>> _ownFileReferences = new(ReferenceEqualityComparer.Instance);

    // Mutable walk state (single-threaded; reset per file / construct).
    private readonly List<LocalFrame> _scopeChain = [];
    private SourceFile _currentFile = SourceFile.Synthesized;
    private FileEnvironment _currentEnv = FileEnvironment.Empty;
    private AstNode? _currentTopLevelDecl;

    private SemanticAnalyzer(LoadGraph graph) => _graph = graph;

    /// <summary>Analyzes a loaded graph (follows <c>include</c> merges and <c>use</c> imports). Never throws.</summary>
    /// <param name="graph">The loaded graph to analyze.</param>
    /// <returns>The semantic model plus diagnostics.</returns>
    public static SemanticResult Analyze(LoadGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new SemanticAnalyzer(graph).Run();
    }

    /// <summary>
    /// Analyzes a single parsed file in isolation (validation + own-scope model) — the unit-test entry.
    /// The file is treated as a one-node graph with no resolved edges, so unresolved references are not
    /// flagged unless the file pulls in nothing external (see SB3005).
    /// </summary>
    /// <param name="file">The parsed file to analyze.</param>
    /// <returns>The semantic model plus diagnostics.</returns>
    public static SemanticResult Analyze(ScadFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var loaded = new LoadedFile(file.Source, file, [], []);
        var graph = new LoadGraph(
            loaded,
            new Dictionary<string, LoadedFile>(StringComparer.Ordinal) { [file.Source.Path] = loaded },
            []);
        return Analyze(graph);
    }

    private SemanticResult Run()
    {
        IReadOnlyList<LoadedFile> files = DistinctFiles();

        // Pass 1: declaration tables + within-scope duplicate detection.
        foreach (LoadedFile file in files)
        {
            _loaded[file.Source] = file;
            _scopes[file.Source] = BuildFileScope(file);
        }

        // Pass 2: reference resolution + validation, each file in its own environment.
        foreach (LoadedFile file in files)
        {
            ResolveFile(file);
        }

        var declarations = new Dictionary<SourceFile, FileDeclarations>();
        foreach ((SourceFile source, FileScope scope) in _scopes)
        {
            declarations[source] = new FileDeclarations(scope.ModuleList, scope.FunctionList, scope.VariableList);
        }

        var referencesTo = new Dictionary<AstNode, IReadOnlyList<AstNode>>(ReferenceEqualityComparer.Instance);
        foreach ((AstNode declaration, List<AstNode> references) in _referencesTo)
        {
            referencesTo[declaration] = references;
        }

        var ownFileReferences = new Dictionary<AstNode, IReadOnlyList<Symbol>>(ReferenceEqualityComparer.Instance);
        foreach ((AstNode declaration, List<Symbol> edges) in _ownFileReferences)
        {
            ownFileReferences[declaration] = edges;
        }

        var model = new SemanticModel(declarations, _resolution, referencesTo, ownFileReferences);

        IReadOnlyList<Diagnostic> sorted = [.. _diagnostics.ToList()
            .OrderBy(d => d.Span.File.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Span.Start.Offset)
            .ThenBy(d => d.Code, StringComparer.Ordinal)];
        return new SemanticResult(model, sorted);
    }

    private List<LoadedFile> DistinctFiles()
    {
        var seen = new HashSet<SourceFile>();
        var result = new List<LoadedFile>();
        void Add(LoadedFile? file)
        {
            if (file is not null && seen.Add(file.Source))
            {
                result.Add(file);
            }
        }

        Add(_graph.Root);
        foreach (LoadedFile file in _graph.ByAbsolutePath.Values)
        {
            Add(file);
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 1 — declaration tables (§6) + duplicate detection (SB3003/SB3004)
    // ---------------------------------------------------------------------------------------------

    // Duplicate detection (SB3003/SB3004) runs at FILE scope — the scope that drives cross-file
    // bundling collisions. Repeated names inside a module body / block are local-only (the inliner
    // never renames or merges locals) and OpenSCAD's exact block-scope boundary for hoisted
    // assignments is ambiguous, so flagging them risks false positives; deferred by design (§6).
    private FileScope BuildFileScope(LoadedFile file)
    {
        var scope = new FileScope();
        foreach (Statement statement in file.Ast.Statements)
        {
            switch (statement)
            {
                case ModuleDefinition module:
                    if (scope.Modules.ContainsKey(module.Name))
                    {
                        _diagnostics.Warning(
                            DiagnosticCode.DefinitionRedefined,
                            $"module '{module.Name}' is redefined; the last definition wins.",
                            module.Span);
                    }

                    scope.Modules[module.Name] = new Symbol(SymbolKind.Module, module.Name, file.Source, module);
                    scope.ModuleList.Add(module);
                    break;

                case FunctionDefinition function:
                    if (scope.Functions.ContainsKey(function.Name))
                    {
                        _diagnostics.Warning(
                            DiagnosticCode.DefinitionRedefined,
                            $"function '{function.Name}' is redefined; the last definition wins.",
                            function.Span);
                    }

                    scope.Functions[function.Name] = new Symbol(SymbolKind.Function, function.Name, file.Source, function);
                    scope.FunctionList.Add(function);
                    break;

                case AssignmentStatement assignment:
                    if (scope.Variables.TryGetValue(assignment.Name, out Symbol? previous))
                    {
                        _diagnostics.Warning(
                            DiagnosticCode.VariableReassigned,
                            $"Variable '{assignment.Name}' was assigned on line {previous.Declaration.Span.Start.Line} but is overwritten; the last assignment wins.",
                            assignment.Span);
                    }

                    scope.Variables[assignment.Name] = new Symbol(SymbolKind.Variable, assignment.Name, file.Source, assignment);
                    scope.VariableList.Add(assignment);
                    break;
            }
        }

        return scope;
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 2 — reference resolution + validation
    // ---------------------------------------------------------------------------------------------

    private void ResolveFile(LoadedFile file)
    {
        _currentFile = file.Source;
        _currentEnv = BuildEnvironment(file);
        _scopeChain.Clear();

        foreach (Statement statement in file.Ast.Statements)
        {
            // Only top-level declarations seed PrivateConstants reachability; geometry does not.
            _currentTopLevelDecl = statement is ModuleDefinition or FunctionDefinition or AssignmentStatement
                ? statement
                : null;
            ResolveStatement(statement);
        }

        _currentTopLevelDecl = null;
    }

    private void ResolveStatement(Statement statement)
    {
        switch (statement)
        {
            case IncludeStatement or UseStatement or EmptyStatement:
                break;

            case AssignmentStatement assignment:
                ResolveExpression(assignment.Value, comprehensionAllowed: false);
                break;

            case ModuleDefinition module:
                ResolveModuleDefinition(module);
                break;

            case FunctionDefinition function:
                ResolveFunctionDefinition(function);
                break;

            case ModuleInstantiation instantiation:
                ResolveModuleInstantiation(instantiation);
                break;

            case BlockStatement block:
                foreach (Statement child in block.Statements)
                {
                    ResolveStatement(child);
                }

                break;

            case IfStatement branch:
                ResolveExpression(branch.Condition, comprehensionAllowed: false);
                ResolveStatement(branch.Then);
                if (branch.Else is not null)
                {
                    ResolveStatement(branch.Else);
                }

                break;

            case ForStatement loop:
                ResolveBoundBody(loop.Bindings, loop.Body);
                break;
            case IntersectionForStatement loop:
                ResolveBoundBody(loop.Bindings, loop.Body);
                break;
            case LetStatement let:
                ResolveBoundBody(let.Bindings, let.Body);
                break;
        }
    }

    private void ResolveModuleDefinition(ModuleDefinition module)
    {
        ResolveParameterDefaults(module.Parameters);
        PushFrame();
        AddParameters(module.Parameters);
        CollectBodyLocals(module.Body, _scopeChain[^1]);
        ResolveStatement(module.Body);
        PopFrame();
    }

    private void ResolveFunctionDefinition(FunctionDefinition function)
    {
        ResolveParameterDefaults(function.Parameters);
        PushFrame();
        AddParameters(function.Parameters);
        ResolveExpression(function.Body, comprehensionAllowed: false);
        PopFrame();
    }

    private void ResolveBoundBody(IReadOnlyList<Binding> bindings, Statement body)
    {
        PushFrame();
        ResolveBindings(bindings);
        ResolveStatement(body);
        PopFrame();
    }

    private void ResolveModuleInstantiation(ModuleInstantiation instantiation)
    {
        RecordResolution(instantiation, ResolveModuleCall(instantiation));

        // `assign(a = …) child` binds its named arguments for the child — deprecated `let` semantics
        // (the inliner rewrites it to `let`, SB5001). Model the scope so the child's reads resolve.
        if (instantiation.Name == "assign" && instantiation.Child is not null)
        {
            PushFrame();
            foreach (Argument argument in instantiation.Arguments)
            {
                ResolveExpression(argument.Value, comprehensionAllowed: false);
                if (argument.Name is not null)
                {
                    _scopeChain[^1].Variables.Add(argument.Name);
                }
            }

            ResolveStatement(instantiation.Child);
            PopFrame();
            return;
        }

        ResolveArguments(instantiation.Arguments);
        if (instantiation.Child is not null)
        {
            ResolveStatement(instantiation.Child);
        }
    }

    private void ResolveExpression(Expression expression, bool comprehensionAllowed)
    {
        switch (expression)
        {
            case NumberLiteral or StringLiteral or BooleanLiteral or UndefLiteral:
                break;

            case Identifier identifier:
                RecordResolution(identifier, ResolveVariableRead(identifier));
                break;

            case VectorExpression vector:
                foreach (Expression element in vector.Elements)
                {
                    ResolveExpression(element, comprehensionAllowed: true);
                }

                break;

            case RangeExpression range:
                ResolveExpression(range.Start, comprehensionAllowed: false);
                if (range.Step is not null)
                {
                    ResolveExpression(range.Step, comprehensionAllowed: false);
                }

                ResolveExpression(range.End, comprehensionAllowed: false);
                break;

            case BinaryExpression binary:
                ResolveExpression(binary.Left, comprehensionAllowed: false);
                ResolveExpression(binary.Right, comprehensionAllowed: false);
                break;

            case UnaryExpression unary:
                ResolveExpression(unary.Operand, comprehensionAllowed: false);
                break;

            case ConditionalExpression conditional:
                ResolveExpression(conditional.Condition, comprehensionAllowed: false);
                ResolveExpression(conditional.Then, comprehensionAllowed: false);
                ResolveExpression(conditional.Else, comprehensionAllowed: false);
                break;

            case ParenthesizedExpression parenthesized:
                // A `(generator)` written in vector-element position stays a valid comprehension.
                ResolveExpression(parenthesized.Inner, comprehensionAllowed);
                break;

            case IndexExpression index:
                ResolveExpression(index.Target, comprehensionAllowed: false);
                ResolveExpression(index.Index, comprehensionAllowed: false);
                break;

            case MemberExpression member:
                ResolveExpression(member.Target, comprehensionAllowed: false);
                ValidateMember(member);
                break;

            case FunctionCallExpression call:
                ResolveCall(call);
                break;

            case LetExpression let:
                ResolveBoundExpression(let.Bindings, let.Body);
                break;

            case AssertExpression assert:
                ResolveArguments(assert.Arguments);
                if (assert.Body is not null)
                {
                    ResolveExpression(assert.Body, comprehensionAllowed: false);
                }

                break;

            case EchoExpression echo:
                ResolveArguments(echo.Arguments);
                if (echo.Body is not null)
                {
                    ResolveExpression(echo.Body, comprehensionAllowed: false);
                }

                break;

            case FunctionLiteral literal:
                ResolveFunctionLiteral(literal);
                break;

            case ForComprehension comprehension:
                GuardComprehension("for", comprehensionAllowed, comprehension.Span);
                PushFrame();
                ResolveBindings(comprehension.Bindings);
                ResolveExpression(comprehension.Body, comprehensionAllowed: true);
                PopFrame();
                break;

            case ForCComprehension comprehension:
                GuardComprehension("for", comprehensionAllowed, comprehension.Span);
                PushFrame();
                ResolveBindings(comprehension.Init);
                ResolveExpression(comprehension.Condition, comprehensionAllowed: false);
                ResolveBindings(comprehension.Update);
                ResolveExpression(comprehension.Body, comprehensionAllowed: true);
                PopFrame();
                break;

            case IfComprehension comprehension:
                GuardComprehension("if", comprehensionAllowed, comprehension.Span);
                ResolveExpression(comprehension.Condition, comprehensionAllowed: false);
                ResolveExpression(comprehension.Then, comprehensionAllowed: true);
                if (comprehension.Else is not null)
                {
                    ResolveExpression(comprehension.Else, comprehensionAllowed: true);
                }

                break;

            case LetComprehension comprehension:
                GuardComprehension("let", comprehensionAllowed, comprehension.Span);
                PushFrame();
                ResolveBindings(comprehension.Bindings);
                ResolveExpression(comprehension.Body, comprehensionAllowed: true);
                PopFrame();
                break;

            case EachExpression each:
                GuardComprehension("each", comprehensionAllowed, each.Span);
                ResolveExpression(each.Value, comprehensionAllowed: true);
                break;
        }
    }

    private void ResolveCall(FunctionCallExpression call)
    {
        // The callee identifier is the function reference; any other callee is an expression that
        // yields a function value (e.g. an immediately-invoked function literal).
        if (call.Callee is Identifier callee)
        {
            RecordResolution(callee, ResolveFunctionCall(callee));
        }
        else
        {
            ResolveExpression(call.Callee, comprehensionAllowed: false);
        }

        ResolveArguments(call.Arguments);
    }

    private void ResolveFunctionLiteral(FunctionLiteral literal)
    {
        ResolveParameterDefaults(literal.Parameters);
        PushFrame();
        AddParameters(literal.Parameters);
        ResolveExpression(literal.Body, comprehensionAllowed: false);
        PopFrame();
    }

    private void ResolveBoundExpression(IReadOnlyList<Binding> bindings, Expression body)
    {
        PushFrame();
        ResolveBindings(bindings);
        ResolveExpression(body, comprehensionAllowed: false);
        PopFrame();
    }

    private void ResolveArguments(IReadOnlyList<Argument> arguments)
    {
        foreach (Argument argument in arguments)
        {
            ResolveExpression(argument.Value, comprehensionAllowed: false);
        }
    }

    /// <summary>Resolves binding values left-to-right, each seeing the bindings added before it, then
    /// adds the bound name to the current (already-pushed) frame. Mirrors OpenSCAD's sequential
    /// <c>let</c>/<c>for</c> binding visibility.</summary>
    private void ResolveBindings(IReadOnlyList<Binding> bindings)
    {
        foreach (Binding binding in bindings)
        {
            ResolveExpression(binding.Value, comprehensionAllowed: false);
            _scopeChain[^1].Variables.Add(binding.Name);
        }
    }

    private void ResolveParameterDefaults(IReadOnlyList<Parameter> parameters)
    {
        foreach (Parameter parameter in parameters)
        {
            if (parameter.DefaultValue is not null)
            {
                ResolveExpression(parameter.DefaultValue, comprehensionAllowed: false);
            }
        }
    }

    private void AddParameters(IReadOnlyList<Parameter> parameters)
    {
        foreach (Parameter parameter in parameters)
        {
            _scopeChain[^1].Variables.Add(parameter.Name);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Resolution rules (§5)
    // ---------------------------------------------------------------------------------------------

    private Symbol? ResolveVariableRead(Identifier identifier)
    {
        string name = identifier.Name;
        if (Builtins.IsSpecialVariable(name))
        {
            return null; // special (dynamically-scoped); never renameable
        }

        if (IsLocal(name, Kind.Variable))
        {
            return null; // parameter / let / for / comprehension binding
        }

        Symbol? symbol = LookupOwnOrIncluded(name, Kind.Variable);
        if (symbol is not null)
        {
            return symbol;
        }

        if (Builtins.IsConstant(name))
        {
            return null; // PI
        }

        ReportUnknown("variable", name, identifier.Span);
        return null;
    }

    private Symbol? ResolveModuleCall(ModuleInstantiation instantiation)
    {
        string name = instantiation.Name;
        if (IsLocal(name, Kind.Module))
        {
            return null; // nested/local module definition
        }

        Symbol? symbol = LookupOwnOrIncluded(name, Kind.Module);
        if (symbol is not null)
        {
            return symbol;
        }

        if (Builtins.IsModule(name))
        {
            return null; // built-in module shadowed only by own/included scope
        }

        symbol = LookupUsedDefinition(name, Kind.Module);
        if (symbol is not null)
        {
            return symbol;
        }

        ReportUnknown("module", name, instantiation.Span);
        return null;
    }

    private Symbol? ResolveFunctionCall(Identifier callee)
    {
        string name = callee.Name;
        if (IsLocal(name, Kind.Function))
        {
            return null;
        }

        Symbol? symbol = LookupOwnOrIncluded(name, Kind.Function);
        if (symbol is not null)
        {
            return symbol;
        }

        if (Builtins.IsFunction(name))
        {
            return null;
        }

        symbol = LookupUsedDefinition(name, Kind.Function);
        if (symbol is not null)
        {
            return symbol;
        }

        ReportUnknown("function", name, callee.Span);
        return null;
    }

    private bool IsLocal(string name, Kind kind)
    {
        foreach (LocalFrame frame in _scopeChain)
        {
            if (frame.Names(kind).Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Looks a name up in the file's flat <c>include</c>-merged scope: own declarations plus
    /// every <c>include</c>d file's declarations, last-wins by document order (see
    /// <see cref="BuildMergedScope"/>). Used libraries are NOT consulted here — variables are never
    /// imported by <c>use</c>, and call resolution checks built-ins before used libraries.</summary>
    private Symbol? LookupOwnOrIncluded(string name, Kind kind) =>
        Table(_currentEnv.Merged, kind).GetValueOrDefault(name);

    private Symbol? LookupUsedDefinition(string name, Kind kind)
    {
        // `Used` is stored last-`use`-first (per `SourceFile::registerUse` front-insertion), so the
        // first match is the most recent `use` — last-`use`-wins.
        foreach (FileScope scope in _currentEnv.Used)
        {
            if (Table(scope, kind).TryGetValue(name, out Symbol? symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    private static Dictionary<string, Symbol> Table(FileScope scope, Kind kind) => kind switch
    {
        Kind.Module => scope.Modules,
        Kind.Function => scope.Functions,
        _ => scope.Variables,
    };

    // ---------------------------------------------------------------------------------------------
    // Recording & diagnostics
    // ---------------------------------------------------------------------------------------------

    private void RecordResolution(AstNode reference, Symbol? symbol)
    {
        if (symbol is null)
        {
            return;
        }

        _resolution[reference] = symbol;

        if (!_referencesTo.TryGetValue(symbol.Declaration, out List<AstNode>? references))
        {
            references = [];
            _referencesTo[symbol.Declaration] = references;
        }

        references.Add(reference);

        // Record own-file reachability edges for PrivateConstants (geometry has no current decl).
        if (_currentTopLevelDecl is not null && symbol.File == _currentFile)
        {
            if (!_ownFileReferences.TryGetValue(_currentTopLevelDecl, out List<Symbol>? edges))
            {
                edges = [];
                _ownFileReferences[_currentTopLevelDecl] = edges;
            }

            edges.Add(symbol);
        }
    }

    private void ValidateMember(MemberExpression member)
    {
        if (member.Member.Length == 0 || member.Member is "x" or "y" or "z")
        {
            return; // valid component, or an empty name from parser recovery (already diagnosed)
        }

        // Point at the member name itself (after the dot), not the whole access expression.
        SourcePosition end = member.Span.End;
        var start = new SourcePosition(
            end.Offset - member.Member.Length, end.Line, end.Column - member.Member.Length);
        _diagnostics.Error(
            DiagnosticCode.InvalidMemberAccess,
            $"Invalid member '.{member.Member}'; only .x, .y, and .z are valid vector components.",
            new SourceSpan(member.Span.File, start, end));
    }

    private void GuardComprehension(string keyword, bool comprehensionAllowed, SourceSpan span)
    {
        if (!comprehensionAllowed)
        {
            _diagnostics.Error(
                DiagnosticCode.ComprehensionOutsideVector,
                $"'{keyword}' generator is only valid inside a list comprehension '[ ... ]'.",
                span);
        }
    }

    private void ReportUnknown(string kind, string name, SourceSpan span)
    {
        // Conservative: only when the file's whole include-closure is fully resolved (we can see
        // every declaration it could reach), mirroring OpenSCAD's "Ignoring unknown …" warnings.
        if (_currentEnv.Complete)
        {
            _diagnostics.Warning(DiagnosticCode.UnknownReference, $"Unknown {kind} '{name}'.", span);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Environments & completeness
    // ---------------------------------------------------------------------------------------------

    private FileEnvironment BuildEnvironment(LoadedFile file)
    {
        var used = new List<FileScope>();
        foreach (UseEdge edge in file.Uses)
        {
            if (edge.Target is not null && !edge.FontPassthrough && _scopes.TryGetValue(edge.Target.Source, out FileScope? scope))
            {
                used.Add(scope);
            }
        }

        // Store last-`use`-first so the most recent `use` is consulted first (last-`use`-wins).
        used.Reverse();

        return new FileEnvironment
        {
            Merged = BuildMergedScope(file),
            Used = used,
            Complete = IsComplete(file),
        };
    }

    /// <summary>Builds the file's flat <c>include</c>-merged scope in OpenSCAD document order: each
    /// file's own top-level declarations interleaved with its <c>include</c>d files' declarations,
    /// spliced in at the include's position, recursively. Because later declarations overwrite earlier
    /// ones (<c>LocalScope.cc</c> flat-scope last-wins), inserting in document order leaves each name
    /// bound to its <i>last</i> definition — the same ordering the inliner's reference rewriter relies
    /// on (<see cref="Inlining.Inliner"/> flattens includes identically).</summary>
    private static FileScope BuildMergedScope(LoadedFile file)
    {
        var merged = new FileScope();
        AppendDeclarations(file, merged, []);
        return merged;
    }

    private static void AppendDeclarations(LoadedFile file, FileScope merged, HashSet<SourceFile> stack)
    {
        if (!stack.Add(file.Source))
        {
            return; // defensive: never recurse a file already on the splice stack (include cycle)
        }

        Dictionary<IncludeStatement, LoadedFile?> targets = IncludeTargets(file);
        foreach (Statement statement in file.Ast.Statements)
        {
            switch (statement)
            {
                case IncludeStatement include:
                    if (targets.TryGetValue(include, out LoadedFile? target) && target is not null)
                    {
                        AppendDeclarations(target, merged, stack);
                    }

                    break;
                case ModuleDefinition module:
                    merged.Modules[module.Name] = new Symbol(SymbolKind.Module, module.Name, file.Source, module);
                    break;
                case FunctionDefinition function:
                    merged.Functions[function.Name] = new Symbol(SymbolKind.Function, function.Name, file.Source, function);
                    break;
                case AssignmentStatement assignment:
                    merged.Variables[assignment.Name] = new Symbol(SymbolKind.Variable, assignment.Name, file.Source, assignment);
                    break;
            }
        }

        stack.Remove(file.Source);
    }

    private static Dictionary<IncludeStatement, LoadedFile?> IncludeTargets(LoadedFile file)
    {
        var map = new Dictionary<IncludeStatement, LoadedFile?>(ReferenceEqualityComparer.Instance);
        foreach (IncludeEdge edge in file.Includes)
        {
            map[edge.Statement] = edge.Target;
        }

        return map;
    }

    /// <summary>True when <paramref name="file"/> and every file in its <c>include</c>-closure have all
    /// their <c>include</c>/<c>use</c> statements resolved, so the analyzer can see every reachable
    /// declaration (the precondition for confident SB3005).</summary>
    private static bool IsComplete(LoadedFile file)
    {
        var visited = new HashSet<SourceFile>();
        var stack = new Stack<LoadedFile>();
        stack.Push(file);
        while (stack.Count > 0)
        {
            LoadedFile current = stack.Pop();
            if (!visited.Add(current.Source))
            {
                continue;
            }

            if (HasUnresolvedEdges(current))
            {
                return false;
            }

            EnqueueIncludesToStack(current, stack);
        }

        return true;
    }

    private static void EnqueueIncludesToStack(LoadedFile file, Stack<LoadedFile> stack)
    {
        foreach (IncludeEdge edge in file.Includes)
        {
            if (edge.Target is not null)
            {
                stack.Push(edge.Target);
            }
        }
    }

    private static bool HasUnresolvedEdges(LoadedFile file)
    {
        int statements = CountIncludeUseStatements(file.Ast);
        if (statements > file.Includes.Count + file.Uses.Count)
        {
            return true; // include/use statements with no edge (e.g. single-file Analyze)
        }

        return file.Includes.Any(edge => edge.Target is null)
            || file.Uses.Any(edge => edge.Target is null && !edge.FontPassthrough);
    }

    private static int CountIncludeUseStatements(ScadFile ast)
    {
        int count = 0;
        foreach (Statement statement in ast.Statements)
        {
            if (statement is IncludeStatement or UseStatement)
            {
                count++;
            }
        }

        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Scope-local collection
    // ---------------------------------------------------------------------------------------------

    /// <summary>Collects the names a module body binds in its own scope: direct variable assignments,
    /// nested definitions, and those reached through plain blocks, <c>if</c> branches, and geometry
    /// children — but NOT through nested scopes (<c>for</c>/<c>let</c>/defs), which own their bindings.</summary>
    private static void CollectBodyLocals(Statement body, LocalFrame frame)
    {
        switch (body)
        {
            case AssignmentStatement assignment:
                frame.Variables.Add(assignment.Name);
                break;
            case ModuleDefinition module:
                frame.Modules.Add(module.Name);
                break;
            case FunctionDefinition function:
                frame.Functions.Add(function.Name);
                break;
            case BlockStatement block:
                foreach (Statement statement in block.Statements)
                {
                    CollectBodyLocals(statement, frame);
                }

                break;
            case IfStatement branch:
                CollectBodyLocals(branch.Then, frame);
                if (branch.Else is not null)
                {
                    CollectBodyLocals(branch.Else, frame);
                }

                break;
            case ModuleInstantiation instantiation:
                if (instantiation.Child is not null)
                {
                    CollectBodyLocals(instantiation.Child, frame);
                }

                break;
        }
    }

    private void PushFrame() => _scopeChain.Add(new LocalFrame());

    private void PopFrame() => _scopeChain.RemoveAt(_scopeChain.Count - 1);

    // ---------------------------------------------------------------------------------------------
    // Supporting types
    // ---------------------------------------------------------------------------------------------

    private enum Kind
    {
        Variable,
        Module,
        Function,
    }

    /// <summary>A file's top-level declarations: last-wins lookup tables (for resolution) plus
    /// declaration-ordered lists (for the model queries).</summary>
    private sealed class FileScope
    {
        public Dictionary<string, Symbol> Modules { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Symbol> Functions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Symbol> Variables { get; } = new(StringComparer.Ordinal);

        public List<ModuleDefinition> ModuleList { get; } = [];

        public List<FunctionDefinition> FunctionList { get; } = [];

        public List<AssignmentStatement> VariableList { get; } = [];
    }

    /// <summary>The lexical environment a file's references resolve against.</summary>
    private sealed class FileEnvironment
    {
        public static readonly FileEnvironment Empty = new()
        {
            Merged = new FileScope(),
            Used = [],
            Complete = false,
        };

        /// <summary>The flat <c>include</c>-merged scope: own + every <c>include</c>d file's
        /// declarations, last-wins by document order (see <see cref="BuildMergedScope"/>).</summary>
        public required FileScope Merged { get; init; }

        public required IReadOnlyList<FileScope> Used { get; init; }

        public required bool Complete { get; init; }
    }

    /// <summary>A pushed local scope's bound names, by namespace.</summary>
    private sealed class LocalFrame
    {
        public HashSet<string> Variables { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Modules { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Functions { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Names(Kind kind) => kind switch
        {
            Kind.Module => Modules,
            Kind.Function => Functions,
            _ => Variables,
        };
    }
}
