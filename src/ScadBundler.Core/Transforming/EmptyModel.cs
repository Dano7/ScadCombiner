using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// A no-op <see cref="ISemanticModel"/> used as the <see cref="TransformContext.Model"/> default before
/// the <see cref="Transformer"/> sets a real one — every query returns empty/<c>null</c>. Never reached
/// by a model-needing pass (the transformer refreshes the model first), but keeps the property non-null.
/// </summary>
internal sealed class EmptyModel : ISemanticModel
{
    /// <summary>The shared instance.</summary>
    public static readonly EmptyModel Instance = new();

    private EmptyModel()
    {
    }

    public IReadOnlyList<ModuleDefinition> Modules(SourceFile file) => [];

    public IReadOnlyList<FunctionDefinition> Functions(SourceFile file) => [];

    public IReadOnlyList<AssignmentStatement> TopLevelVariables(SourceFile file) => [];

    public IReadOnlyList<AssignmentStatement> PrivateConstants(SourceFile usedFile) => [];

    public IReadOnlyList<AssignmentStatement> PrivateConstants(IReadOnlyList<SourceFile> mergedFiles) => [];

    public Symbol? Resolve(AstNode reference) => null;

    public IReadOnlyList<AstNode> ReferencesTo(Symbol declaration) => [];
}
