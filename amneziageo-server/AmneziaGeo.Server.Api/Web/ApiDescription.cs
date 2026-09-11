using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Describes the routes of the API as an OpenAPI document.
/// </summary>
public static class ApiDescription
{
    /// <summary>
    /// The route the document is served at.
    /// </summary>
    public const string Route = "/api/openapi.json";

    private const string Scheme = "bearer";

    /// <summary>
    /// Registers the document with the token and the right each route asks for.
    /// </summary>
    public static IServiceCollection AddApiDescription(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(Title);
            options.AddOperationTransformer(Guard);
        });

    /// <summary>
    /// Serves the document to a caller with a valid token.
    /// </summary>
    public static IEndpointRouteBuilder MapApiDescription(this IEndpointRouteBuilder routes)
    {
        routes.MapOpenApi(Route).RequireCaller();

        return routes;
    }

    private static Task Title(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "AmneziaGeo Server";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = Scheme,
            Description = "the access token of a session or a long lived token that starts with " + ApiTokenRules.Prefix,
        };

        return Task.CompletedTask;
    }

    private static Task Guard(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var needed = context.Description.ActionDescriptor.EndpointMetadata.OfType<CallerRequirement>().LastOrDefault();
        if (needed is null)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(Scheme, context.Document)] = Rights(needed),
        });

        return Task.CompletedTask;
    }

    private static List<string> Rights(CallerRequirement needed) => needed.Scope.Length == 0 ? [] : [needed.Scope];
}
