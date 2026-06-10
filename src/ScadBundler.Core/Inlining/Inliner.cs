using System.Globalization;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// Flattens a <see cref="LoadGraph"/> into one bundled <see cref="ScadFile"/> AST, consuming the
/// pre-inline <see cref="ISemanticModel"/> for reference/symbol facts. Six phases (Spec §6): inline
/// <c>include</c> (document order), import <c>use</c>d definitions + their private constants, resolve
/// collisions (last-wins / namespace), deduplicate structurally-identical defs, normalize deprecated
/// constructs, and assemble. Assembly hoists the root file's Customizer parameter prologue to the top
/// (verbatim, never renamed) and fences everything else behind a synthesized <c>/* [Hidden] */</c>, so
/// OpenSCAD's Customizer still shows the model's parameters. With <see cref="BundleOptions.BundleLicenses"/>
/// (default on), the <see cref="Attribution"/> pass additionally hoists every bundled file's leading
/// header/license comments into a deduplicated block at the very top (SB5007) and separates the inlined
/// sections with one-line provenance banners. Never throws — every problem is a diagnostic.
/// </summary>
public static class Inliner
{
    /// <summary>Bundles <paramref name="graph"/> using <paramref name="model"/>. Never throws.</summary>
    /// <param name="graph">The loaded graph (root + dependency closure).</param>
    /// <param name="model">The semantic model built over the pre-inline graph.</param>
    /// <param name="options">Collision strategy and assembly options.</param>
    /// <returns>The bundled AST plus inliner diagnostics (loader/semantic diagnostics are merged by <see cref="Bundler"/>).</returns>
    public static (ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics) Bundle(
        LoadGraph graph, ISemanticModel model, BundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        return new Run(graph, model, options).Execute();
    }

    private enum DefKind
    {
        Module,
        Function,
        Variable,
    }

    // One occurrence of a renameable declaration in the merged emit sequence (a class, so two
    // occurrences of the same node — e.g. a diamond splice — are distinct keys).
    private sealed class Candidate
    {
        public required Statement Node { get; init; }

        public required DefKind Kind { get; init; }

        public required string Name { get; init; }

        public required bool FromUse { get; init; }

        // A root Customizer-parameter prologue assignment: emitted verbatim at the top and never
        // renamed/namespaced/dropped, so the end user still sees it in the Customizer.
        public bool Protected { get; init; }
    }

    private sealed class Run
    {
        private readonly LoadGraph _graph;
        private readonly ISemanticModel _model;
        private readonly BundleOptions _options;
        private readonly DiagnosticBag _diagnostics = new();

        private readonly Dictionary<AstNode, string> _renames = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<AstNode> _winners = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _takenNames = new(StringComparer.Ordinal);
        private Attribution? _attribution;
        private bool _errorCollision;

        public Run(LoadGraph graph, ISemanticModel model, BundleOptions options)
        {
            _graph = graph;
            _model = model;
            _options = options;
        }

        public (ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics) Execute()
        {
            LoadedFile root = _graph.Root;

            // Phase L — attribution (default-on --bundle-licenses): collect every file's leading
            // header/license comments (encounter order, root first) + per-file provenance labels.
            _attribution = _options.BundleLicenses ? Attribution.Collect(_graph) : null;

            // Phase A0 — the root's Customizer parameter prologue (hoisted to the top; never renamed).
            var prologueNodes = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
            List<AssignmentStatement> prologue = ExtractPrologue(root, prologueNodes);

            // Phase A — inline includes (document order); hoist use/font edges out of the flat list.
            (List<LoadedFile> usedFiles, List<UseStatement> fontUses) = DiscoverUses(root);
            List<Statement> rootFlat = FlattenIncludes(root, []);

            // Phase B — import used definitions + their private constants.
            List<Candidate> useItems = GatherUseImports(usedFiles);

            // Phases C/D — collisions + dedup over the merged definition/variable set.
            List<Candidate> rootDefs = RootDefinitions(rootFlat, prologueNodes);
            ResolveCollisions(useItems, rootDefs);

            // Phases E/F — assemble + single rewrite pass (renames + normalization).
            var rewriter = new BundleRewriter(_renames, _diagnostics);
            ScadFile bundled = Assemble(root, fontUses, useItems, rootFlat, prologue, prologueNodes, rewriter);

            if (_attribution is { AggregatedHeaderCount: > 0 } && !_errorCollision)
            {
                _diagnostics.Info(
                    DiagnosticCode.LicensesAggregated,
                    $"Aggregated {_attribution.AggregatedHeaderCount.ToString(CultureInfo.InvariantCulture)} "
                    + "file header(s) into the bundle header.",
                    root.Ast.Span);
            }

            IReadOnlyList<Diagnostic> sorted = Sort(_diagnostics.ToList());
            return (bundled, sorted);
        }

