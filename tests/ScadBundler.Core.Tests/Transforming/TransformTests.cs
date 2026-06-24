using System.Text.RegularExpressions;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Transforming;
using Xunit;

namespace ScadBundler.Core.Tests.Transforming;

/// <summary>
/// Tests for the Slice-7 hardening stage (minify/obfuscate) driven through the inline → transform
/// pipeline, plus the per-transform behaviors and the determinism/avalanche/Customizer invariants.
/// </summary>
public sealed class TransformTests
{
    // ---------------------------------------------------------------------------------------------
    // Determinism & avalanche
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void Profile_ProducesDeterministicOutput(HardeningProfile profile)
    {
        (string Name, string Source) main = ("main.scad", "w = 2;\nmodule ring(d) { circle(d); }\nring(w);\n");
        string a = EmitDefault(Harden(profile, main).Bundle);
        string b = EmitDefault(Harden(profile, main).Bundle);
        Assert.Equal(a, b);
    }

    [Fact]
    public void OneCharSourceChange_AvalanchesGeneratedNames()
    {
        string emittedA = EmitDefault(Harden(HardeningProfile.Obfuscate,
            ("main.scad", "w = 2;\nfunction f(x) = x - w;\nmodule m(d) { circle(f(d)); }\nm(10);\n")).Bundle);
        string emittedB = EmitDefault(Harden(HardeningProfile.Obfuscate,
            ("main.scad", "w = 3;\nfunction f(x) = x - w;\nmodule m(d) { circle(f(d)); }\nm(10);\n")).Bundle);

        string[] namesA = GeneratedNames(emittedA);
        string[] namesB = GeneratedNames(emittedB);
        Assert.NotEmpty(namesA);
        int shared = namesA.Intersect(namesB).Count();
        Assert.True(shared <= 1, $"expected avalanche, {shared} generated names shared");
    }

