using System.Linq;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Workspace;
using Xunit;

namespace ScadBundler.Core.Tests.Workspace;

/// <summary>
/// <see cref="ProjectAnalyzer"/>: entry-point inference, the dependency tree, missing/ambiguous reporting,
/// layout (basename) inference, the SB4001 filter, and never-throw behavior.
/// </summary>
public sealed class ProjectAnalyzerTests
{
    private static ProjectAnalysis Analyze(string? explicitRoot, params UploadedFile[] uploads) =>
        ProjectAnalyzer.Analyze(uploads, explicitRoot).Analysis;

    // ----- Entry-point inference -------------------------------------------------------------------

    [Fact]
    public void SingleFile_IsItsOwnRoot()
    {
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "cube(1);"));

        Assert.Equal("/proj/main.scad", a.InferredRoot);
        Assert.Equal("/proj/main.scad", a.Root);
        Assert.Equal(["/proj/main.scad"], a.EntryPointCandidates);
        Assert.NotNull(a.Tree);
        Assert.True(a.Tree!.Root.IsRoot);
        Assert.Equal(ReferenceOrigin.Root, a.Tree.Root.Origin);
    }

    [Fact]
    public void OneEntryTwoLibraries_InfersTheEntry()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("main.scad", "use <lib1.scad>\ninclude <lib2.scad>\nbox1();\nbox2();"),
            new UploadedFile("lib1.scad", "module box1() cube(1);"),
            new UploadedFile("lib2.scad", "module box2() cube(2);"));

        Assert.Equal("/proj/main.scad", a.InferredRoot);
        Assert.Equal(["/proj/main.scad"], a.EntryPointCandidates);
    }

    [Fact]
    public void TwoIndependentEntries_AreAmbiguous_NoInferredRoot()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("a.scad", "cube(1);"),
            new UploadedFile("b.scad", "sphere(1);"));

        Assert.Null(a.InferredRoot);
        Assert.Null(a.Root);
        Assert.Null(a.Tree);
        Assert.Equal(2, a.EntryPointCandidates.Count);
        Assert.Contains("/proj/a.scad", a.EntryPointCandidates);
        Assert.Contains("/proj/b.scad", a.EntryPointCandidates);
    }

    [Fact]
    public void GeometryTieBreak_CallerBearingFileWins()
    {
        // Two in-degree-0 files; only the model has top-level geometry → it is the inferred root and is
        // ordered first among the candidates.
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("zlib.scad", "module foo() cube(1);"), // name sorts after "model" — geometry must reorder it
            new UploadedFile("model.scad", "cube(1);"));

        Assert.Equal("/proj/model.scad", a.InferredRoot);
        Assert.Equal("/proj/model.scad", a.EntryPointCandidates[0]); // geometry-first
    }

    [Fact]
    public void TwoFileCycle_FallsBackToGeometryCandidates_NoInferredRoot()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("x.scad", "include <y.scad>\ncube(1);"),
            new UploadedFile("y.scad", "include <x.scad>\nsphere(1);"));

        Assert.Null(a.InferredRoot); // both bear geometry → ambiguous
        Assert.Equal(2, a.EntryPointCandidates.Count);
    }

    [Fact]
    public void AllCycleNoGeometry_FallsBackToAllFiles()
    {
        // A 2-file cycle of pure definitions (no top-level geometry) → no in-degree-0, no geometry-bearing
        // file, so every file becomes a candidate.
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("x.scad", "include <y.scad>\nmodule ax() cube(1);"),
            new UploadedFile("y.scad", "include <x.scad>\nmodule ay() cube(1);"));

        Assert.Null(a.InferredRoot);
        Assert.Equal(2, a.EntryPointCandidates.Count);
        Assert.Contains("/proj/x.scad", a.EntryPointCandidates);
        Assert.Contains("/proj/y.scad", a.EntryPointCandidates);
    }

    [Fact]
    public void RootNull_StillReportsMissingFromRawScan()
    {
        // Two independent entries → no inferred root; the missing reference is still surfaced.
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("a.scad", "use <missing.scad>\ncube(1);"),
            new UploadedFile("b.scad", "sphere(1);"));

        Assert.Null(a.Root);
        Assert.Null(a.Tree);
        MissingReference missing = Assert.Single(a.Missing);
        Assert.Equal("missing.scad", missing.RawPath);
        Assert.Equal(["/proj/a.scad"], missing.NeededBy);
    }

    [Fact]
    public void AbsoluteReference_ResolvesAgainstAnAbsolutelyPlacedFile()
    {
        // "../lib.scad" canonicalizes to /lib.scad; an absolute include then resolves to it.
        (InMemoryFileSystem fs, ProjectAnalysis a) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("main.scad", "include </lib.scad>\nbox();"),
            new UploadedFile("../lib.scad", "module box() cube(1);"),
        ]);

        Assert.Equal("/proj/main.scad", a.Root);
        Assert.Empty(a.Missing);
        Assert.True(WebBundler.Bundle(fs, a.Root!, new WebBundleOptions()).Ok);
    }

    [Fact]
    public void AbsoluteReference_UnsatisfiableByBasename_IsMissing()
    {
        // An absolute reference cannot be placed by basename inference even with a matching upload, so it
        // is reported missing rather than silently dropped.
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("main.scad", "include </weird/lib.scad>\nbox();"),
            new UploadedFile("lib.scad", "module box() cube(1);"));

        MissingReference missing = Assert.Single(a.Missing);
        Assert.Equal("/weird/lib.scad", missing.RawPath);
        Assert.Empty(a.Ambiguous);
    }

    [Fact]
    public void ExplicitRoot_OverridesInference()
    {
        ProjectAnalysis a = Analyze(
            "/proj/lib.scad",
            new UploadedFile("main.scad", "include <lib.scad>\ncube(1);"),
            new UploadedFile("lib.scad", "module box() cube(1);"));

        Assert.Equal("/proj/main.scad", a.InferredRoot); // inference still reported
        Assert.Equal("/proj/lib.scad", a.Root);          // but the override is used
        Assert.Equal("/proj/lib.scad", a.Tree!.Root.VirtualPath);
    }

    // ----- Dependency tree, missing references, fonts ----------------------------------------------

    [Fact]
    public void Diamond_IsLoadedOnce()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("main.scad", "include <x.scad>\ninclude <y.scad>\ncube(1);"),
            new UploadedFile("x.scad", "include <shared.scad>"),
            new UploadedFile("y.scad", "include <shared.scad>"),
            new UploadedFile("shared.scad", "module s() cube(1);"));

        Assert.Empty(a.Missing);
        // shared appears under both x and y in the tree…
        DependencyNode root = a.Tree!.Root;
        Assert.Equal(2, root.Children.Count);
        Assert.All(root.Children, child => Assert.Single(child.Children));
        // …but the bundle inlines it once.
        (InMemoryFileSystem fs, _) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("main.scad", "include <x.scad>\ninclude <y.scad>\ncube(1);"),
            new UploadedFile("x.scad", "include <shared.scad>"),
            new UploadedFile("y.scad", "include <shared.scad>"),
            new UploadedFile("shared.scad", "module s() cube(1);"),
        ]);
        WebBundleResult bundle = WebBundler.Bundle(fs, a.Root!, new WebBundleOptions());
        Assert.Equal(3, bundle.Stats.FilesInlined); // x, y, shared — not 4
    }

    [Fact]
    public void UnresolvedUse_IsAMissingReference_WithNeededBy()
    {
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "use <missing.scad>\ncube(1);"));

        MissingReference missing = Assert.Single(a.Missing);
        Assert.Equal("missing.scad", missing.RawPath);
        Assert.Equal(ReferenceOrigin.Use, missing.Origin);
        Assert.Equal(["/proj/main.scad"], missing.NeededBy);

        // …and it shows as an unresolved child in the tree.
        DependencyNode child = Assert.Single(a.Tree!.Root.Children);
        Assert.False(child.Resolved);
        Assert.Equal("missing.scad", child.VirtualPath);
        Assert.Equal(ReferenceOrigin.Use, child.Origin);
    }

    [Fact]
    public void FontUse_IsInformational_NeverMissing()
    {
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "use <Arial.ttf>\ntext(\"hi\");"));

        Assert.Empty(a.Missing);
        DependencyNode child = Assert.Single(a.Tree!.Root.Children);
        Assert.Equal(ReferenceOrigin.Font, child.Origin);
        Assert.True(child.Resolved);
    }

    [Fact]
    public void SB4001_IsFilteredFromDiagnostics()
    {
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "include <missing.scad>\ncube(1);"));

        Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Single(a.Missing); // surfaced here instead
    }

    [Fact]
    public void SemanticProblems_AreProjected_WithSpanFields()
    {
        // A comprehension generator outside a vector is SB3002 (a semantic, non-missing problem).
        // `[each … : 5]` parses cleanly to a RangeExpression whose Start is the generator.
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "v = [1,2,3];\nbad = [each v : 5];\ncube(1);"));

        DiagnosticDto problem = Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
        Assert.Equal("/proj/main.scad", problem.File); // the root's path is its canonical virtual path
        Assert.True(problem.Line >= 1);
        Assert.True(problem.Column >= 1);
    }

    // ----- Layout (basename) inference -------------------------------------------------------------

    [Fact]
    public void FlatDrop_SatisfiesSubPathReference_ByBasename()
    {
        (InMemoryFileSystem fs, ProjectAnalysis a) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("main.scad", "include <sub/lib.scad>\nbox();"),
            new UploadedFile("lib.scad", "module box() cube(1);"),
        ]);

        Assert.Empty(a.Missing);
        Assert.Empty(a.Ambiguous);
        Assert.True(fs.Contains("/proj/sub/lib.scad")); // alias placed where the loader will look
        WebBundleResult bundle = WebBundler.Bundle(fs, a.Root!, new WebBundleOptions());
        Assert.True(bundle.Ok);
        Assert.Contains("module box", bundle.Text);
    }

    [Fact]
    public void FolderUpload_ResolvesVerbatim_NoAmbiguity()
    {
        (InMemoryFileSystem fs, ProjectAnalysis a) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("main.scad", "include <sub/lib.scad>\nbox();"),
            new UploadedFile("sub/lib.scad", "module box() cube(1);"),
        ]);

        Assert.Empty(a.Missing);
        Assert.Empty(a.Ambiguous);
        Assert.True(WebBundler.Bundle(fs, a.Root!, new WebBundleOptions()).Ok);
    }

    [Fact]
    public void BasenameAmbiguity_ListsBothCandidates_NotSilentlyBound()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("main.scad", "include <lib.scad>\nbox();"),
            new UploadedFile("a/lib.scad", "module box() cube(1);"),
            new UploadedFile("b/lib.scad", "module box() cube(2);"));

        Assert.Empty(a.Missing);
        AmbiguousReference ambiguous = Assert.Single(a.Ambiguous);
        Assert.Equal("lib.scad", ambiguous.RawPath);
        Assert.Equal(ReferenceOrigin.Include, ambiguous.Origin);
        Assert.Equal(["/proj/a/lib.scad", "/proj/b/lib.scad"], ambiguous.Candidates);
        Assert.Equal(["/proj/main.scad"], ambiguous.NeededBy);
    }

    [Fact]
    public void ReAddingAChosenCandidate_ClearsTheAmbiguity()
    {
        UploadedFile[] withConflict =
        [
            new("main.scad", "include <lib.scad>\nbox();"),
            new("a/lib.scad", "module box() cube(1);"),
            new("b/lib.scad", "module box() cube(2);"),
        ];
        Assert.Single(ProjectAnalyzer.Analyze(withConflict).Analysis.Ambiguous);

        // The picker re-adds the chosen file with Name = the raw path.
        UploadedFile[] resolved = [.. withConflict, new UploadedFile("lib.scad", "module box() cube(1);")];
        (InMemoryFileSystem fs, ProjectAnalysis a) = ProjectAnalyzer.Analyze(resolved);

        Assert.Empty(a.Ambiguous);
        Assert.Empty(a.Missing);
        Assert.True(WebBundler.Bundle(fs, a.Root!, new WebBundleOptions()).Ok);
    }

    [Fact]
    public void DuplicateUploadName_LastWins_NotDoubleCounted()
    {
        ProjectAnalysis a = Analyze(
            null,
            new UploadedFile("main.scad", "cube(1);"),
            new UploadedFile("main.scad", "sphere(2);")); // same name re-dropped

        Assert.Equal(["/proj/main.scad"], a.EntryPointCandidates);
        Assert.Equal("/proj/main.scad", a.Root);
    }

    // ----- Never throws ----------------------------------------------------------------------------

    [Fact]
    public void EmptyUploads_ReturnEmptyAnalysis()
    {
        (InMemoryFileSystem fs, ProjectAnalysis a) = ProjectAnalyzer.Analyze([]);

        Assert.Empty(fs.Files);
        Assert.Empty(a.EntryPointCandidates);
        Assert.Null(a.InferredRoot);
        Assert.Null(a.Root);
        Assert.Null(a.Tree);
        Assert.Empty(a.Missing);
        Assert.Empty(a.Ambiguous);
        Assert.Empty(a.Diagnostics);
    }

    [Fact]
    public void MalformedSource_DoesNotThrow_AndSurfacesParseDiagnostics()
    {
        ProjectAnalysis a = Analyze(null, new UploadedFile("main.scad", "module ("));

        Assert.NotNull(a.Root); // still treated as the single file
        Assert.NotEmpty(a.Diagnostics); // a parse error (SB2xxx)
    }
}
