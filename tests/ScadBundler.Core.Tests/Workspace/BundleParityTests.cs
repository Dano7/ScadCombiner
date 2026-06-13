using System.Linq;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Workspace;
using Xunit;

namespace ScadBundler.Core.Tests.Workspace;

/// <summary>
/// The bundle-parity anchor (Slice W0): <see cref="WebBundler"/> over an <see cref="InMemoryFileSystem"/>
/// must produce text <b>byte-identical</b> to the same pipeline run over a real disk fixture of the same
/// content — across Normal / Minify / Obfuscate. This proves the in-memory file system drives
/// <see cref="Bundler"/> exactly like the CLI's <see cref="DiskFileSystem"/>, and that the option mapping
/// matches.
/// </summary>
public sealed class BundleParityTests
{
    public static TheoryData<HardeningProfile> Profiles => new()
    {
        HardeningProfile.None,
        HardeningProfile.Minify,
        HardeningProfile.Obfuscate,
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void SingleFile_IsByteIdenticalToDisk(HardeningProfile profile)
    {
        UploadedFile[] uploads =
        [
            new("main.scad", "// single-file model\nsize = 10;\ncube(size);\n"),
        ];
        AssertParity(uploads, "main.scad", new WebBundleOptions(Hardening: profile));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void IncludeAndUse_IsByteIdenticalToDisk(HardeningProfile profile)
    {
        UploadedFile[] uploads =
        [
            new("main.scad", "use <lib_use.scad>\ninclude <lib_inc.scad>\nwidth = 5;\nwidget(width);\nhelper();\n"),
            new("lib_use.scad", "// reusable widget\nmodule widget(w) cube(w);\n"),
            new("lib_inc.scad", "// shared helper\nmodule helper() sphere(2);\n"),
        ];
        AssertParity(uploads, "main.scad", new WebBundleOptions(Hardening: profile));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void CustomizerShaped_IsByteIdenticalToDisk(HardeningProfile profile)
    {
        UploadedFile[] uploads =
        [
            new(
                "main.scad",
                "// MIT License — sample header\n"
                + "include <fhlib.scad>\n"
                + "/* [Dimensions] */\n"
                + "// width of the thing\n"
                + "width = 20; // [10:50]\n"
                + "height = 8;\n"
                + "/* [Hidden] */\n"
                + "secret = 3;\n"
                + "thing(width, height);\n"),
            new("fhlib.scad", "// helper library\nmodule thing(w, h) {\n    cube([w, h, secret]);\n}\n"),
        ];
        AssertParity(uploads, "main.scad", new WebBundleOptions(Hardening: profile));
        // Also exercise the no-license / strip / collision knobs against disk.
        AssertParity(uploads, "main.scad", new WebBundleOptions(BundleLicenses: false, Hardening: profile));
        AssertParity(uploads, "main.scad", new WebBundleOptions(Hardening: profile, StripLicense: true));
    }

    [Fact]
    public void OptionPermutations_AreByteIdenticalToDisk()
    {
        UploadedFile[] uploads =
        [
            new(
                "main.scad",
                "// MIT License — sample header\n"
                + "include <lib.scad>\n"
                + "/* [Dimensions] */\n"
                + "// width of the thing\n"
                + "width = 20; // [10:50]\n"
                + "thing(width);\n"),
            new("lib.scad", "// helper library\nmodule thing(w) cube(w);\n"),
        ];

        // Every W3 knob permutation must map to the same bytes the equivalent CLI flags produce. AssertParity
        // runs the in-memory facade and a disk fixture (mirroring BundleCommand's mapping) and compares.
        WebBundleOptions[] permutations =
        [
            new(OnCollision: CollisionStrategy.Auto),
            new(OnCollision: CollisionStrategy.Prefix),
            new(OnCollision: CollisionStrategy.Error),
            new(OnCollision: CollisionStrategy.KeepFirst),
            new(OnCollision: CollisionStrategy.KeepLast),
            new(PreserveComments: false),
            new(BundleLicenses: false, PreserveComments: false),
            new(Hardening: HardeningProfile.Minify, OnCollision: CollisionStrategy.Prefix),
            new(Hardening: HardeningProfile.Minify, PreserveComments: false),
            new(Hardening: HardeningProfile.Obfuscate, StripLicense: true, OnCollision: CollisionStrategy.KeepLast),
        ];

        foreach (WebBundleOptions options in permutations)
        {
            AssertParity(uploads, "main.scad", options);
        }
    }

    // -------------------------------------------------------------------------------------------------

    private static void AssertParity(UploadedFile[] uploads, string rootName, WebBundleOptions options)
    {
        (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);
        Assert.NotNull(analysis.Root);
        WebBundleResult web = WebBundler.Bundle(fs, analysis.Root!, options);

        string diskText = DiskBundle(uploads, rootName, options);
        Assert.Equal(diskText, web.Text);
    }

    // Runs the same pipeline over a real temp-directory fixture, mirroring BundleCommand's option mapping
    // (the IFileSystem overload with no library paths — no OPENSCADPATH — to match the browser sandbox).
    private static string DiskBundle(UploadedFile[] uploads, string rootName, WebBundleOptions options)
    {
        string dir = Path.Combine(Path.GetTempPath(), "scadbundler-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            foreach (UploadedFile upload in uploads)
            {
                string full = Path.Combine(dir, upload.Name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, upload.Text);
            }

            var bundleOptions = new BundleOptions(
                [],
                options.OnCollision,
                options.BundleLicenses,
                options.PreserveComments,
                options.Hardening,
                options.StripLicense);
            var emitOptions = new EmitOptions(
                Minify: options.Hardening == HardeningProfile.Minify,
                PreserveComments: options.Hardening == HardeningProfile.None && options.PreserveComments);

            string root = Path.Combine(dir, rootName.Replace('/', Path.DirectorySeparatorChar));
            BundleResult result = Bundler.Bundle(root, bundleOptions, DiskFileSystem.Instance);
            return result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
                ? string.Empty
                : Emitter.Emit(result.Bundled, emitOptions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
