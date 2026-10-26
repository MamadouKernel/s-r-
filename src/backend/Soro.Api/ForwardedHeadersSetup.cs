using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Soro.Api;

/// <summary>
/// Derrière un reverse proxy (nginx, Caddy…), l'adresse que voit le serveur est celle du proxy : toutes les limites de débit (par adresse) se
/// partageraient entre tous les clients. On ne croit l'en-tête <c>X-Forwarded-For</c> que de la part des proxys déclarés dans
/// <c>ForwardedHeaders:KnownProxies</c> ; sans déclaration, il est ignoré (un client pourrait sinon se faire passer pour n'importe quelle adresse).
/// </summary>
public static class ForwardedHeadersSetup
{
    public static ForwardedHeadersOptions? OptionsFrom(IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        if (proxies.Length == 0)
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address)
                ? address
                : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contient « {proxy} », qui n'est pas une adresse IP."));
        }

        return options;
    }

    /// <summary>En-têtes de protection du navigateur : l'API ne sert que du JSON et des fichiers, jamais une page.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers["Cache-Control"] = "no-store";
                }

                return Task.CompletedTask;
            });
            await next();
        });
}
