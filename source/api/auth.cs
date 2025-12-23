// api/auth.cs

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using ASP.NETCoreWebApi.Serialization;


namespace ASP.NETCoreWebApi;

public static class AuthEndpoints {

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app) {

        app.MapPost("/api/auth/signup-token", async (HttpRequest req) => {
            RecaptchaRequest? body = await req.ReadFromJsonAsync<RecaptchaRequest>();
            string? recaptcha_response = body?.recaptcha_response;

            // You can validate the recaptcha_response here

            var payload = new DTOs.OkResponse<DTOs.SignupTokenData[]>( ) { data = new[] { new DTOs.SignupTokenData { token = "16363f3d-1cb0-4ebd-9f38-f9034dc9c8b3", duration = 3600 } } };
            return Results.Json(payload,
                (JsonTypeInfo<DTOs.OkResponse<DTOs.SignupTokenData[]>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.SignupTokenData[]>))!,
                statusCode: 200);
        });

        app.MapPost("/api/auth/signup", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            SignupRequest? body = await req.ReadFromJsonAsync<SignupRequest>();

            string? apf = body?.apf;
            string? email = body?.email?.Trim().ToLower();
            string? password = body?.password;
            string? request = body?.request;
            bool tos_pp_agree = body?.tos_pp_agree ?? false;
            string? username = body?.username?.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username, email, and password are required" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            if (!tos_pp_agree) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "You must agree to the terms of service and privacy policy" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            // Check if username or email already exists
            bool userExists = await usersDb.Users.AnyAsync(u => u.Username == username || u.Email == email);
            if (userExists) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username or email already exists" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            // Hash the password
            string passwordHash = helpers.AuthHelpers.HashPassword(password);

            // Create new user entity
            // Generate a numeric userId with length between 6 and 10 and ensure uniqueness
            Random random = new Random();
            string userId;
            do {
                userId = random.Next(0, 1000000000).ToString("D10").TrimStart('0');
                if (userId.Length < 6)
                    userId = userId.PadLeft(6, '0');
                if (userId.Length > 10)
                    userId = userId.Substring(0, 10);
            } while (await usersDb.Users.AnyAsync(u => u.UserId == userId));

            Models.User newUser = new Models.User {
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                UserId = userId
            };

            usersDb.Users.Add(newUser);
            await usersDb.SaveChangesAsync();

            return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = null },
                (JsonTypeInfo<DTOs.SimpleStatusResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SimpleStatusResponse))!,
                statusCode: 200);
        });
    }

    public class RecaptchaRequest {
        public string? recaptcha_response {
            get; set;
        }
    }

    public class SignupRequest {
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
}
