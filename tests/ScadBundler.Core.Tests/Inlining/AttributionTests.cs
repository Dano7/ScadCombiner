using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The attribution pass behind the default-on <c>--bundle-licenses</c>: header/license aggregation at
/// the top of the bundle (encounter order, root first, deduplicated, moved not copied) and the
/// one-line provenance banners between inlined sections (SB5007).
/// </summary>
public sealed class AttributionTests
{
    private const string BlockOpen =
        "// ======== file headers & licenses aggregated by ScadBundler ========";

    private const string BlockClose =
        "// ====================================================================";

    [Fact]
    public void Headers_AggregatedAtTop_RootFirstUnframed_ThenLabeledBlock()
    {
        (ScadFile bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "// (c) Root Author, CC-BY-4.0\ninclude <lib.scad>\nbox();"),
            ("lib.scad", "// (c) Lib Author, MIT\nmodule box() cube(1);"));

        Assert.Equal(
            [
                "// (c) Root Author, CC-BY-4.0",
                BlockOpen,
                "// -------- include <lib.scad> --------",
                "// (c) Lib Author, MIT",
                BlockClose,
                "// ======== include <lib.scad> ========",
            ],
            LeadingComments(bundled.Statements[0]));

        // The library's header was moved, not copied: its definition no longer carries it, and the
        // section returning to the root is bannered with the root's file name.
        Assert.Equal(["// ======== main.scad ========"], LeadingComments(bundled.Statements[1]));
    }

    [Fact]
    public void NonRootHeaderAggregation_ReportsSb5007()
    {
        (ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics) result = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "// (c) Lib Author, MIT\nmodule box() cube(1);"));

        Assert.Contains(DiagnosticCode.LicensesAggregated, BundleHelper.Codes(result));
        Diagnostic info = result.Diagnostics.Single(d => d.Code == DiagnosticCode.LicensesAggregated);
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
    }

    [Fact]
    public void IdenticalHeaders_AcrossFiles_AppearOnce_ButAreStrippedEverywhere()
    {
        (ScadFile bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\ncube(1);"),
            ("a.scad", "// Shared License\nA = 1;"),
            ("b.scad", "// Shared License\nB = 2;"));

        int copies = bundled.Statements
            .SelectMany(LeadingComments)
            .Count(t => t == "// Shared License");
        Assert.Equal(1, copies);
    }

    [Fact]
    public void CustomizerGroupMarker_EndsTheHeaderRun_AndStaysWithItsParameter()
    {
        (ScadFile bundled, IReadOnlyList<Diagnostic> diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "// My Model License\n/* [Box] */\nwidth = 10;\ncube(width);"));

        // The license is hoisted above the group marker (a positional no-op for a single file), and
        // the marker keeps fencing its parameter — the Customizer UI is unchanged.
        Assert.Equal(["// My Model License", "/* [Box] */"], LeadingComments(bundled.Statements[0]));

        // A root-only header is not an aggregation: no SB5007.
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.LicensesAggregated);
    }

    [Fact]
    public void Banners_MarkEveryOriginChange_WithContinuedOnReentry()
    {
        (ScadFile bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ncube(1);\ninclude <b.scad>\nsphere(1);"),
            ("a.scad", "A = 1;"),
            ("b.scad", "B = 2;"));

        Assert.Equal(
            ["/* [Hidden] */", "// ======== include <a.scad> ========"],
            LeadingComments(bundled.Statements[0]));
        Assert.Equal(["// ======== main.scad ========"], LeadingComments(bundled.Statements[1]));
        Assert.Equal(["// ======== include <b.scad> ========"], LeadingComments(bundled.Statements[2]));
        Assert.Equal(["// ======== main.scad (continued) ========"], LeadingComments(bundled.Statements[3]));
    }

    [Fact]
    public void UseImport_BlockCommentHeaderCollected_LabeledAsUse_AndStrippedFromTheImportedDefinition()
    {
        (ScadFile bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "/* Lib License: MIT */\nmodule box() cube(1);"));

        // A block comment that is not a Customizer group marker is an ordinary header line.
        Assert.Equal(
            [
                BlockOpen,
                "// -------- use <lib.scad> --------",
                "/* Lib License: MIT */",
                BlockClose,
                "// ======== use <lib.scad> ========",
            ],
            LeadingComments(bundled.Statements[0]));
        Assert.Equal("lib__box", Assert.IsType<ModuleDefinition>(bundled.Statements[0]).Name);
    }

    [Fact]
    public void UnresolvedIncludesAndFontUses_AreSkippedByTheEncounterWalk()
    {
        (ScadFile bundled, IReadOnlyList<Diagnostic> diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <font.ttf>\ninclude <missing.scad>\ncube(1);"));

        Assert.IsType<UseStatement>(bundled.Statements[0]); // the font pass-through survives verbatim
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.LicensesAggregated);
    }

    [Fact]
    public void ErrorStrategyCollision_SuppressesSb5007_AndEmptiesTheBundle()
    {
        (ScadFile bundled, IReadOnlyList<Diagnostic> diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Error),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "// a.scad - MIT\nmodule part() cube(1);"),
            ("b.scad", "// b.scad - GPL\nmodule part() sphere(1);"));

        Assert.Empty(bundled.Statements);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.CollisionError);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.LicensesAggregated);
    }

    [Fact]
    public void CommentsOnlyIncludeFile_HeaderIsCollectedFromEofTrivia()
    {
        (ScadFile bundled, IReadOnlyList<Diagnostic> diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <NOTICE.scad>\ncube(1);"),
            ("NOTICE.scad", "// NOTICE: legal text\n"));

        Assert.Contains("// NOTICE: legal text", LeadingComments(bundled.Statements[0]));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.LicensesAggregated);
    }

    [Fact]
    public void BundleLicensesOff_NoAnnotations_HeadersLeftWhereTheyWere()
    {
        (ScadFile bundled, IReadOnlyList<Diagnostic> diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], BundleLicenses: false),
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "// Lib License: MIT\nmodule box() cube(1);"));

        Assert.Equal(["// Lib License: MIT"], LeadingComments(bundled.Statements[0]));
        Assert.Empty(LeadingComments(bundled.Statements[1]));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.LicensesAggregated);
        Assert.DoesNotContain(
            bundled.Statements.SelectMany(LeadingComments),
            t => t.Contains("========", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> LeadingComments(Statement statement) =>
        [.. statement.LeadingTrivia.OfType<CommentTrivia>().Select(t => t.Text)];
}
