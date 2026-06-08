namespace ScadBundler.Core.Semantics;

/// <summary>The kind of top-level declaration a <see cref="Symbol"/> identifies.</summary>
public enum SymbolKind
{
    /// <summary>A <c>module</c> definition.</summary>
    Module,

    /// <summary>A <c>function</c> definition.</summary>
    Function,

    /// <summary>A top-level variable (an <see cref="Ast.AssignmentStatement"/>).</summary>
    Variable,
}
