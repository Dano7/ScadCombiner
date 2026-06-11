using ScadBundler.Core.Inlining;
using ScadBundler.Core.Transforming;
using Xunit;

namespace ScadBundler.Core.Tests.Transforming;

/// <summary>Unit tests for the deterministic avalanche name generator (Slice 7 §5).</summary>
public sealed class NameGeneratorTests
{
    [Fact]
    public void Fnv1a64_IsDeterministic_AndDiffersOnOneCharChange()
    {
        Assert.Equal(NameGenerator.Fnv1a64("cube(1);"), NameGenerator.Fnv1a64("cube(1);"));
        Assert.NotEqual(NameGenerator.Fnv1a64("cube(1);"), NameGenerator.Fnv1a64("cube(2);"));
    }

    [Fact]
    public void Minify_AssignsShortestUniqueNames()
    {
        var generator = new NameGenerator(HardeningProfile.Minify, 12345UL, []);
        string[] names = generator.AssignBatch(5);

        Assert.Equal(5, names.Distinct().Count());
        // The five shortest identifiers, in some seed-permuted order.
        Assert.Equal(["a", "b", "c", "d", "e"], names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Minify_SkipsAlreadyTakenNames()
    {
        var generator = new NameGenerator(HardeningProfile.Minify, 1UL, ["a", "b"]);
        string[] names = generator.AssignBatch(3);

        Assert.DoesNotContain("a", names);
        Assert.DoesNotContain("b", names);
        Assert.Equal(3, names.Distinct().Count());
    }

    [Fact]
    public void Obfuscate_AssignsOpaqueUnderscorePrefixedNames()
    {
        var generator = new NameGenerator(HardeningProfile.Obfuscate, 999UL, []);
        string[] names = generator.AssignBatch(8);

        Assert.Equal(8, names.Distinct().Count());
        Assert.All(names, n => Assert.StartsWith("_", n, StringComparison.Ordinal));
        Assert.All(names, n => Assert.Matches("^_[a-z0-9]+$", n));
    }

    [Fact]
    public void SameSeedAndProfile_ProducesIdenticalBatch()
    {
        string[] a = new NameGenerator(HardeningProfile.Obfuscate, 42UL, []).AssignBatch(10);
        string[] b = new NameGenerator(HardeningProfile.Obfuscate, 42UL, []).AssignBatch(10);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Avalanche_OneSeedBitFlip_ReshufflesMostNames()
    {
        string[] a = new NameGenerator(HardeningProfile.Obfuscate, NameGenerator.Fnv1a64("x"), []).AssignBatch(20);
        string[] b = new NameGenerator(HardeningProfile.Obfuscate, NameGenerator.Fnv1a64("y"), []).AssignBatch(20);

        int shared = a.Intersect(b).Count();
        Assert.True(shared <= 2, $"expected near-total reshuffle, {shared}/20 names shared");
    }

    [Fact]
    public void Minify_Avalanche_PermutesAssignmentOrder()
    {
        // The set of names is identical (size is minimal either way) but the order differs.
        string[] a = new NameGenerator(HardeningProfile.Minify, NameGenerator.Fnv1a64("a"), []).AssignBatch(12);
        string[] b = new NameGenerator(HardeningProfile.Minify, NameGenerator.Fnv1a64("b"), []).AssignBatch(12);

        Assert.Equal(a.OrderBy(n => n, StringComparer.Ordinal), b.OrderBy(n => n, StringComparer.Ordinal));
        Assert.NotEqual(a, b); // but the per-ordinal assignment is permuted
    }

    [Fact]
    public void FreshName_IsUnique_AcrossCalls()
    {
        var generator = new NameGenerator(HardeningProfile.Obfuscate, 7UL, []);
        string[] fresh = [generator.FreshName(), generator.FreshName(), generator.FreshName()];
        Assert.Equal(3, fresh.Distinct().Count());
    }

    [Fact]
    public void Reserved_NeverGeneratesAKeywordOrBuiltin()
    {
        // A large minify batch forces multi-letter names through the keyword/builtin range (if, or, …).
        var generator = new NameGenerator(HardeningProfile.Minify, 3UL, []);
        string[] names = generator.AssignBatch(800);

        Assert.DoesNotContain("if", names);
        Assert.DoesNotContain("for", names);
        Assert.DoesNotContain("let", names);
        Assert.DoesNotContain("ln", names);  // built-in function
        Assert.DoesNotContain("PI", names);
        Assert.Equal(800, names.Distinct().Count());
    }
}
