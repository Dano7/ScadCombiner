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
/// constructs, and assemble. Never throws — every problem is a diagnostic.
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

            // Phase A — inline includes (document order); hoist use/font edges out of the flat list.
            (List<LoadedFile> usedFiles, List<UseStatement> fontUses) = DiscoverUses(root);
            List<Statement> rootFlat = FlattenIncludes(root, []);

            // Phase B — import used definitions + their private constants.
            List<Candidate> useItems = GatherUseImports(usedFiles);

            // Phases C/D — collisions + dedup over the merged definition/variable set.
            List<Candidate> rootDefs = RootDefinitions(rootFlat);
            ResolveCollisions(useItems, rootDefs);

            // Phases E/F — assemble + single rewrite pass (renames + normalization).
            var rewriter = new BundleRewriter(_renames, _diagnostics);
            ScadFile bundled = Assemble(root, fontUses, useItems, rootFlat, rewriter);

            IReadOnlyList<Diagnostic> sorted = Sort(_diagnostics.ToList());
            return (bundled, sorted);
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

        private static List<Candidate> RootDefinitions(List<Statement> rootFlat)
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
                        result.Add(new Candidate { Node = assignment, Kind = DefKind.Variable, Name = assignment.Name, FromUse = false });
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
                    _winners.Add(rep.Node);
                }

                return;
            }

            switch (_options.OnCollision)
            {
                case CollisionStrategy.Prefix:
                    foreach (Candidate rep in reps)
                    {
                        NamespaceRep(rep);
                    }

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

        private void NamespaceRep(Candidate rep)
        {
            string file = rep.Node.Span.File.Path;
            string stem = Sanitize(Path.GetFileNameWithoutExtension(file));
            string newName = UniqueName($"{stem}__{rep.Name}");
            _winners.Add(rep.Node);
            _renames[rep.Node] = newName;

            foreach (AstNode reference in _model.ReferencesTo(SymbolFor(rep)))
            {
                _renames[reference] = newName;
            }

            _diagnostics.Warning(
                DiagnosticCode.NameRenamed,
                $"'{rep.Name}' from '{file}' renamed to '{newName}' to resolve a collision.",
                rep.Node.Span);
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
            BundleRewriter rewriter)
        {
            if (_errorCollision)
            {
                return new ScadFile(root.Source, []); // `Error` strategy: a collision means no output
            }

            var statements = new List<Statement>();
            var emitted = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);

            foreach (UseStatement font in fontUses)
            {
                statements.Add(font); // a binary font cannot be inlined — preserved verbatim
            }

            foreach (Candidate item in useItems)
            {
                if (_winners.Contains(item.Node) && emitted.Add(item.Node))
                {
                    statements.Add(rewriter.RewriteStatement(item.Node));
                }
            }

            foreach (Statement statement in rootFlat)
            {
                if (statement is ModuleDefinition or FunctionDefinition or AssignmentStatement)
                {
                    if (!_winners.Contains(statement) || !emitted.Add(statement))
                    {
                        continue; // dropped by collision resolution, or an already-emitted diamond copy
                    }
                }

                statements.Add(rewriter.RewriteStatement(statement));
            }

            return new ScadFile(root.Source, statements);
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
