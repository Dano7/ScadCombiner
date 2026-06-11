using System.Text;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Semantics;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Deterministic identifier generator for the hardening profiles (Slice 7 §5). Every name is a pure
/// function of a global <b>avalanche seed</b> (a hash of the whole post-inline bundle), so two runs on
/// the same input produce byte-identical names, yet a one-character source change reshuffles
/// <i>every</i> generated name. Never uses memory addresses or live hash codes (those break goldens and
/// idempotence).
/// <list type="bullet">
/// <item><b>Obfuscate</b>: each declaration's name is an independent seed-derived opaque token
/// (<c>_&lt;base32&gt;</c>); avalanche is intrinsic.</item>
/// <item><b>Minify</b>: the shortest names (<c>a, b, …, z, aa, …</c>) assigned in an order
/// <i>permuted</i> by the seed — the set of names (and total byte size) stays minimal, but which
/// declaration gets <c>a</c> avalanches.</item>
/// </list>
/// Generated names never collide with one another, with reserved built-ins/keywords, or with any name
/// passed to the constructor as already-taken.
/// </summary>
internal sealed class NameGenerator
{
    // Crockford-style base32 alphabet (lowercase + digits, ambiguous chars o/l/1/0 dropped): the first
    // char of an opaque name is always '_' so the result is a valid identifier regardless of alphabet.
    private const string Base32Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "module", "function", "include", "use", "if", "else", "for", "intersection_for",
        "let", "assert", "echo", "each", "true", "false", "undef",
    };

    private readonly HardeningProfile _profile;
    private readonly ulong _seed;
    private readonly HashSet<string> _taken;
    private int _shortCounter;
    private int _freshCounter;

    /// <summary>Creates a generator for <paramref name="profile"/> seeded by <paramref name="seed"/>.</summary>
    /// <param name="profile">The active hardening profile (selects the naming scheme).</param>
    /// <param name="seed">The global avalanche seed (see <see cref="Fnv1a64"/>).</param>
    /// <param name="alreadyTaken">Names that must never be generated (existing identifiers in the bundle).</param>
    public NameGenerator(HardeningProfile profile, ulong seed, IEnumerable<string> alreadyTaken)
    {
        _profile = profile;
        _seed = seed;
        _taken = new HashSet<string>(alreadyTaken, StringComparer.Ordinal);
        _taken.UnionWith(ReservedWords);
    }

    /// <summary>The 64-bit FNV-1a hash of <paramref name="text"/> — the avalanche seed source.</summary>
    /// <param name="text">The canonical bundle text to hash.</param>
    /// <returns>The seed.</returns>
    public static ulong Fnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash = (hash ^ (byte)c) * 1099511628211UL;
            hash = (hash ^ (byte)(c >> 8)) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Reserves <paramref name="name"/> so it is never generated (e.g. a kept Customizer param).</summary>
    /// <param name="name">The name to reserve.</param>
    public void Reserve(string name) => _taken.Add(name);

    /// <summary>
    /// Assigns a unique generated name to each of <paramref name="count"/> declarations enumerated in a
    /// stable document order. Index <c>i</c> of the result is the new name for declaration <c>i</c>.
    /// </summary>
    /// <param name="count">The number of declarations to name.</param>
    /// <returns>The generated names, indexed by declaration ordinal.</returns>
    public string[] AssignBatch(int count)
    {
        return _profile == HardeningProfile.Minify ? MinifyBatch(count) : ObfuscateBatch(count);
    }

    /// <summary>A single fresh unique name (for synthesized aliases and injected decoys).</summary>
    /// <returns>A unique generated name in the active profile's style.</returns>
    public string FreshName() =>
        _profile == HardeningProfile.Minify ? NextShort() : Opaque(unchecked(0x5BD1E995 + _freshCounter++));

    private string[] MinifyBatch(int count)
    {
        var names = new string[count];
        int[] order = [.. Enumerable.Range(0, count).OrderBy(KeyFor)];
        foreach (int index in order)
        {
            names[index] = NextShort();
        }

        return names;
    }

    private string[] ObfuscateBatch(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = Opaque(i);
        }

        return names;
    }

    private string NextShort()
    {
        while (true)
        {
            string candidate = ShortName(_shortCounter++);
            if (!IsBuiltinName(candidate) && _taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    // Short names can land on a built-in (e.g. `ln`, `pi`→`PI` differs in case, `sin`); never shadow one.
    private static bool IsBuiltinName(string name) =>
        Builtins.IsModule(name) || Builtins.IsFunction(name) || Builtins.IsConstant(name);

    private string Opaque(int ordinal)
    {
        ulong key = KeyFor(ordinal);
        for (int attempt = 0; ; attempt++)
        {
            string candidate = "_" + Base32(Mix(key ^ (ulong)attempt));
            if (_taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private ulong KeyFor(int ordinal) => Mix(_seed ^ unchecked((ulong)ordinal * 0x9E3779B97F4A7C15UL));

    /// <summary>splitmix64 finalizer — strong avalanche, so flipping one bit of <paramref name="x"/>
    /// scrambles the whole result. Shared so injection passes can make seed-derived deterministic
    /// decisions.</summary>
    /// <param name="x">The value to mix.</param>
    /// <returns>The mixed value.</returns>
    internal static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    // Seven base32 chars (35 bits) of the key — a fixed-width opaque suffix.
    private static string Base32(ulong value)
    {
        var chars = new char[7];
        for (int i = 0; i < 7; i++)
        {
            chars[i] = Base32Alphabet[(int)(value & 0x1F)];
            value >>= 5;
        }

        return new string(chars);
    }

    // Bijective base-26 (a=0): 0->a … 25->z, 26->aa, 27->ab … — the shortest distinct identifiers.
    private static string ShortName(int index)
    {
        var builder = new StringBuilder();
        int n = index;
        do
        {
            builder.Insert(0, (char)('a' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return builder.ToString();
    }
}
