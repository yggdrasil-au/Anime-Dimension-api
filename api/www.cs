// api/www.cs

using System.Text;
using Microsoft.AspNetCore.Http;
using System.IO;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;


namespace ASP.NETCoreWebApi;

public static class WebEndpoints {

    static readonly String WwwRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "api", "www", "api"));

    static readonly Dictionary<String, String> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".mjs"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain; charset=utf-8",
        [".webmanifest"] = "application/manifest+json; charset=utf-8",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
    };

    static String GetContentType(String path) {
        String ext = Path.GetExtension(path);
        return !String.IsNullOrEmpty(ext) && ContentTypes.TryGetValue(ext, out String? ct) ? ct : "application/octet-stream";
    }

    // Return candidate paths for debug visibility.
    static Boolean TryResolveFile(String relative, out String fullPath, out List<String> candidates) {
        // Normalize and prevent path traversal
        relative = (relative ?? String.Empty).Replace('\\', '/').TrimStart('/');
        if (relative.Contains("..")) {
            fullPath = String.Empty;
            candidates = new List<string>();
            return false;
        }

        // Build candidate list
        candidates = new List<String>();
        if (String.IsNullOrWhiteSpace(relative) || relative.EndsWith('/')) {
            candidates.Add(Path.Combine(WwwRoot, relative, "index.html"));
        } else if (Path.HasExtension(relative)) {
            candidates.Add(Path.Combine(WwwRoot, relative));
        } else {
            candidates.Add(Path.Combine(WwwRoot, relative + ".html"));
            candidates.Add(Path.Combine(WwwRoot, relative, "index.html"));
        }

        foreach (String c in candidates) {
            String fp = Path.GetFullPath(c);
            if (!fp.StartsWith(WwwRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(fp)) {
                fullPath = fp;
                return true;
            }
        }
        fullPath = String.Empty;
        return false;
    }


    static IResult NotFoundOrDebug(String requested, List<String> candidates) {
        //#if DEBUG
        DTOs.DebugNotFoundResponse payload = new DTOs.DebugNotFoundResponse {
            requested = requested,
            root = WwwRoot,
            candidates = candidates,
            cwd = System.IO.Directory.GetCurrentDirectory()
        };
        return Microsoft.AspNetCore.Http.Results.Json(payload,
            (JsonTypeInfo<DTOs.DebugNotFoundResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.DebugNotFoundResponse))!,
            contentType: "application/json; charset=utf-8", statusCode: 404);
        //#else
        //return Results.StatusCode(StatusCodes.Status500InternalServerError);
        //#endif
    }

    public static void MapwwwEndpoints(this IEndpointRouteBuilder app) {

        // Redirect root to /www (API website home)
        app.MapGet(pattern: "/", () => Results.Redirect(url: "/www", permanent: false));

        // Serve common static asset folders copied during build
        foreach (String? basePath in new[] { "css/{**path}", "/js/{**path}", "/assets/{**path}" }) {
            app.MapGet(basePath, (String path) => {
                // basePath like "/css/{**path}" -> folder name at index 1
                String folder = basePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
                if (String.IsNullOrWhiteSpace(path))
                    return NotFoundOrDebug($"{folder}/", new List<String>());

                String rel = Path.Combine(folder, path).Replace('\\', '/');
                if (TryResolveFile(rel, out String? fp, out List<String>? candidates)) {
                    String ct = GetContentType(fp);
                    return Results.File(fp, contentType: ct);
                }
                return NotFoundOrDebug(rel, candidates);
            });
        }

        app.MapGet(pattern: "/robots.txt", () => {
            // return string directly, no file
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(value: "User-agent: *");
            sb.AppendLine(value: "Disallow: /");
            sb.AppendLine(value: "Crawl-delay: 10");
            return Results.Text(sb.ToString(), contentType: "text/plain; charset=utf-8");
        });

        // API website pages: /www and /www/**
        app.MapGet(pattern: "/www", () => {
            String rel = "index.html";
            return TryResolveFile(rel, out String? fp, out List<String>? candidates)
                    ? Results.File(fp, GetContentType(fp))
                    : NotFoundOrDebug(rel, candidates);
        });

        app.MapGet(pattern: "/www/{**path}", (String path) => {
            // Resolve inside www/ subtree (keep 'path' relative)
            String rel = (path ?? String.Empty).Replace('\\', '/');
            if (TryResolveFile(rel, out String? fp, out List<String>? candidates)) {
                String ct = GetContentType(fp);
                return Results.File(fp, contentType: ct);
            }
            return NotFoundOrDebug(rel, candidates);
        });
    }
}
