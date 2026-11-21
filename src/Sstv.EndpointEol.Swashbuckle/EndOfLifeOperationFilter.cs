using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Sstv.EndpointEol.Swashbuckle;

/// <summary>
/// Marks operations with <see cref="EndOfLifeAttribute"/> as deprecated in OpenAPI documents:
/// sets deprecated flag, adds human-readable sunset note and x-* vendor extensions.
/// </summary>
public sealed class EndOfLifeOperationFilter : IOperationFilter
{
    private const string DATE_FORMAT = "yyyy-MM-dd";

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var attribute = ResolveAttribute(context);
        if (attribute is null)
        {
            return;
        }

        var memberName = $"{context.MethodInfo.DeclaringType?.Name}.{context.MethodInfo.Name}";
        var policy = EndOfLifePolicy.From(attribute, memberName);

        operation.Deprecated = true;
        operation.Description = AppendSunsetNote(operation.Description, policy);
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        operation.Extensions["x-sunset"] = CreateExtension(policy.BlockAfter.ToString(DATE_FORMAT));

        if (policy.WarnAfter is { } warnAfter)
        {
            operation.Extensions["x-eol-warn-after"] = CreateExtension(warnAfter.ToString(DATE_FORMAT));
        }

        if (policy.ReplacedBy is { } replacedBy)
        {
            operation.Extensions["x-eol-replaced-by"] = CreateExtension(replacedBy);
        }
    }

    /// <summary>
    /// Resolves the attribute the same way the middleware does: the last one wins, so an
    /// action-level attribute overrides a controller-level one.
    /// </summary>
    private static EndOfLifeAttribute? ResolveAttribute(OperationFilterContext context)
    {
        var attribute = context.MethodInfo.GetCustomAttribute<EndOfLifeAttribute>(inherit: true)
            ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<EndOfLifeAttribute>(inherit: true);

        // Minimal API endpoints attach metadata through WithMetadata(), which reflection cannot see.
        if (attribute is not null)
        {
            return attribute;
        }

        return context.ApiDescription?.ActionDescriptor.EndpointMetadata?
            .OfType<EndOfLifeAttribute>()
            .LastOrDefault();
    }

    /// <summary>
    /// Wraps a string as an OpenAPI vendor extension value.
    /// </summary>
    private static JsonNodeExtension CreateExtension(string value) =>
        new(JsonValue.Create(value));

    private static string AppendSunsetNote(string? existingDescription, EndOfLifePolicy policy)
    {
        var note = new StringBuilder();

        if (!string.IsNullOrEmpty(existingDescription))
        {
            note.Append(existingDescription).Append("\n\n");
        }

        note.Append("**Deprecated.** Planned removal (sunset): **").Append(policy.BlockAfter.ToString(DATE_FORMAT)).Append("**.");

        if (policy.WarnAfter is { } warnAfter)
        {
            note.Append(" Warnings begin: ").Append(warnAfter.ToString(DATE_FORMAT)).Append('.');
        }

        if (policy.ReplacedBy is { } replacedBy)
        {
            note.Append(" Replacement: `").Append(replacedBy).Append("`.");
        }

        return note.ToString();
    }
}
