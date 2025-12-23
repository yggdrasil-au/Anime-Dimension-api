// api/logout.cs

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

using System.Text.Json.Serialization.Metadata;

using ASP.NETCoreWebApi.Serialization;

namespace ASP.NETCoreWebApi;

public static class LogoutEndpoints {
    public static void MapLogoutEndpoints(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) {
        app.MapPost("/api/logout", async (HttpRequest req, HttpResponse res, Sql.UsersDbContext usersDb) => {
            // 1. Get the token from the secure cookie first (for web)
            string? sessionToken = req.Cookies["session_token"];

            // 2. If no cookie, try to get it from the body (for native)
            // This part is optional if your native apps don't use this endpoint,
            // but it's good practice for consistency.
            if (string.IsNullOrEmpty(sessionToken)) {
                try {
                    CsrfToken? body = await req.ReadFromJsonAsync<CsrfToken>();
                    sessionToken = body?._csrf_token;
                } catch { /* Ignore if body is not valid JSON */ }
            }

            // 3. If we still don't have a token, we can't do anything.
            if (string.IsNullOrEmpty(sessionToken)) {
                // Silently succeed, as the user is effectively logged out.
                return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = "No active session found." },
                    (JsonTypeInfo<DTOs.SimpleStatusResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SimpleStatusResponse))!);
            }

            // 4. Find and remove the session from the database
            Models.UserSession? session = await usersDb.UserSessions.FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

            if (session != null) {
                usersDb.UserSessions.Remove(session);
                await usersDb.SaveChangesAsync();
            }

            // 5. Explicitly delete the cookie from the browser
            res.Cookies.Delete("session_token");

            return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = null },
                (JsonTypeInfo<DTOs.SimpleStatusResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SimpleStatusResponse))!);
        });
    }

    public class CsrfToken {
        public string? _csrf_token {
            get; set;
        }
    }
}