        // The root file's leading run of top-level assignments — the model's Customizer parameters.
        // OpenSCAD only shows literal top-level assignments that precede the first '{' and physically
        // belong to the root file (CommentParser::collectParameters + getLineToStop). Bundling splices
        // included libraries above the root's own assignments, pushing the real parameters past that
        // cutoff; hoisting this prologue back to the top (and fencing the rest with '/* [Hidden] */' in
        // Assemble) restores them. Leading include/use/empty statements are skipped; the run ends at the
        // first definition, instantiation, or control-flow/block statement.
        private static List<AssignmentStatement> ExtractPrologue(LoadedFile root, HashSet<AstNode> nodes)
        {
            var prologue = new List<AssignmentStatement>();
            foreach (Statement statement in root.Ast.Statements)
            {
                if (statement is AssignmentStatement assignment)
                {
                    prologue.Add(assignment);
                    nodes.Add(assignment);
                    continue;
                }

                if (statement is IncludeStatement or UseStatement or EmptyStatement)
                {
                    continue; // inert here: includes/uses are flattened/imported by later phases
                }

                break; // first definition/instantiation/control-flow ends the parameter block
            }

            return prologue;
        }

        // ---------------------------------------------------------------------------------------------
        // Phase A — include flattening + use discovery
        // ---------------------------------------------------------------------------------------------

        private static List<Statement> FlattenIncludes(LoadedFile file, HashSet<SourceFile> stack)
        {
            var result = new List<Statement>();
            if (!stack.Add(file.Source))
            {
                return result; // defensive: never recurse a file already on the splice stack
            }

            Dictionary<IncludeStatement, LoadedFile?> targets = IncludeTargets(file);
            foreach (Statement statement in file.Ast.Statements)
            {
                switch (statement)
                {
                    case IncludeStatement include:
                        if (targets.TryGetValue(include, out LoadedFile? target) && target is not null)
                        {
                            result.AddRange(FlattenIncludes(target, stack));
                        }

                        break; // unresolved include: dropped (already SB4001)
                    case UseStatement:
                        break; // hoisted to the use-set (Phase B); not spliced
                    default:
                        result.Add(statement);
                        break;
                }
            }

            stack.Remove(file.Source);
            return result;
        }

