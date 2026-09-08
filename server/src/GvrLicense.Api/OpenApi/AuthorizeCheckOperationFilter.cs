using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GvrLicense.Api.OpenApi;

/// <summary>
/// Bearer JWT solo en endpoints con <see cref="IAuthorizeData"/> (heartbeat/usage/deactivate).
/// Sin esto, el candado de Swagger aparece también en activate/updates/health.
/// </summary>
public sealed class AuthorizeCheckOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresAuth = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuth)
        {
            operation.Security = [];
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            }
        ];
    }
}
