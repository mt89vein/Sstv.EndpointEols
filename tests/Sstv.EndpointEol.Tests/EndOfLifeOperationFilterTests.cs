using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Sstv.EndpointEol.Swashbuckle;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for <see cref="EndOfLifeOperationFilter"/> OpenAPI annotations,
/// including endpoints marked through Minimal API metadata instead of attributes.
/// </summary>
[TestFixture]
public sealed class EndOfLifeOperationFilterTests
{
    private static readonly MethodInfo HandlerMethod =
        typeof(EndOfLifeOperationFilterTests).GetMethod(nameof(Handler), BindingFlags.Static | BindingFlags.NonPublic)!;

    [Test]
    public void Apply_WhenActionIsMarked_MarksOperationDeprecatedWithExtensions()
    {
        var operation = Apply(CreateContext(
            typeof(MarkedController),
            nameof(MarkedController.MarkedAction)
        ));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.Deprecated, Is.True);
            Assert.That(Extension(operation, "x-sunset"), Is.EqualTo("2027-01-01"));
            Assert.That(Extension(operation, "x-eol-warn-after"), Is.EqualTo("2026-10-23"));
            Assert.That(Extension(operation, "x-eol-replaced-by"), Is.EqualTo("/api/v2/test"));
            Assert.That(operation.Description, Does.Contain("Deprecated"));
            Assert.That(operation.Description, Does.Contain("2027-01-01"));
        }
    }

    [Test]
    public void Apply_WhenOnlyControllerIsMarked_MarksOperationDeprecated()
    {
        var operation = Apply(CreateContext(typeof(MarkedController), nameof(MarkedController.InheritedAction)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.Deprecated, Is.True);
            Assert.That(Extension(operation, "x-sunset"), Is.EqualTo("2027-05-05"));
        }
    }

    [Test]
    public void Apply_WhenActionAttributeOverridesControllerOne_UsesActionDates()
    {
        var operation = Apply(CreateContext(typeof(MarkedController), nameof(MarkedController.OverriddenAction)));

        Assert.That(Extension(operation, "x-sunset"), Is.EqualTo("2030-01-01"));
    }

    [Test]
    public void Apply_WhenAttributeComesFromEndpointMetadata_MarksOperationDeprecated()
    {
        // Minimal API endpoints attach the attribute via WithMetadata(), invisible to reflection.
        var operation = Apply(CreateContext(
            declaringType: null,
            methodInfo: HandlerMethod,
            endpointMetadata: [new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/test" }]
        ));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.Deprecated, Is.True);
            Assert.That(Extension(operation, "x-sunset"), Is.EqualTo("2027-01-01"));
            Assert.That(Extension(operation, "x-eol-warn-after"), Is.EqualTo("2026-10-23"));
            Assert.That(Extension(operation, "x-eol-replaced-by"), Is.EqualTo("/api/v2/test"));
        }
    }

    [Test]
    public void Apply_WhenSeveralEndpointMetadataAttributes_UsesTheLastOne()
    {
        var operation = Apply(CreateContext(
            declaringType: null,
            methodInfo: HandlerMethod,
            endpointMetadata:
            [
                new EndOfLifeAttribute("2027-01-01"),
                new EndOfLifeAttribute("2029-12-31")
            ]
        ));

        Assert.That(Extension(operation, "x-sunset"), Is.EqualTo("2029-12-31"));
    }

    [Test]
    public void Apply_WhenNothingIsMarked_LeavesOperationUntouched()
    {
        var operation = Apply(CreateContext(typeof(UnmarkedController), nameof(UnmarkedController.PlainAction)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.Deprecated, Is.False);
            Assert.That(operation.Extensions, Is.Null.Or.Empty);
            Assert.That(operation.Description, Is.Null);
        }
    }

    [Test]
    public void Apply_WhenOperationHasDescription_PrependsExistingDescription()
    {
        var operation = new OpenApiOperation { Description = "Original text." };

        new EndOfLifeOperationFilter().Apply(
            operation,
            CreateContext(typeof(MarkedController), nameof(MarkedController.MarkedAction))
        );

        Assert.That(operation.Description, Does.StartWith("Original text."));
    }

    private static OpenApiOperation Apply(OperationFilterContext context)
    {
        var operation = new OpenApiOperation();
        new EndOfLifeOperationFilter().Apply(operation, context);

        return operation;
    }

    private static string Extension(OpenApiOperation operation, string name) =>
        ((JsonNodeExtension)operation.Extensions![name]).Node.GetValue<string>();

    private static OperationFilterContext CreateContext(
        Type? declaringType,
        string? methodName = null,
        MethodInfo? methodInfo = null,
        IList<object>? endpointMetadata = null
    )
    {
        var method = methodInfo
            ?? declaringType!.GetMethod(methodName!, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)!;

        var apiDescription = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = endpointMetadata ?? []
            }
        };

        // The filter never touches the schema generator, so a null reference is enough here.
        return new OperationFilterContext(
            apiDescription,
            null!,
            new SchemaRepository(),
            new OpenApiDocument(),
            method);
    }

    private static void Handler()
    {
    }

    [EndOfLife("2027-05-05")]
    private sealed class MarkedController
    {
        [EndOfLife("2027-01-01", WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/test")]
        public void MarkedAction()
        {
        }

        [EndOfLife("2030-01-01")]
        public void OverriddenAction()
        {
        }

        public void InheritedAction()
        {
        }

        public void PlainAction()
        {
        }
    }

    private sealed class UnmarkedController
    {
        public void PlainAction()
        {
        }
    }
}