        // Used files reachable through the root's include-closure (and transitively through used files),
        // in first-use order; plus font pass-throughs, deduped by raw path.
        private static (List<LoadedFile> UsedFiles, List<UseStatement> FontUses) DiscoverUses(LoadedFile root)
        {
            var usedFiles = new List<LoadedFile>();
            var fontUses = new List<UseStatement>();
            var usedSeen = new HashSet<SourceFile>();
            var fontSeen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<LoadedFile>();

            void Collect(LoadedFile file)
            {
                foreach (UseEdge edge in file.Uses)
                {
                    if (edge.FontPassthrough)
                    {
                        if (fontSeen.Add(edge.Statement.RawPath))
                        {
                            fontUses.Add(edge.Statement);
                        }
                    }
                    else if (edge.Target is not null && usedSeen.Add(edge.Target.Source))
                    {
                        usedFiles.Add(edge.Target);
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            foreach (LoadedFile file in IncludeClosure(root))
            {
                Collect(file);
            }

            while (queue.Count > 0)
            {
                foreach (LoadedFile file in IncludeClosure(queue.Dequeue()))
                {
                    Collect(file);
                }
            }

            return (usedFiles, fontUses);
        }

        // ---------------------------------------------------------------------------------------------
        // Phase B — use imports
        // ---------------------------------------------------------------------------------------------

        private List<Candidate> GatherUseImports(List<LoadedFile> usedFiles)
        {
            var items = new List<Candidate>();
            var importedFiles = new HashSet<SourceFile>();
            foreach (LoadedFile used in usedFiles)
            {
                foreach (LoadedFile file in IncludeClosure(used))
                {
                    if (!importedFiles.Add(file.Source))
                    {
                        continue; // import each file's defs once
                    }

                    var privateConstants = new HashSet<AssignmentStatement>(
                        _model.PrivateConstants(file.Source), ReferenceEqualityComparer.Instance);

                    foreach (Statement statement in file.Ast.Statements)
                    {
                        switch (statement)
                        {
                            case ModuleDefinition module:
                                items.Add(new Candidate { Node = module, Kind = DefKind.Module, Name = module.Name, FromUse = true });
                                break;
                            case FunctionDefinition function:
                                items.Add(new Candidate { Node = function, Kind = DefKind.Function, Name = function.Name, FromUse = true });
                                break;
                            case AssignmentStatement assignment when privateConstants.Contains(assignment):
                                items.Add(new Candidate { Node = assignment, Kind = DefKind.Variable, Name = assignment.Name, FromUse = true });
                                break;
                        }
                    }
                }
            }

            return items;
        }

        private static List<Candidate> RootDefinitions(List<Statement> rootFlat, HashSet<AstNode> prologueNodes)
        {
            var result = new List<Candidate>();
            foreach (Statement statement in rootFlat)
            {
                switch (statement)
                {
                    case ModuleDefinition module:
                        result.Add(new Candidate { Node = module, Kind = DefKind.Module, Name = module.Name, FromUse = false });
                        break;
                    case FunctionDefinition function:
                        result.Add(new Candidate { Node = function, Kind = DefKind.Function, Name = function.Name, FromUse = false });
                        break;
                    case AssignmentStatement assignment:
                        result.Add(new Candidate
                        {
                            Node = assignment,
                            Kind = DefKind.Variable,
                            Name = assignment.Name,
                            FromUse = false,
                            Protected = prologueNodes.Contains(assignment),
                        });
                        break;
                }
            }

            return result;
        }

        // ---------------------------------------------------------------------------------------------
        // Phases C/D — collision resolution + dedup
        // ---------------------------------------------------------------------------------------------

        private void ResolveCollisions(List<Candidate> useItems, List<Candidate> rootDefs)
        {
            // Emit order: use-imports (hoisted to top) then include-flattened root defs.
            var all = new List<Candidate>(useItems.Count + rootDefs.Count);
            all.AddRange(useItems);
            all.AddRange(rootDefs);

            foreach (Candidate candidate in all)
            {
                _takenNames.Add(candidate.Name);
            }

            foreach (IGrouping<(DefKind Kind, string Name), Candidate> group in
                all.GroupBy(c => (c.Kind, c.Name)))
            {
                ResolveGroup([.. group]);
            }
        }

        private void ResolveGroup(List<Candidate> group)
        {
            List<Candidate> reps = Deduplicate(group);
            if (reps.Count <= 1)
            {
                foreach (Candidate rep in reps)
                {
                    // A `use`-import is isolated by construction (OpenSCAD evaluates it in its own
                    // FileContext, ScopeContext.cc), so it is always namespaced — not only on a detected
                    // clash (ADR 0001: "safe by construction"). A non-clashing import is namespaced
                    // silently: SB5004 would otherwise fire once per library symbol; the collision paths
                    // below still warn for genuine clashes.
                    if (rep is { FromUse: true, Protected: false })
                    {
                        NamespaceRep(rep, report: false);
                    }
                    else
                    {
                        _winners.Add(rep.Node);
                    }
                }

                return;
            }

            // A root Customizer parameter must survive verbatim (the end user reads it). When one
            // collides, protect it and resolve the collision by namespacing every other definition of
            // the name — regardless of the configured strategy.
            if (reps.Exists(r => r.Protected))
            {
                foreach (Candidate rep in reps)
                {
                    if (rep.Protected)
                    {
                        _winners.Add(rep.Node);
                    }
                    else
                    {
                        NamespaceRep(rep);
                    }
                }

                return;
            }

            switch (_options.OnCollision)
            {
                case CollisionStrategy.Prefix:
                    ResolvePrefix(reps);
                    break;

                case CollisionStrategy.KeepFirst:
                    _winners.Add(reps[0].Node); // drop the rest silently (forced strategy)
                    break;

                case CollisionStrategy.KeepLast:
                    KeepLastWins(reps);
                    break;

                case CollisionStrategy.Error:
                    _errorCollision = true;
                    ReportCollisionError(reps); // hard-fail: Error-severity diagnostics; output emptied below
                    break;

                default:
                    ResolveAuto(reps);
                    break;
            }
        }

        // Auto (origin-dependent): namespace every use-origin def; last-wins among include-origin defs.
        private void ResolveAuto(List<Candidate> reps)
        {
            foreach (Candidate rep in reps.Where(r => r.FromUse))
            {
                NamespaceRep(rep);
            }

            List<Candidate> includeReps = [.. reps.Where(r => !r.FromUse)];
            if (includeReps.Count == 0)
            {
                return;
            }

            KeepLastWins(includeReps);
        }

        // Prefix: namespace every colliding definition (both survive). A use-origin def is isolated in
        // its own per-file FileContext, so each keeps its own references. include-origin defs share one
        // flat scope (LocalScope.cc last-wins), so every reference to the colliding name must bind to the
        // last include-origin definition — it must NOT be distributed across the namespaced copies by the
        // pre-inline model, which resolves each reference against its own file's view and would mis-bind a
        // cross-include call (e.g. an a.scad-internal call to a name that b.scad redefines).
        private void ResolvePrefix(List<Candidate> reps)
        {
            foreach (Candidate rep in reps.Where(r => r.FromUse))
            {
                NamespaceRep(rep);
            }

            List<Candidate> includeReps = [.. reps.Where(r => !r.FromUse)];
            if (includeReps.Count == 0)
            {
                return;
            }

            foreach (Candidate rep in includeReps)
            {
                RenameDeclaration(rep);
            }

            // The bundle's flat scope binds the name to the last include-origin definition; point every
            // reference there (the earlier namespaced copies become dead code, as in OpenSCAD).
            RedirectReferences(includeReps, _renames[includeReps[^1].Node]);
        }

        // Keep the last definition (highest emit position); each earlier one is a redefinition the last
        // overrides — SB3004 (module/function) / SB3003 (variable), matching OpenSCAD flat-scope last-wins.
        private void KeepLastWins(List<Candidate> reps)
        {
            _winners.Add(reps[^1].Node);
            for (int i = 1; i < reps.Count; i++)
            {
                ReportRedefinition(reps[i], reps[i - 1]);
            }
        }

        private void ReportRedefinition(Candidate redefinition, Candidate previous)
        {
            if (redefinition.Kind == DefKind.Variable)
            {
                _diagnostics.Warning(
                    DiagnosticCode.VariableReassigned,
                    $"Variable '{redefinition.Name}' was assigned on line {previous.Node.Span.Start.Line} but is overwritten; the last assignment wins.",
                    redefinition.Node.Span);
            }
            else
            {
                _diagnostics.Warning(
                    DiagnosticCode.DefinitionRedefined,
                    $"{Noun(redefinition.Kind)} '{redefinition.Name}' is redefined; the last definition wins.",
                    redefinition.Node.Span);
            }
        }

        // `--on-collision error`: every genuine collision is a hard failure. Emit one Error-severity
        // diagnostic per colliding site (so the CLI exits non-zero, vs. the keep-last warning churn of
        // the other strategies); the whole bundle is emptied in Assemble.
        private void ReportCollisionError(List<Candidate> reps)
        {
            for (int i = 1; i < reps.Count; i++)
            {
                Candidate previous = reps[i - 1];
                _diagnostics.Error(
                    DiagnosticCode.CollisionError,
                    $"Collision: {KindNoun(reps[i].Kind)} '{reps[i].Name}' is also defined at "
                    + $"{previous.Node.Span.File.Path}:{previous.Node.Span.Start.Line.ToString(CultureInfo.InvariantCulture)}; "
                    + "no output is produced under '--on-collision error'.",
                    reps[i].Node.Span);
            }
        }

        // Collapse structurally-identical copies (diamond include/use). Modules/functions dedup by
        // content key (SB5005); variables collapse only exact same-node duplicates (a constant spliced
        // twice) silently — distinct same-name variables are a genuine reassignment, not a dup.
        private List<Candidate> Deduplicate(List<Candidate> group)
        {
            if (group[0].Kind == DefKind.Variable)
            {
                var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
                return [.. group.Where(c => seen.Add(c.Node))];
            }

            var clusters = new Dictionary<string, (Candidate Rep, int Count)>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Candidate candidate in group)
            {
                string key = StructuralKey.Of(candidate.Node);
                if (clusters.TryGetValue(key, out (Candidate Rep, int Count) existing))
                {
                    clusters[key] = (existing.Rep, existing.Count + 1);
                }
                else
                {
                    clusters[key] = (candidate, 1);
                    order.Add(key);
                }
            }

            var reps = new List<Candidate>(order.Count);
            foreach (string key in order)
            {
                (Candidate rep, int count) = clusters[key];
                if (count >= 2)
                {
                    _diagnostics.Info(
                        DiagnosticCode.DuplicateMerged,
                        $"Duplicate definition '{rep.Name}' merged ({count.ToString(CultureInfo.InvariantCulture)} copies).",
                        rep.Node.Span);
                }

                reps.Add(rep);
            }

            return reps;
        }

        // Namespaces one definition: rename the declaration and the references the model bound to it.
        // Correct for the per-file-isolated cases — `use`-imports and the non-protected side of a
        // protected-prologue collision — where the model's binding already matches the bundle's scope.
        // <paramref name="report"/> is false for a by-construction `use`-import (no clash → no SB5004).
        private void NamespaceRep(Candidate rep, bool report = true)
        {
            string newName = RenameDeclaration(rep, report);
            foreach (AstNode reference in _model.ReferencesTo(SymbolFor(rep)))
            {
                _renames[reference] = newName;
            }
        }

        // Renames a colliding declaration to a unique namespaced name (`<filestem>__name`), records it as
        // a winner, and (when <paramref name="report"/>) emits SB5004. References are NOT rewritten here —
        // the caller binds them (so a shared flat scope can point every reference at one surviving
        // definition; see ResolvePrefix).
        private string RenameDeclaration(Candidate rep, bool report = true)
        {
            string file = rep.Node.Span.File.Path;
            string stem = Sanitize(Path.GetFileNameWithoutExtension(file));
            string newName = UniqueName($"{stem}__{rep.Name}");
            _winners.Add(rep.Node);
            _renames[rep.Node] = newName;

            if (report)
            {
                _diagnostics.Warning(
                    DiagnosticCode.NameRenamed,
                    $"'{rep.Name}' from '{file}' renamed to '{newName}' to resolve a collision.",
                    rep.Node.Span);
            }

            return newName;
        }

        // Points every reference the pre-inline model bound to any of <paramref name="reps"/> at one name
        // — the bundle's flat-scope binding target — rather than trusting the per-file resolution.
        private void RedirectReferences(IEnumerable<Candidate> reps, string targetName)
        {
            foreach (Candidate rep in reps)
            {
                foreach (AstNode reference in _model.ReferencesTo(SymbolFor(rep)))
                {
                    _renames[reference] = targetName;
                }
            }
        }

        private string UniqueName(string baseName)
        {
            if (_takenNames.Add(baseName))
            {
                return baseName;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{baseName}_{suffix.ToString(CultureInfo.InvariantCulture)}";
                if (_takenNames.Add(candidate))
                {
                    return candidate;
                }
            }
        }

        private static Symbol SymbolFor(Candidate candidate) => new(
            candidate.Kind switch
            {
                DefKind.Module => SymbolKind.Module,
                DefKind.Function => SymbolKind.Function,
                _ => SymbolKind.Variable,
            },
            candidate.Name,
            candidate.Node.Span.File,
            candidate.Node);

        // ---------------------------------------------------------------------------------------------
        // Phase F — assembly
        // ---------------------------------------------------------------------------------------------

        private ScadFile Assemble(
            LoadedFile root,
            List<UseStatement> fontUses,
            List<Candidate> useItems,
            List<Statement> rootFlat,
            List<AssignmentStatement> prologue,
            HashSet<AstNode> prologueNodes,
            BundleRewriter rewriter)
        {
            if (_errorCollision)
            {
                return new ScadFile(root.Source, []); // `Error` strategy: a collision means no output
            }

            var emitted = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);

            // Provenance-banner state (attribution on): a one-line banner marks every change of origin
            // file between consecutive emitted statements; a re-entered file is "(continued)".
            SourceFile currentOrigin = root.Source;
            var appearedOrigins = new HashSet<SourceFile>();

            Statement Finish(Statement statement, bool banner)
            {
                statement = StripHoisted(statement);
                if (_attribution is null)
                {
                    return statement;
                }

                SourceFile origin = statement.Span.File;
                if (origin == SourceFile.Synthesized)
                {
                    return statement; // no real origin: stays in the current section
                }

                if (banner && origin != currentOrigin)
                {
                    statement = WithSectionBanner(statement, _attribution.LabelFor(origin), appearedOrigins.Contains(origin));
                }

                currentOrigin = origin;
                appearedOrigins.Add(origin);
                return statement;
            }

            // The Customizer parameter prologue leads, verbatim (never renamed, so its names stay
            // user-facing). It is exempt from collision/dedup dropping — it is always emitted here.
            var statements = new List<Statement>();
            foreach (AssignmentStatement parameter in prologue)
            {
                if (emitted.Add(parameter))
                {
                    statements.Add(Finish(rewriter.RewriteStatement(parameter), banner: false));
                }
            }

            // Everything else: fonts, then use-imports, then the include-flattened body.
            var rest = new List<Statement>();
            foreach (UseStatement font in fontUses)
            {
                rest.Add(Finish(font, banner: true)); // a binary font cannot be inlined — preserved verbatim
            }

            foreach (Candidate item in useItems)
            {
                if (_winners.Contains(item.Node) && emitted.Add(item.Node))
                {
                    rest.Add(Finish(rewriter.RewriteStatement(item.Node), banner: true));
                }
            }

            foreach (Statement statement in rootFlat)
            {
                if (prologueNodes.Contains(statement))
                {
                    continue; // hoisted into the prologue above
                }

                if (statement is ModuleDefinition or FunctionDefinition or AssignmentStatement)
                {
                    if (!_winners.Contains(statement) || !emitted.Add(statement))
                    {
                        continue; // dropped by collision resolution, or an already-emitted diamond copy
                    }
                }

                rest.Add(Finish(rewriter.RewriteStatement(statement), banner: true));
            }

            // Fence the remaining top-level assignments out of the Customizer with a synthesized
            // `/* [Hidden] */` boundary, so only the root's parameters surface (OpenSCAD's Hidden group).
            // Only needed when a body assignment could otherwise be picked up as a parameter.
            if (rest.Exists(s => s is AssignmentStatement))
            {
                rest[0] = WithHiddenFence(rest[0]);
            }

            statements.AddRange(rest);

            // The aggregated header/license block leads the whole bundle (above the fence and any
            // banner): the root's own header verbatim, then each distinct non-root header, labeled.
            if (_attribution is { HeaderBlock.Count: > 0 } && statements.Count > 0)
            {
                statements[0] = statements[0] with
                {
                    LeadingTrivia = [.. _attribution.HeaderBlock, .. statements[0].LeadingTrivia],
                };
            }

            return new ScadFile(root.Source, statements);
        }

        // Prepends a synthesized `/* [Hidden] */` Customizer boundary to a statement's leading trivia.
        // Modeled as trivia (not a node) so it round-trips the emitter self-check (a comment re-parses
        // to a comment). Dropped under `--minify`/`--no-preserve-comments`, like all comments.
        private static Statement WithHiddenFence(Statement statement)
        {
            var fence = new CommentTrivia("/* [Hidden] */", CommentKind.Block) { Span = SourceSpan.Synthetic };
            var leading = new List<Trivia>(statement.LeadingTrivia.Count + 1) { fence };
            leading.AddRange(statement.LeadingTrivia);
            return statement with { LeadingTrivia = leading, BlankLineBefore = true };
        }

        // Removes header trivia the attribution pass hoisted into the bundle's top block, so a license
        // is moved — never duplicated mid-bundle. No-op when attribution is off or nothing matches.
        private Statement StripHoisted(Statement statement)
        {
            if (_attribution is not { } attribution
                || statement.LeadingTrivia.Count == 0
                || !statement.LeadingTrivia.Any(attribution.IsHoisted))
            {
                return statement;
            }

            return statement with
            {
                LeadingTrivia = [.. statement.LeadingTrivia.Where(t => !attribution.IsHoisted(t))],
            };
        }

        // Prepends the one-line provenance banner that opens a new origin section, e.g.
        // `// ======== use <gears.scad> ========` — the label echoes the statement that pulled the
        // file in, so a curious reader can map each section back to the original project layout.
        private static Statement WithSectionBanner(Statement statement, string label, bool continued)
        {
            string text = $"// ======== {label}{(continued ? " (continued)" : string.Empty)} ========";
            var banner = new CommentTrivia(text, CommentKind.Line) { Span = SourceSpan.Synthetic };
            var leading = new List<Trivia>(statement.LeadingTrivia.Count + 1) { banner };
            leading.AddRange(statement.LeadingTrivia);
            return statement with { LeadingTrivia = leading, BlankLineBefore = true };
        }

        // ---------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------

        private static List<LoadedFile> IncludeClosure(LoadedFile start)
        {
            var result = new List<LoadedFile>();
            var seen = new HashSet<SourceFile>();
            var stack = new Stack<LoadedFile>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                LoadedFile file = stack.Pop();
                if (!seen.Add(file.Source))
                {
                    continue;
                }

                result.Add(file);

                // Push in reverse so the first include is processed first (document order).
                for (int i = file.Includes.Count - 1; i >= 0; i--)
                {
                    if (file.Includes[i].Target is LoadedFile target)
                    {
                        stack.Push(target);
                    }
                }
            }

            return result;
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

        private static IReadOnlyList<Diagnostic> Sort(IReadOnlyList<Diagnostic> diagnostics) =>
            [.. diagnostics
                .OrderBy(d => d.Span.File.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Span.Start.Offset)
                .ThenBy(d => d.Code, StringComparer.Ordinal)];

        private static string Noun(DefKind kind) => kind == DefKind.Function ? "function" : "module";

        private static string KindNoun(DefKind kind) => kind switch
        {
            DefKind.Function => "function",
            DefKind.Variable => "variable",
            _ => "module",
        };

        private static string Sanitize(string stem)
        {
            if (stem.Length == 0)
            {
                return "lib";
            }

            var chars = stem.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                {
                    chars[i] = '_';
                }
            }

            string sanitized = new(chars);
            return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
        }
    }
}
