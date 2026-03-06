using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using System.Reflection;

namespace MetaAnime.Utilities.Extensions;

/// <summary>
/// Wraps <see cref="IApiDescriptionGroupCollectionProvider"/> and removes any
/// <see cref="ApiParameterDescription"/> whose <see cref="ModelMetadata"/> has
/// <c>IsEnhancedModelMetadataSupported == false</c>.
/// <para>
/// On .NET 10, <c>Microsoft.AspNetCore.OpenApi</c> accesses the internal
/// <c>ModelMetadata.IsConvertibleType</c> getter for every parameter.  That getter throws
/// <see cref="NotSupportedException"/> when the internal <c>IsEnhancedModelMetadataSupported</c>
/// backing field is <see langword="false"/>.  This occurs for route-constraint parameters like
/// <c>{version:apiVersion}</c> that are not backed by a <see cref="DefaultModelMetadata"/>.
/// Stripping those parameters here prevents the crash without touching controller routes.
/// </para>
/// </summary>
internal sealed class SafeApiDescriptionGroupCollectionProvider(
    IApiDescriptionGroupCollectionProvider inner) : IApiDescriptionGroupCollectionProvider
{
    // IsEnhancedModelMetadataSupported is an internal property on ModelMetadata.
    // Cache it once via reflection; fall back to the DefaultModelMetadata type-check if not found.
    private static readonly PropertyInfo? _isEnhancedProp =
        typeof(ModelMetadata).GetProperty(
            "IsEnhancedModelMetadataSupported",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    public ApiDescriptionGroupCollection ApiDescriptionGroups
    {
        get
        {
            var groups = inner.ApiDescriptionGroups;

            foreach (var group in groups.Items)
            {
                foreach (var description in group.Items)
                {
                    var unresolved = description.ParameterDescriptions
                        .Where(static p => !IsMetadataSafe(p.ModelMetadata))
                        .ToList();

                    foreach (var p in unresolved)
                        description.ParameterDescriptions.Remove(p);
                }
            }

            return groups;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the metadata can safely have
    /// <c>IsConvertibleType</c> accessed (i.e. <c>IsEnhancedModelMetadataSupported == true</c>).
    /// </summary>
    private static bool IsMetadataSafe(ModelMetadata? metadata)
    {
        if (metadata is null) return false;

        // Fast path: DefaultModelMetadata always sets IsEnhancedModelMetadataSupported = true.
        if (metadata is DefaultModelMetadata) return true;

        // Reflection path for any other subclass (e.g. custom Asp.Versioning metadata types).
        if (_isEnhancedProp is not null)
            return _isEnhancedProp.GetValue(metadata) is true;

        // Cannot determine — assume unsafe to avoid a runtime crash.
        return false;
    }
}
