// api/login.cs
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ASP.NETCoreWebApi;

public static class LoginEndpoints {

    public static void MapLoginEndpoints(this IEndpointRouteBuilder app) {

        app.MapPut("/api/login/validateState", async (HttpRequest req, UsersDbContext usersDb, ILogger<Program> logger) => {
            try {
                String? token = req.Cookies["session_token"];
                if (String.IsNullOrEmpty(token)) {
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
                if (String.IsNullOrEmpty(token)) {
                    String? authHeader = req.Headers["Authorization"];
                    if (!String.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ")) {
                        token = authHeader.Substring(7); // "Bearer ".Length
                    }
                }

                if (String.IsNullOrEmpty(token)) {
                    logger.LogInformation("401: Missing session token");
                    return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Missing session token" },
                        (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
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
                        (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                        statusCode: 401);
                }

                Models.User? user = await usersDb.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == session.UserId);

                if (user == null) {
                    logger.LogWarning("404: Session valid but user not found (UserId: {UserId})", session.UserId);
                    return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Session is valid, but user not found" },
                        (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                        statusCode: 404);
                }

                logger.LogInformation("200: Session validated for user {UserId} ({Username})", user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.ValidateStateData> { data = new DTOs.ValidateStateData { user_id = session.UserId, user_name = user.Username, expiresAt = session.ExpiresAt } },
                    (JsonTypeInfo<DTOs.OkResponse<DTOs.ValidateStateData>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.ValidateStateData>))!,
                    statusCode: 200);
            } catch (Exception ex) {
                logger.LogError("500: Exception during session validation: {Message}", ex.Message);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "An error occurred while validating the session" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 500);
            }
        });

        app.MapPut("/api/login", async (HttpRequest req, HttpResponse res, UsersDbContext usersDb, ILogger<Program> logger) => {
            if (!req.HasJsonContentType()) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Request must be of type application/json" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 415);
            }
            AnimeLoginRequest? body = await req.ReadFromJsonAsync<AnimeLoginRequest>();
            String? username = body?._username;
            String? password = body?._password;

            if (String.IsNullOrEmpty(username) || String.IsNullOrEmpty(password)) {
                logger.LogInformation("400: Username or password missing");
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username and password must be provided" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);

            if (user == null || !AuthHelpers.VerifyPassword(password, user.PasswordHash)) {
                logger.LogInformation("400: Invalid login attempt for username '{Username}'", username);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Your username or password was incorrect" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 200);
            }

            /* if (!user.IsActive) {
                logger.LogInformation("403: Login attempt for inactive account '{Username}'", username);
                return Results.Json(new {
                    status = "err",
                    msg = "You need to activate your account before you can log in."
                }, statusCode: 200);
            }*/

            // Statically trigger TFA for a specific test user. In a real application,
            // you would check a flag on the user object, like `if (user.IsTfaEnabled)`.
            if (user.Username.Equals("testy", StringComparison.OrdinalIgnoreCase)) {
                logger.LogInformation("400: TFA required for user '{Username}'", username);
                return Results.Json(new DTOs.OkResponse<object> { data = new { tfa = true } },
                    (JsonTypeInfo<DTOs.OkResponse<object>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<object>))!,
                    statusCode: 400);
            }

            const Int32 MaxUserSessions = 10;
            String sessionToken = Guid.NewGuid().ToString();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            List<Models.UserSession> existingSessions = await usersDb.UserSessions
              .Where(s => s.UserId == user.UserId)
              .OrderBy(s => s.CreatedAt)
              .ToListAsync();

            existingSessions.RemoveAll(s => s.ExpiresAt < now.DateTime);

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

            String? platform = req.Headers["X-Platform"].FirstOrDefault();

            // Set the session token in a secure, HttpOnly cookie
            res.Cookies.Append("session_token", sessionToken, new CookieOptions {
                HttpOnly = true,
                Secure = true,
                // Use SameSite=None to allow cross-site requests from a separate frontend origin
                SameSite = SameSiteMode.None,
                Expires = session.ExpiresAt
            });

            if (!String.IsNullOrEmpty(platform) && platform == "web") {
                logger.LogInformation("200: Login success (web) for user {UserId} ({Username})", user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.LoginOkData> { data = new DTOs.LoginOkData { user_id = user.UserId, user_name = user.Username } },
                    (JsonTypeInfo<DTOs.OkResponse<DTOs.LoginOkData>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.LoginOkData>))!);
            } else if (!String.IsNullOrEmpty(platform) && (platform == "android" || platform == "ios")) {
                logger.LogInformation("200: Login success ({Platform}) for user {UserId} ({Username})", platform, user.UserId, user.Username);
                return Results.Json(new DTOs.OkResponse<DTOs.LoginOkDataMobile> { data = new DTOs.LoginOkDataMobile { user_id = user.UserId, user_name = user.Username, session_token = sessionToken } },
                    (JsonTypeInfo<DTOs.OkResponse<DTOs.LoginOkDataMobile>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.LoginOkDataMobile>))!);
            } else {
                logger.LogInformation("400: Unsupported platform '{Platform}'", platform);
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Unsupported platform" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }
        });
    }

    public class ValidateRequest {
        public String? token {
            get; set;
        }
    }

    public class SigninRequest {
        public String? apf {
            get; set;
        }
        public String? email {
            get; set;
        }
        public String? password {
            get; set;
        }
        public String? request {
            get; set;
        }
        public Boolean tos_pp_agree {
            get; set;
        }
        public String? username {
            get; set;
        }
    }

    public class AnimeLoginRequest {
        public String? _username {
            get; set;
        }
        public String? _password {
            get; set;
        }
        public Boolean _remember_me {
            get; set;
        }
    }
}