    // ---------------------------------------------------------------------------------------------
    // Customizer parameter aliasing (the headline)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void CustomizerParameter_KeptVerbatimAtTop_ConsumedIntoAliasBelow(HardeningProfile profile)
    {
        ScadFile bundle = Harden(profile,
            ("main.scad", "diameter = 20;\nmodule box(d) { cube(d); }\nbox(diameter);\n")).Bundle;
        string text = EmitDefault(bundle);

        // The first top-level statement is the verbatim parameter (name preserved for the Customizer).
        AssignmentStatement first = Assert.IsType<AssignmentStatement>(bundle.Statements[0]);
        Assert.Equal("diameter", first.Name);
        Assert.IsType<NumberLiteral>(first.Value);

        // The name occurs exactly twice: the parameter declaration and its single alias read.
        Assert.Equal(2, Regex.Count(text, @"\bdiameter\b"));
        // The body call no longer uses `diameter` directly — it uses the alias.
        Assert.DoesNotContain("box(diameter)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cube(diameter)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomizerParameter_Unreferenced_GetsNoAlias_StaysVerbatim()
    {
        ScadFile bundle = Harden(HardeningProfile.Minify,
            ("main.scad", "label = \"hi\";\ncube(1);\n")).Bundle;
        AssignmentStatement first = Assert.IsType<AssignmentStatement>(bundle.Statements[0]);
        Assert.Equal("label", first.Name);
        Assert.Equal("\"hi\"", Assert.IsType<StringLiteral>(first.Value).RawText);
    }

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void CustomizerComments_SurviveHardening_KeepingGroupsLabelsAndAnnotations(HardeningProfile profile)
    {
        // Regression: a hardened bundle must keep the comments OpenSCAD's Customizer reads — group
        // headers, parameter descriptions, and inline annotations — so the bundled model's Customizer
        // still groups and labels its knobs (identical functionality to the unbundled file). The long
        // library header is not a Customizer comment and still drops (here via --strip-license).
        ScadFile bundle = Harden(Options(profile) with { StripLicense = true },
            ("main.scad",
                "// Long library header line A\n"
                + "// Long library header line B\n"
                + "/* [Box] */\n"
                + "// Outer width of the box\n"
                + "width = 20; // [10:50]\n"
                + "module box(w) { cube(w); }\n"
                + "box(width);\n")).Bundle;
        string text = Emitter.Emit(bundle, EmitFor(profile));

        Assert.Contains("/* [Box] */", text, StringComparison.Ordinal);               // group header
        Assert.Contains("// Outer width of the box", text, StringComparison.Ordinal);  // description
        Assert.Contains("// [10:50]", text, StringComparison.Ordinal);                 // inline annotation
        Assert.Equal("width", Assert.IsType<AssignmentStatement>(bundle.Statements[0]).Name); // name kept for the knob
        Assert.DoesNotContain("Long library header line", text, StringComparison.Ordinal);     // header still dropped
    }

    // ---------------------------------------------------------------------------------------------
    // Tree-shaking
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TreeShaking_RemovesUnreferencedDefinitions_ReportsSb5009()
    {
        var result = Harden(HardeningProfile.Minify,
            ("main.scad", "include <lib.scad>\nused(1);\n"),
            ("lib.scad", "module used(n) { cube(n); }\nmodule never_called(n) { sphere(n); }\nfunction dead() = 1;\n"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(result.Bundle);
        // Two dead definitions removed; the used module stays (renamed).
        Assert.Equal(1, names.Count(n => n.Length > 0)); // only the surviving (renamed) module
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.Hardened);
    }

    [Fact]
    public void TreeShaking_KeepsAssignmentWithEchoSideEffect()
    {
        ScadFile bundle = Harden(HardeningProfile.Minify,
            ("main.scad", "noise = echo(\"hi\");\ncube(1);\n")).Bundle;
        // The echo-bearing assignment is a root (side effect) — never tree-shaken.
        Assert.Contains(bundle.Statements, s => s is AssignmentStatement a && a.Value is EchoExpression);
    }

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void TreeShaking_KeepsSpecialVariableDefault_ReadOnlyThroughDynamicScope(HardeningProfile profile)
    {
        // A top-level `$special = …` default is read through DYNAMIC scope: a descendant module reads it
        // off the call stack, an edge the static reference model can't see (special-variable reads bind to
        // no symbol). Tree-shaking must keep it, or the dynamic read finds `undef` at render time.
        // Regression for BOSL2's `$tags_shown = "ALL"` / `$transform = IDENT` attachment globals vanishing
        // under --minify/--obfuscate and crashing the bundle (assertion on the now-undefined variable).
        ScadFile bundle = Harden(profile,
            ("main.scad", "include <lib.scad>\nshape();\n"),
            ("lib.scad",
                "$decorate = \"ALL\";\n"
                + "module shape() { deco(); }\n"
                + "module deco() { assert($decorate == \"ALL\"); cube(1); }\n")).Bundle;

        // The default survives (it would be dropped if liveness were judged only by static references).
        Assert.Contains(bundle.Statements, s => s is AssignmentStatement { Name: "$decorate" });
    }

    // ---------------------------------------------------------------------------------------------
    // Renaming integrity
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void Renaming_DoesNotIntroduceUnknownReferences(HardeningProfile profile)
    {
        (string, string) lib = ("lib.scad", "WALL = 2;\nfunction inset(x) = x - WALL;\nmodule ring(d) { circle(inset(d)); }\n");
        (string, string) main = ("main.scad", "include <lib.scad>\nd = 10;\nring(d);\n");

        int before = UnknownReferenceCount(BundleHelper.Bundle(BundleOptions.Default, main, lib).Bundled);
        int after = UnknownReferenceCount(Harden(Options(profile), main, lib).Bundle);
        Assert.Equal(before, after); // every renamed reference still resolves
    }

    [Fact]
    public void Renaming_PreservesBuiltinsAndSpecialVariables()
    {
        string text = EmitDefault(Harden(HardeningProfile.Obfuscate,
            ("main.scad", "module m(d) { cylinder(d, $fn = 64); }\nm(5);\n")).Bundle);
        Assert.Contains("cylinder(", text, StringComparison.Ordinal); // built-in never renamed
        Assert.Contains("$fn", text, StringComparison.Ordinal);       // special variable never renamed
    }

    // ---------------------------------------------------------------------------------------------
    // Minify-only: literal canonicalization
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LiteralCanonicalization_ShortensNumbersUnderMinify()
    {
        ScadFile bundle = Harden(HardeningProfile.Minify, ("main.scad", "translate([0.0, 0.5, 1.0]) cube(2.000);\n")).Bundle;
        string text = Emitter.Emit(bundle, new EmitOptions(Minify: true));
        Assert.Contains(".5", text, StringComparison.Ordinal);   // 0.5 -> .5
        Assert.Contains("cube(2)", text, StringComparison.Ordinal); // 2.000 -> 2
        Assert.DoesNotContain("1.0", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Obfuscate-only: string decomposition, indirection, dead-code injection
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Obfuscate_DecomposesAsciiStrings_ButNotPathArguments()
    {
        string text = EmitDefault(Harden(HardeningProfile.Obfuscate,
            ("main.scad", "echo(\"hi\");\nimport(\"part.stl\");\n")).Bundle);
        Assert.Contains("chr(", text, StringComparison.Ordinal);        // "hi" decomposed
        Assert.Contains("\"part.stl\"", text, StringComparison.Ordinal); // import path left literal
    }

    [Fact]
    public void Obfuscate_InjectsRenderInertDecoys()
    {
        var result = Harden(HardeningProfile.Obfuscate, ("main.scad", "module m(d) { cube(d + 1); }\nm(3);\n"));
        string text = EmitDefault(result.Bundle);
        Assert.Contains("*", text, StringComparison.Ordinal); // disable-modifier (*) decoy call — always injected
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.Hardened);
    }

    [Fact]
    public void IndirectionInjection_WrapsEligibleExpressions()
    {
        // Many eligible (binary/number) expressions, so the seed-driven density reliably wraps several.
        ScadFile bundle = ParseHelper.Parse(
            "v = [1+1, 2+2, 3+3, 4+4, 5+5, 6+6, 7+7, 8+8, 9+9, 10+10];\ncube(v[0]);\n").Root;
        var context = new TransformContext(HardeningProfile.Obfuscate, 1UL, new DiagnosticBag());

        ScadFile result = new IndirectionInjection().Apply(bundle, context);

        Assert.True(context.InjectedCount > 0);
        Assert.Contains("let(", Emitter.Emit(result, EmitOptions.Default), StringComparison.Ordinal);
    }

    [Fact]
    public void DeadCodeInjection_AppendsUncalledModulesAndDisabledCall()
    {
        ScadFile bundle = ParseHelper.Parse("cube(1);\n").Root;
        var context = new TransformContext(HardeningProfile.Obfuscate, 5UL, new DiagnosticBag());

        ScadFile result = new DeadCodeInjection().Apply(bundle, context);
        string text = Emitter.Emit(result, EmitOptions.Default);

        Assert.True(context.InjectedCount >= 3);
        Assert.Contains("module ", text, StringComparison.Ordinal); // uncalled decoy definitions
        Assert.Contains("*", text, StringComparison.Ordinal);       // disabled, render-inert call
    }

    // ---------------------------------------------------------------------------------------------
    // Round-trip & no-op
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HardeningProfile.Minify)]
    [InlineData(HardeningProfile.Obfuscate)]
    public void Transformed_RoundTripsStructurally(HardeningProfile profile)
    {
        ScadFile bundle = Harden(profile,
            ("main.scad", "w = 2;\nfunction f(x) = x * w;\nmodule m(d) { translate([f(d), 0, 0]) cube(d); }\nm(5);\n")).Bundle;
        Assert.True(Emitter.RoundTripsStructurally(bundle, EmitFor(profile)));
    }

    [Fact]
    public void NoneProfile_LeavesBundleUnchanged()
    {
        (string, string) main = ("main.scad", "x = 1;\ncube(x);\n");
        ScadFile inlined = BundleHelper.Bundle(BundleOptions.Default, main).Bundled;
        var bag = new DiagnosticBag();
        ScadFile output = Transformer.Run(inlined, HardeningProfile.None, bag);
        Assert.Same(inlined, output);
        Assert.Empty(bag.ToList());
    }

    [Fact]
    public void ParametersFirst_LicenseSurvivesMinify_EvenWhenItsHostStatementIsTreeShaken()
    {
        // ADR 0002: --parameters-first relocates the sticky license header onto the first body statement.
        // Here that statement (an unreferenced use-imported module) is tree-shaken by minify — the license
        // must not go with it. DeadCodeElimination carries sticky leading trivia forward to the next kept
        // statement, so the legal text still leads the body.
        BundleOptions options = BundleOptions.Default with { Hardening = HardeningProfile.Minify, ParametersFirst = true };
        ScadFile bundle = Harden(
            options,
            ("main.scad", "// (c) Author, MIT\nuse <lib.scad>\nwidth = 10;\nkeeper(width);"),
            ("lib.scad", "module dead() cube(1);\nmodule keeper(w) cube(w);")).Bundle;

        string minified = Emitter.Emit(bundle, EmitFor(HardeningProfile.Minify));

        Assert.Contains("// (c) Author, MIT", minified, StringComparison.Ordinal); // license survives the drop
        Assert.DoesNotContain("cube(1)", minified, StringComparison.Ordinal);       // the dead module was tree-shaken
    }

    [Fact]
    public void ParametersFirst_LicenseSurvivesMinify_EvenWhenTheWholeBodyIsTreeShaken()
    {
        // Edge of the carry-forward: the entire body (the header's host plus everything after it) is
        // tree-shaken, so no later statement can catch the rescued trivia. The license must still be
        // re-homed atop the surviving parameters — but the now-purposeless /* [Hidden] */ fence must NOT
        // be (placing it above the parameters would hide them from the Customizer).
        BundleOptions options = BundleOptions.Default with { Hardening = HardeningProfile.Minify, ParametersFirst = true };
        ScadFile bundle = Harden(
            options,
            ("main.scad", "// (c) Author, MIT\nwidth = 10;\nmodule helper() cube(1);\nDEAD = width;\nmodule unused() cube([width, DEAD, 1]);"))
            .Bundle;

        string minified = Emitter.Emit(bundle, EmitFor(HardeningProfile.Minify));

        Assert.Contains("// (c) Author, MIT", minified, StringComparison.Ordinal);     // attribution preserved
        Assert.DoesNotContain("/* [Hidden] */", minified, StringComparison.Ordinal);   // fence dropped, not hiding params
        AssignmentStatement first = Assert.IsType<AssignmentStatement>(bundle.Statements[0]);
        Assert.Equal("width", first.Name);                                             // the parameter still leads
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static BundleOptions Options(HardeningProfile profile) => BundleOptions.Default with { Hardening = profile };

    private static (ScadFile Bundle, IReadOnlyList<Diagnostic> Diagnostics) Harden(
        HardeningProfile profile, params (string Name, string Source)[] files) => Harden(Options(profile), files);

    private static (ScadFile Bundle, IReadOnlyList<Diagnostic> Diagnostics) Harden(
        BundleOptions options, params (string Name, string Source)[] files)
    {
        var (bundled, inlinerDiagnostics) = BundleHelper.Bundle(options, files);
        var bag = new DiagnosticBag();
        ScadFile output = Transformer.Run(bundled, options.Hardening, bag);
        return (output, [.. inlinerDiagnostics, .. bag.ToList()]);
    }

    private static string EmitDefault(ScadFile bundle) => Emitter.Emit(bundle, EmitOptions.Default);

    private static EmitOptions EmitFor(HardeningProfile profile) => new(
        Minify: profile == HardeningProfile.Minify,
        PreserveComments: profile == HardeningProfile.None);

    private static string[] GeneratedNames(string text) =>
        [.. Regex.Matches(text, @"_[a-z0-9]{6,}").Select(m => m.Value).Distinct()];

    private static int UnknownReferenceCount(ScadFile bundle) =>
        SemanticAnalyzer.Analyze(bundle).Diagnostics.Count(d => d.Code == DiagnosticCode.UnknownReference);
}
