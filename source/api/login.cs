// api/login.cs
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ASP.NETCoreWebApi;

public static class LoginEndpoints {

    public static void MapLoginEndpoints(this IEndpointRouteBuilder app) {

        app.MapPut("/api/login/validateState", async (HttpRequest req, Sql.UsersDbContext usersDb, ILogger<Program> logger) => {
            try {
                string? token = req.Cookies["session_token"];
                if (string.IsNullOrEmpty(token)) {
                    if (req.ContentLength.HasValue && req.ContentLength.Value > 0) {
                        try {
                            ValidateRequest? body = await req.ReadFromJsonAsync<ValidateRequest>();
                            token = body?.token;
                        } catch (Exception ex) {
                            logger.LogWarning("Failed to parse JSON body: {Message}", ex.Message);
                        }
                    }
                }

                // If token is not in cookie, check the Authorization header
                if (string.IsNullOrEmpty(token)) {
                    string? authHeader = req.Headers["Authorization"];
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ")) {
                        token = authHeader.Substring(7); // "Bearer ".Length
                    }
                }

                if (string.IsNullOrEmpty(token)) {
                    logger.LogInformation("401: Missing session token");
                    return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Missing session token" },
                        statusCode: 401);
                }

                Models.UserSession? session = await usersDb.UserSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SessionToken == token);

                if (session == null || session.ExpiresAt < DateTime.UtcNow) {
                    if (req.Cookies.ContainsKey("session_token")) {
                        req.HttpContext.Response.Cookies.Delete("session_token");
                    }
                    logger.LogInformation("401: Invalid or expired session token");
                    return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Invalid or expired session token" },
                        statusCode: 401);
                }

                Models.User? user = await usersDb.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == session.UserId);

                if (user == null) {
                    logger.LogWarning("404: Session valid but user not found (UserId: {UserId})", session.UserId);
                    return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Session is valid, but user not found" },
                        statusCode: 404);
                }

                logger.LogInformation("200: Session validated for user {UserId} ({Username})", user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.ValidateStateData> { data = new DTOs.ValidateStateData { user_id = session.UserId, user_name = user.Username, expiresAt = session.ExpiresAt } },
                    statusCode: 200);
            } catch (Exception ex) {
                logger.LogError("500: Exception during session validation: {Message}", ex.Message);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "An error occurred while validating the session" },
                    statusCode: 500);
            }
        });

        app.MapPut("/api/login", async (HttpRequest req, HttpResponse res, Sql.UsersDbContext usersDb, ILogger<Program> logger) => {
            if (!req.HasJsonContentType()) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Request must be of type application/json" },
                    statusCode: 415);
            }
            AnimeLoginRequest? body = await req.ReadFromJsonAsync<AnimeLoginRequest>();
            string? userlogin = body?._username?.Trim();
            string? password = body?._password;

            if (string.IsNullOrEmpty(userlogin) || string.IsNullOrEmpty(password)) {
                logger.LogInformation("400: User login or password missing");
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "User login and password must be provided" },
                    statusCode: 400);
            }

            Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == userlogin || u.Email == userlogin.ToLower());

            if (user == null || !helpers.AuthHelpers.VerifyPassword(password, user.PasswordHash)) {
                logger.LogInformation("400: Invalid login attempt for user login '{UserLogin}'", userlogin);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Your username or password was incorrect" },
                    statusCode: 200);
            }

            /* if (!user.IsActive) {
                logger.LogInformation("403: Login attempt for inactive account '{UserLogin}'", userlogin);
                return Results.Json(new {
                    status = "err",
                    msg = "You need to activate your account before you can log in."
                }, statusCode: 200);
            }*/

            // Statically trigger TFA for a specific test user. In a real application,
            // you would check a flag on the user object, like `if (user.IsTfaEnabled)`.
            if (user.Username.Equals("testy", StringComparison.OrdinalIgnoreCase)) {
                logger.LogInformation("400: TFA required for user login '{UserLogin}'", userlogin);
                return Results.Json(new DTOs.OkResponse<object> { data = new { tfa = true } },
                    statusCode: 400);
            }

            const Int32 MaxUserSessions = 10;
            string sessionToken = Guid.NewGuid().ToString();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            List<Models.UserSession> existingSessions = await usersDb.UserSessions
                .Where(s => s.UserId == user.UserId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            // Remove expired sessions from the database, not just the in-memory list.
            var expiredSessions = existingSessions.Where(s => s.ExpiresAt < now.DateTime).ToList();
            if (expiredSessions.Count > 0) {
                    usersDb.UserSessions.RemoveRange(expiredSessions);
                    // Also remove from the local list so counts below are accurate
                    existingSessions.RemoveAll(s => s.ExpiresAt < now.DateTime);
            }

            if (existingSessions.Count >= MaxUserSessions) {
                    Models.UserSession oldestSession = existingSessions[0];
                    usersDb.UserSessions.Remove(oldestSession);
            }

            Models.UserSession session = new Models.UserSession {
                UserId = user.UserId,
                SessionToken = sessionToken,
                CreatedAt = now.DateTime,
                ExpiresAt = now.AddHours(24).DateTime
            };

            usersDb.UserSessions.Add(session);
            await usersDb.SaveChangesAsync();

            string? platform = req.Headers["X-Platform"].FirstOrDefault();

            // Set the session token in a secure, HttpOnly cookie.
            // NOTE: `SameSite` controls whether the cookie is sent on cross-site requests.
            // - Use `SameSiteMode.None` + `Secure = true` when your API and frontend are on
            //   entirely different origins (e.g. api.example.com and example.com on different registrable domains).
            // - If your frontend is hosted on a sibling subdomain (e.g. app.example.com and api.example.com),
            //   prefer `SameSiteMode.Lax` (or `Strict`) for improved CSRF protection.
            // If you want to change this behavior based on deployment, consider making
            // the SameSite mode configurable via environment/config.
            res.Cookies.Append("session_token", sessionToken, new CookieOptions {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = session.ExpiresAt
            });

            if (!string.IsNullOrEmpty(platform) && platform == "web") {
                logger.LogInformation("200: Login success (web) for user {UserId} ({Username})", user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.LoginOkData> { data = new DTOs.LoginOkData { user_id = user.UserId, user_name = user.Username } });
            } else if (!string.IsNullOrEmpty(platform) && (platform == "android" || platform == "ios")) {
                logger.LogInformation("200: Login success ({Platform}) for user {UserId} ({Username})", platform, user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.LoginOkDataMobile> { data = new DTOs.LoginOkDataMobile { user_id = user.UserId, user_name = user.Username, session_token = sessionToken } });
            } else {
                logger.LogInformation("400: Unsupported platform '{Platform}'", platform);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Unsupported platform" },
                    statusCode: 400);
            }
        });
    }

    public class ValidateRequest {
        public string? token {
            get; set;
        }
    }

    public class SigninRequest {
        public string? apf {
            get; set;
        }
        public string? email {
            get; set;
        }
        public string? password {
            get; set;
        }
        public string? request {
            get; set;
        }
        public bool tos_pp_agree {
            get; set;
        }
        public string? username {
            get; set;
        }
    }

    public class AnimeLoginRequest {
        public string? _username {
            get; set;
        }
        public string? _password {
            get; set;
        }
        public bool _remember_me {
            get; set;
        }
    }
}
