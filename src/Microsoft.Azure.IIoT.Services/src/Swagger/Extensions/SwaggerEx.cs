// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Swashbuckle.AspNetCore.Swagger {
    using Swashbuckle.AspNetCore.SwaggerGen;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc.Controllers;
    using Microsoft.Azure.IIoT.Auth.Clients;
    using Microsoft.Azure.IIoT.Http;
    using Microsoft.Azure.IIoT.Services.Swagger;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Azure.IIoT.Auth.Server;

    /// <summary>
    /// Configure swagger
    /// </summary>
    public static class SwaggerEx {

        /// <summary>
        /// Configure swagger
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        /// <param name="info"></param>
        public static void AddSwagger(this IServiceCollection services,
            ISwaggerConfig config, Info info) {

            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }
            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            // Generate swagger documentation
            services.AddSwaggerGen(options => {
                // Generate doc for version
                options.SwaggerDoc(info.Version, info);

                // Add annotations
                options.EnableAnnotations();

                // Add help
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory,
                    config.GetType().Assembly.GetName().Name + ".xml"), true);

                // If auth enabled, need to have bearer token to access any api
                if (config.WithAuth) {
                    var resource = config as IClientConfig;
                    if (string.IsNullOrEmpty(config.SwaggerAppId) || resource == null) {
                        options.AddSecurityDefinition("bearer", new ApiKeyScheme {
                            Description =
                                "Authorization token in the form of 'bearer <token>'",
                            Name = "Authorization",
                            In = "header"
                        });
                    }
                    else {
                        options.AddSecurityDefinition("oauth2", new OAuth2Scheme {
                            Type = "oauth2",
                            Flow = "implicit",
                            AuthorizationUrl = resource.GetAuthorityUrl() +
                                "/oauth2/authorize",
                            Scopes = services.GetRequiredScopes()
                                .ToDictionary(k => k, k => $"Access {k} operations"),
                            TokenUrl = resource.GetAuthorityUrl() +
                                "/oauth2/token"
                        });
                        options.OperationFilter<SecurityRequirementsOperationFilter>();
                    }
                }
            });
        }

        /// <summary>
        /// Use swagger in application
        /// </summary>
        /// <param name="app"></param>
        /// <param name="config"></param>
        /// <param name="info"></param>
        public static void UseSwagger(this IApplicationBuilder app,
            ISwaggerConfig config, Info info) {

            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }
            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            // Enable swagger and swagger ui
            app.UseSwagger(options => {
                options.PreSerializeFilters.Add((doc, request) => {
                    if (request.Headers.TryGetValue(HttpHeader.Location,
                            out var values) && values.Count > 0) {
                        doc.BasePath = "/" + values[0];
                    }
                    doc.Schemes = new List<string> { "http", "https" };
                });
                options.RouteTemplate = "{documentName}/swagger.json";
            });
            if (!config.UIEnabled) {
                return;
            }
            app.UseSwaggerUI(options => {
                if (config.WithAuth) {
                    options.OAuthAppName(info.Title);
                    options.OAuthClientId(config.SwaggerAppId);
                    if (!string.IsNullOrEmpty(config.SwaggerAppSecret)) {
                        options.OAuthClientSecret(config.SwaggerAppSecret);
                    }
                    var resource = config as IAuthConfig;
                    if (!string.IsNullOrEmpty(resource?.Audience)) {
                        options.OAuthAdditionalQueryStringParams(
                            new Dictionary<string, string> {
                                ["resource"] = resource.Audience
                            });
                    }
                }
                options.RoutePrefix = "";
                options.SwaggerEndpoint("v1/swagger.json", info.Version);
            });
        }

        /// <summary>
        /// Gather security operations
        /// </summary>
        private class SecurityRequirementsOperationFilter : IOperationFilter {

            /// <summary>
            /// Create filter using injected and configured authorization options
            /// </summary>
            /// <param name="options"></param>
            public SecurityRequirementsOperationFilter(IOptions<AuthorizationOptions> options) {
                _options = options;
            }

            /// <summary>
            /// Process operation
            /// </summary>
            /// <param name="operation"></param>
            /// <param name="context"></param>
            public void Apply(Operation operation, OperationFilterContext context) {
                var descriptor = context.ApiDescription.ActionDescriptor as
                    ControllerActionDescriptor;
                var claims = descriptor.GetRequiredPolicyGlaims(_options.Value);
                if (claims.Any()) {
                    operation.Responses.Add("401",
                        new Response { Description = "Unauthorized" });
                    operation.Responses.Add("403",
                        new Response { Description = "Forbidden" });

                    // Add security description
                    operation.Security = new List<IDictionary<string, IEnumerable<string>>> {
                        new Dictionary<string, IEnumerable<string>> {
                            { "oauth2", claims }
                        }
                    };
                }
            }

            private readonly IOptions<AuthorizationOptions> _options;
        }
    }
}
