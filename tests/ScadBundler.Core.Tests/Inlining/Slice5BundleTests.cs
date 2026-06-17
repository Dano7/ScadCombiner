using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The Slice-5 decision-proving bundle cases (Test-Corpus B-001..B-007) plus dedup, collision
/// strategies, and font pass-through. Assertions are on the bundled AST (presence/absence/rewrite),
/// which is binding now; exact emitted text becomes a golden once Slice 6 locks formatting.
/// </summary>
public sealed class Slice5BundleTests
{
    [Fact]
    public void B001_Include_BringsInEverything_AndExecutesTopLevelGeometry()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);\ncube(99);"));

        Assert.DoesNotContain(bundled.Statements, s => s is IncludeStatement or UseStatement);
        Assert.Contains(bundled.Statements, s => s is AssignmentStatement { Name: "WALL" });
        Assert.Contains(bundled.Statements, s => s is ModuleDefinition { Name: "box" });
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "cube", Arguments: [{ Value: NumberLiteral { Value: 99 } }] });
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "box" });
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void B002_Use_ImportsDefinitionsOnly_PreservesReferencedPrivateConstants()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "$fn = 64;\nWALL = 2;\nUNUSED = 5;\nmodule box() cube(WALL);\ncube(99);"));

        // `use`-imports are namespaced by construction (ADR 0001), so WALL/box become lib__WALL/lib__box.
        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("lib__WALL", names);        // referenced by box → carried as private constant
        Assert.Contains("lib__box", names);
        Assert.DoesNotContain("$fn", names);        // top-level special var dropped
        Assert.DoesNotContain("UNUSED", names);     // unreferenced var dropped
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "lib__box" });
        Assert.DoesNotContain(bundled.Statements, s => s is UseStatement);
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is NumberLiteral { Value: 99 }); // cube(99) dropped
        Assert.Empty(diagnostics); // a non-clashing use-import is namespaced silently
    }

    [Fact]
    public void Use_DefinitionReadingItsIncludesConstant_CarriesTheConstant()
    {
        // The modulecache-tests `includefrommodule` shape, caught by the OpenSCAD differential
        // harness: the used file's module reads a constant that arrives via the used file's own
        // `include` (ScopeContext.cc include-merge). The import must carry the constant — without
        // it the bundle silently renders with `undef`.
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nwidget();"),
            ("lib.scad", "include <consts.scad>\nmodule widget() cube(SIZE);"),
            ("consts.scad", "SIZE = 5;\nIGNORED = 7;"));

        // The body's (renamed) reference must agree with an emitted top-level assignment of 5…
        ModuleDefinition widget = SemanticHelper.Find<ModuleDefinition>(
            bundled, m => m.Name.EndsWith("widget", StringComparison.Ordinal));
        Identifier reference = SemanticHelper.Find<Identifier>(widget);
        Assert.Contains(bundled.Statements, s =>
            s is AssignmentStatement { Value: NumberLiteral { Value: 5 } } a && a.Name == reference.Name);

        // …while the closure's unreferenced constant stays out, under any name.
        Assert.DoesNotContain(
            BundleHelper.TopLevelDeclarationNames(bundled),
            n => n.EndsWith("IGNORED", StringComparison.Ordinal));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UsedBuiltinWrapper_OverriddenByIncludedModule_FreesBuiltinName_NoRecursion()
    {
        // BOSL2's pattern: builtins.scad wraps the OpenSCAD builtin `translate`; transforms.scad `use`s
        // it and OVERRIDES `translate`, delegating to the wrapper to reach the original. OpenSCAD keeps
        // the wrapper isolated (its `translate` = builtin); flattened naively, the wrapper's bare
        // `translate` would bind to the override → infinite recursion (the real failure seen rendering
        // ParametricCompoundPlanetary). The inliner must free the builtin name by renaming the
        // include-origin override and its references, leaving the wrapper's builtin call untouched.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <transforms.scad>\ntranslate([1, 0, 0]) cube(1);"),
            ("transforms.scad", "use <builtins.scad>\nmodule translate(v) { _translate(v) children(); }"),
            ("builtins.scad", "module _translate(v) translate(v) children();"));

        // The override no longer occupies the bare builtin name, so the wrapper's `translate` is the builtin.
        Assert.DoesNotContain(bundled.Statements, s => s is ModuleDefinition { Name: "translate" });

        // The wrapper still calls the bare (unrenamed) builtin `translate`...
        ModuleDefinition wrapper = SemanticHelper.Find<ModuleDefinition>(
            bundled, m => m.Body is ModuleInstantiation { Name: "translate" });
        Assert.NotEqual("translate", wrapper.Name);

        // ...while the override delegates to the (renamed) wrapper, never to the bare name or itself.
        ModuleDefinition over = bundled.Statements.OfType<ModuleDefinition>()
            .First(m => m != wrapper && m.Name.EndsWith("translate", StringComparison.Ordinal));
        Assert.Contains(SemanticHelper.Descendants(over), n => n is ModuleInstantiation mi && mi.Name == wrapper.Name);
        Assert.DoesNotContain(SemanticHelper.Descendants(over), n => n is ModuleInstantiation { Name: "translate" });

        // The user's own call resolves to the override (renamed), not the freed builtin name.
        Assert.DoesNotContain(bundled.Statements, s => s is ModuleInstantiation { Name: "translate" });
    }

    [Fact]
    public void B003_Assign_NormalizedToLet()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(a = 1, b = 2) translate([a, b, 0]) cube(1);"));

        var let = Assert.IsType<LetStatement>(bundled.Statements[0]);
        Assert.Collection(
            let.Bindings,
            b => Assert.Equal("a", b.Name),
            b => Assert.Equal("b", b.Name));
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.AssignNormalized, diagnostic.Code);
        Assert.Equal(1, diagnostic.Span.Start.Line);
        Assert.Equal(1, diagnostic.Span.Start.Column);
    }

    [Fact]
    public void B004_Child_NormalizedToChildren()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "module wrapper() {\n    child();\n    child(1);\n}"));

        List<ModuleInstantiation> children =
        [
            .. SemanticHelper.Descendants(bundled).OfType<ModuleInstantiation>().Where(m => m.Name == "children"),
        ];
        Assert.Equal(2, children.Count);
        Assert.Contains(children, m => m.Arguments is [{ Value: NumberLiteral { Value: 0 } }]); // child() → children(0)
        Assert.Contains(children, m => m.Arguments is [{ Value: NumberLiteral { Value: 1 } }]); // child(1) → children(1)
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "child" });

        Assert.Equal(
            [DiagnosticCode.ChildNormalized, DiagnosticCode.ChildNormalized],
            diagnostics.Select(d => d.Code));
        Assert.Equal((2, 5), (diagnostics[0].Span.Start.Line, diagnostics[0].Span.Start.Column));
        Assert.Equal((3, 5), (diagnostics[1].Span.Start.Line, diagnostics[1].Span.Start.Column));
    }

    [Fact]
    public void B005_DeprecatedBuiltin_PreservedVerbatim()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "import_stl(\"part.stl\");"));

        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "import_stl" });
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DeprecatedBuiltinPreserved, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void B006_UseCollision_NamespacesBothLibraries_CallBindsToLastUse()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <gear_a.scad>\nuse <gear_b.scad>\ngear();"),
            ("gear_a.scad", "module gear() cube(1);"),
            ("gear_b.scad", "module gear() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("gear_a__gear", names);
        Assert.Contains("gear_b__gear", names);
        Assert.DoesNotContain("gear", names);

        var gearA = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "gear_a__gear");
        var gearB = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "gear_b__gear");
        Assert.True(gearA.Body is ModuleInstantiation { Name: "cube" });
        Assert.True(gearB.Body is ModuleInstantiation { Name: "sphere" });

        // The call binds to the last-used library (gear_b), and is rewritten to its namespaced name.
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "gear_b__gear" });
        Assert.DoesNotContain(bundled.Statements, s => s is ModuleInstantiation { Name: "gear" });

        Assert.Equal(2, diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed));
    }

    [Fact]
    public void B007_IncludeDuplicate_LastWins()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "sphere" }); // b (the later include) wins
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "part" });

        Diagnostic redefinition = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DefinitionRedefined, redefinition.Code);
        Assert.Equal("b.scad", redefinition.Span.File.Path); // points at the redefinition (b)
    }

    [Fact]
    public void Diamond_StructurallyIdenticalDefinitions_Deduplicated()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\nshared();"),
            ("a.scad", "include <common.scad>"),
            ("b.scad", "include <common.scad>"),
            ("common.scad", "module shared() cube(1);"));

        Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "shared");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DuplicateMerged);
    }

    [Fact]
    public void Diamond_TopLevelGeometry_IsPreservedTwice()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>"),
            ("a.scad", "include <common.scad>"),
            ("b.scad", "include <common.scad>"),
            ("common.scad", "cube(7);"));

        // Duplicated geometry renders twice in OpenSCAD — preserving both is semantic equivalence.
        Assert.Equal(2, bundled.Statements.OfType<ModuleInstantiation>().Count(m => m.Name == "cube"));
    }

    [Fact]
    public void PrefixStrategy_IncludeCollision_KeepsBothNamespaced()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Prefix),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("a__part", names);
        Assert.Contains("b__part", names);
        Assert.DoesNotContain("part", names);
        Assert.Equal(2, diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed));

        // The call binds to the later include (LocalScope.cc last-wins), so it rewrites to b's namespace.
        var call = Assert.Single(bundled.Statements.OfType<ModuleInstantiation>());
        Assert.Equal("b__part", call.Name);
    }

    [Fact]
    public void KeepFirstStrategy_IncludeCollision_DropsLater()
    {
        var (bundled, _) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.KeepFirst),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "cube" }); // a (the first) is kept
    }

    [Fact]
    public void PrefixStrategy_CrossIncludeInternalReference_BindsToLastWins()
    {
        // Regression for the cross-`include` mis-bind (Post-v1-Plan #4): a.scad defines `part` and an
        // internal call to it; b.scad redefines `part`. The pre-inline model resolves a.scad's call
        // against a.scad's own scope (→ a.part), but the flat bundle binds the name last-wins (→ b.part).
        // Under `prefix` both definitions survive namespaced, so every reference — including a.scad's
        // internal one — must be rewritten to the last-wins copy, not the per-file copy.
        var (bundled, _) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Prefix),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);\nmodule usesA() part();"),
            ("b.scad", "module part() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("a__part", names); // a's copy survives (now dead code, as in OpenSCAD)
        Assert.Contains("b__part", names);

        var usesA = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "usesA");
        Assert.Equal("b__part", ((ModuleInstantiation)usesA.Body).Name); // ← was a__part (the mis-bind)

        var topLevelCall = Assert.Single(bundled.Statements.OfType<ModuleInstantiation>());
        Assert.Equal("b__part", topLevelCall.Name);
    }

    [Fact]
    public void KeepFirstStrategy_CrossIncludeInternalReference_BindsToKept()
    {
        // Guard the `keep-first` half of Post-v1-Plan #4: keeping the first definition and dropping the
        // rest leaves one surviving `part` with its original name, so both calls (the top-level one the
        // model bound to b, and a.scad's internal one) re-bind by name to the kept (first) definition.
        var (bundled, _) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.KeepFirst),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);\nmodule usesA() part();"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "cube" }); // a (the first) is kept

        var usesA = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "usesA");
        Assert.Equal("part", ((ModuleInstantiation)usesA.Body).Name);
    }

    [Fact]
    public void KeepLastStrategy_IncludeCollision_KeepsLast_WithSB3004()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.KeepLast),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "sphere" }); // b (the last) is kept
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
    }

    [Fact]
    public void Assign_WithPositionalArgument_IsNotNormalized()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(1) cube(1);")); // positional arg → not a valid let; left as-is

        Assert.Contains(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AssignNormalized);
    }

    [Fact]
    public void Assign_WithoutChild_IsNotNormalized()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(a = 1);")); // no child → not rewritten

        Assert.Contains(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AssignNormalized);
    }

    [Fact]
    public void ErrorStrategy_Collision_ProducesNoOutput_WithErrorDiagnostic()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Error),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        Assert.Empty(bundled.Statements);

        // A real collision under `error` must be an Error-severity diagnostic (so the CLI exits 1),
        // not the keep-last warning the other strategies emit.
        Diagnostic collision = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.CollisionError);
        Assert.Equal(DiagnosticSeverity.Error, collision.Severity);
        Assert.Contains("part", collision.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
    }

    [Fact]
    public void ErrorStrategy_NoCollision_BundlesNormally()
    {
        // The strategy only fires on a genuine collision; a clean project still produces output.
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Error),
            ("main.scad", "include <a.scad>\npart();"),
            ("a.scad", "module part() cube(1);"));

        Assert.NotEmpty(bundled.Statements);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.CollisionError);
    }

    [Fact]
    public void FontUse_IsKeptVerbatimInOutput()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <Arial.ttf>\ntext(\"hi\");"));

        Assert.Contains(bundled.Statements, s => s is UseStatement { RawPath: "Arial.ttf" });
    }

    [Fact]
    public void UseImport_NoCollision_IsNamespacedForIsolation()
    {
        // ADR 0001: OpenSCAD evaluates a `use`d library in its own FileContext, so every imported symbol
        // is namespaced by construction — even with no clash — and the call site is rewritten to match.
        // The rename is silent (SB5004 would otherwise fire for every library symbol).
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("lib__box", names);
        Assert.DoesNotContain("box", names);
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "lib__box" });
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NameRenamed);
    }

    [Fact]
    public void CustomizerParameters_HoistedAboveIncludedLibrary_AndFencedWithHidden()
    {
        // The included library (with its own global) is spliced above the root's params in document
        // order; the inliner must hoist the root's parameter prologue back to the top so OpenSCAD's
        // Customizer still sees it, and fence the library globals out with a synthesized /* [Hidden] */.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nwidth = 10;\nheight = 20;\npart();"),
            ("lib.scad", "LIBCONST = 5;\nmodule part() cube(LIBCONST);"));

        // The root parameters lead the bundle, in their original order.
        Assert.Collection(
            bundled.Statements.OfType<AssignmentStatement>().Take(2),
            a => Assert.Equal("width", a.Name),
            a => Assert.Equal("height", a.Name));

        // …ahead of the library's own global.
        List<string> names = [.. BundleHelper.TopLevelDeclarationNames(bundled)];
        Assert.True(names.IndexOf("width") < names.IndexOf("LIBCONST"));
        Assert.True(names.IndexOf("height") < names.IndexOf("LIBCONST"));

        // A synthesized Hidden boundary fences whatever follows the parameters.
        Assert.Contains(
            bundled.Statements,
            s => s.LeadingTrivia.Any(t => t is CommentTrivia { Text: "/* [Hidden] */" }));
    }

    [Fact]
    public void EmptyPrologue_LibraryGlobals_AreStillFencedFromCustomizer()
    {
        // The root declares no parameters of its own, so the library globals must be fenced at the top
        // (the original root would show zero Customizer parameters).
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\npart();"),
            ("lib.scad", "LIBCONST = 5;\nmodule part() cube(LIBCONST);"));

        Statement first = bundled.Statements[0];
        Assert.True(first is AssignmentStatement { Name: "LIBCONST" });
        Assert.Contains(first.LeadingTrivia, t => t is CommentTrivia { Text: "/* [Hidden] */" });
    }

    [Fact]
    public void RootParameters_PreserveAuthorCustomizerComments()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\n/* [Sizes] */\nwidth = 10;\n/* [Hidden] */\nsecret = 1;\npart();"),
            ("lib.scad", "module part() cube(1);"));

        var width = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "width");
        Assert.Contains(width.LeadingTrivia, t => t is CommentTrivia { Text: "/* [Sizes] */" });

        var secret = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "secret");
        Assert.Contains(secret.LeadingTrivia, t => t is CommentTrivia { Text: "/* [Hidden] */" });
    }

    [Fact]
    public void RootParameters_MarkCustomizerCommentsSticky_SoTheySurviveHardening()
    {
        // The comments OpenSCAD's Customizer reads off each hoisted parameter — its /* [group] */ header,
        // the description line directly above, and the trailing // [..] annotation — are marked sticky so
        // they survive a comment-stripping emit (--minify/--obfuscate/--no-preserve-comments) and the
        // bundle keeps the same grouping and labels. An ordinary header line above the parameter is not,
        // so the long file headers still drop. (Licenses off here so the header stays on the parameter.)
        var (bundled, _) = BundleHelper.Bundle(
            BundleOptions.Default with { BundleLicenses = false },
            ("main.scad",
                "// long library header line\n"
                + "/* [Sizes] */\n"
                + "// Outer width of the box\n"
                + "width = 10; // [1:100]\n"
                + "cube(width);"));

        var width = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "width");
        static bool Sticky(IEnumerable<Trivia> trivia, string text) =>
            trivia.OfType<CommentTrivia>().Single(t => t.Text == text).Sticky;

        Assert.True(Sticky(width.LeadingTrivia, "/* [Sizes] */"));               // group header
        Assert.True(Sticky(width.LeadingTrivia, "// Outer width of the box"));    // description (line above)
        Assert.True(Sticky(width.TrailingTrivia, "// [1:100]"));                  // inline annotation
        Assert.False(Sticky(width.LeadingTrivia, "// long library header line")); // ordinary header — drops
    }

    [Fact]
    public void CustomizerStickiness_IsPrecise_MatchesWhatOpenScadActuallyReads()
    {
        // Only what OpenSCAD's CommentParser/ParameterObject actually consume is kept; everything else
        // drops under hardening (the "surgical" guarantee):
        //  - a `//` description must be on the line *directly* above (getDescription reads firstLine-1);
        //  - a trailing comment is kept only in a widget-constraining form (`[..]` or a bare number) —
        //    a prose annotation parses but has no widget effect, so it is not carried;
        //  - a group header must be a bracketed `/* [..] */` marker, not any single-line block.
        var (bundled, _) = BundleHelper.Bundle(
            BundleOptions.Default with { BundleLicenses = false },
            ("main.scad",
                "/* [Sizes] */\n"
                + "// not adjacent\n"
                + "\n"
                + "width = 10; // millimeters\n"
                + "/* ordinary block */\n"
                + "height = 5; // 100\n"
                + "cube([width, height, 1]);"));

        var width = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "width");
        var height = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "height");
        static bool Sticky(IEnumerable<Trivia> trivia, string text) =>
            trivia.OfType<CommentTrivia>().Single(t => t.Text == text).Sticky;

        Assert.True(Sticky(width.LeadingTrivia, "/* [Sizes] */"));          // bracketed group marker
        Assert.False(Sticky(width.LeadingTrivia, "// not adjacent"));        // blank line ⇒ not the description
        Assert.False(Sticky(width.TrailingTrivia, "// millimeters"));        // prose ⇒ no widget effect, drops
        Assert.False(Sticky(height.LeadingTrivia, "/* ordinary block */"));  // bracketless block ⇒ not a marker
        Assert.True(Sticky(height.TrailingTrivia, "// 100"));               // bare-number annotation kept
    }

    [Fact]
    public void ComputedRootAssignment_IsNotHoisted_StaysAfterItsInputs()
    {
        // Regression (ForkedHolder): `spacing = lib_unit;` is not a Customizer parameter — OpenSCAD
        // only collects literal assignments (Expression::isLiteral) — and hoisting it above the
        // included library that assigns `lib_unit` made OpenSCAD read it as undef (top-level
        // assignments evaluate in document order). It must stay at its document position.
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nwidth = 10;\nspacing = lib_unit * 2;\npart();"),
            ("lib.scad", "lib_unit = 42;\nmodule part() cube(lib_unit);"));

        List<string> names = [.. BundleHelper.TopLevelDeclarationNames(bundled)];
        Assert.True(names.IndexOf("width") < names.IndexOf("lib_unit"));   // literal: hoisted
        Assert.True(names.IndexOf("spacing") > names.IndexOf("lib_unit")); // computed: document order
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ForwardReference);
    }

    [Fact]
    public void Prologue_HoistsExactlyOpenScadLiteralForms()
    {
        // Mirrors OpenSCAD Expression::isLiteral(), the Customizer's parameter gate: negatives,
        // strings, booleans, all-literal vectors and ranges qualify; identifier reads, arithmetic,
        // calls, and vectors with computed elements do not.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad",
                "include <lib.scad>\n"
                + "a = -5;\nb = [1, 2];\nc = [0 : 0.5 : 10];\nd = \"text\";\ne = true;\n"
                + "f = LIB + 1;\ng = max(1, 2);\nh = [LIB, 1];\n"
                + "part();"),
            ("lib.scad", "LIB = 1;\nmodule part() cube(1);"));

        List<string> names = [.. BundleHelper.TopLevelDeclarationNames(bundled)];
        int lib = names.IndexOf("LIB");
        foreach (string hoisted in (string[])["a", "b", "c", "d", "e"])
        {
            Assert.True(names.IndexOf(hoisted) < lib, $"'{hoisted}' should be hoisted above the library");
        }

        foreach (string computed in (string[])["f", "g", "h"])
        {
            Assert.True(names.IndexOf(computed) > lib, $"'{computed}' should stay in document order");
        }
    }

    [Fact]
    public void ForwardReference_InAssembledBundle_WarnsSB5008()
    {
        // The library reads `size`, which the root assigns with a computed (non-hoistable)
        // expression — so the bundle reads it before its assignment. OpenSCAD warns the same way
        // on the original include order; the bundle is faithful but flagged.
        var (_, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nsize = base();\nfunction base() = 4;\npart();"),
            ("lib.scad", "r = size * 2;\nmodule part() cube(r);"));

        Diagnostic warning = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ForwardReference);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'size'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludeVariableReassignment_EmitsWinningExpressionAtFirstPosition()
    {
        // OpenSCAD's parser overwrites a reassigned variable's expression *in place* (parser.y
        // handle_assignment → Assignment::setExpr), so the last expression evaluates at the FIRST
        // assignment's document position: lib's `y = x * 2;` reads the final x = 5 → y = 10. The
        // root's `x = 5;` sits after a definition so the Customizer-prologue hoist leaves it in
        // document order; emitting the winner at its own (last) slot would order `y = x * 2;`
        // before any assignment of x and evaluate y as undef (the shape SB5008 flags).
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nmodule m() cube(x);\nx = 5;\nm();"),
            ("lib.scad", "x = 1;\ny = x * 2;"));

        List<AssignmentStatement> assignments = [.. bundled.Statements.OfType<AssignmentStatement>()];
        Assert.Collection(
            assignments,
            x =>
            {
                Assert.Equal("x", x.Name);
                Assert.True(x.Value is NumberLiteral { Value: 5 }); // the last expression…
                Assert.Equal("lib.scad", x.Span.File.Path);         // …at the first occurrence's slot
            },
            y => Assert.Equal("y", y.Name));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.VariableReassigned);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ForwardReference);
    }

    [Fact]
    public void ForwardFunctionCall_DoesNotWarnSB5008()
    {
        // Definitions are scope-wide in OpenSCAD: an assignment may call a function defined later
        // in the file. Only variable reads are order-sensitive.
        var (_, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "a = 1;\nb = later(a);\nfunction later(x) = x + 1;\ncube(b);"));

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ForwardReference);
    }

    [Fact]
    public void LetBoundName_ShadowingLaterAssignment_DoesNotWarnSB5008()
    {
        // `k` inside the let body is the binding, not the later-assigned top-level `k`; `$fn` and
        // `PI` resolve to the special-variable/built-in scope. None is a forward read.
        var (_, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "a = let (k = 2) k + PI + $fn;\nk = base();\nfunction base() = 7;\ncube(a);"));

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ForwardReference);
    }

    [Fact]
    public void UsedLibrary_InternalReference_FollowsRenamedPrivateConstant()
    {
        // Root and the used library both define WALL; the library's private constant is namespaced and
        // box keeps seeing its own value (V2 isolation), while root's WALL is untouched.
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nWALL = 99;\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);"));

        Assert.Contains("WALL", BundleHelper.TopLevelDeclarationNames(bundled));     // root's WALL survives
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NameRenamed);     // library WALL namespaced

        var box = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "lib__box");
        var cubeArg = Assert.IsType<Identifier>(((ModuleInstantiation)box.Body).Arguments[0].Value);
        Assert.StartsWith("lib__WALL", cubeArg.Name); // box's reference rewritten to the namespaced constant

        var rootWall = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "WALL");
        Assert.True(rootWall.Value is NumberLiteral { Value: 99 }); // root's WALL is the unmodified 99
    }

    [Fact]
    public void TwoUsedLibraries_PrivateHelpers_StayIsolated()
    {
        // The isolation case a naive concatenator breaks: two `use`d libraries each define a private
        // `helper()` they call internally. Always-namespacing keeps each library's call bound to its own
        // helper (a__foo→a__helper, b__bar→b__helper), exactly as OpenSCAD's per-file FileContext would.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <a.scad>\nuse <b.scad>\nfoo();\nbar();"),
            ("a.scad", "module helper() cube(1);\nmodule foo() helper();"),
            ("b.scad", "module helper() sphere(1);\nmodule bar() helper();"));

        var foo = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "a__foo");
        Assert.Equal("a__helper", ((ModuleInstantiation)foo.Body).Name);

        var bar = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "b__bar");
        Assert.Equal("b__helper", ((ModuleInstantiation)bar.Body).Name);

        // The root's own calls rebind to each library's namespaced entry point.
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "a__foo" });
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "b__bar" });
    }

    [Fact]
    public void OwnDefinition_ShadowsUsedLibrary_OfSameName()
    {
        // OpenSCAD checks own scope before used libraries (ScopeContext.cc), so the root's own `widget`
        // wins. The used library's `widget` is namespaced out of the way; the call keeps binding to root.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "module widget() cube(9);\nuse <lib.scad>\nwidget();"),
            ("lib.scad", "module widget() sphere(1);"));

        var rootWidget = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "widget");
        Assert.True(rootWidget.Body is ModuleInstantiation { Name: "cube" }); // root's definition survives unrenamed
        Assert.Contains("lib__widget", BundleHelper.TopLevelDeclarationNames(bundled));

        var call = Assert.Single(bundled.Statements.OfType<ModuleInstantiation>(), m => m.Name is "widget" or "lib__widget");
        Assert.Equal("widget", call.Name); // bound to the root's own definition
    }
}
