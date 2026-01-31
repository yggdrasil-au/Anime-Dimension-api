// api/auth.cs

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;


namespace ASP.NETCoreWebApi;

public static class AuthEndpoints {

    // Regex compiled for performance (Matches frontend logic)
    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex PasswordComplexityRegex = new(@"(?=.*[A-Za-z])(?=.*\d)", RegexOptions.Compiled);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app) {

        app.MapPost("/api/auth/signup-token", async (HttpRequest req) => {
            var body = await req.ReadFromJsonAsync<RecaptchaRequest>();
            // TODO: Validate body.recaptcha_response with Google API here if needed

            /*var payload = new DTOs.OkResponse<DTOs.SignupTokenData[]> {
                data = new[] { new DTOs.SignupTokenData { token = Guid.NewGuid().ToString(), duration = 3600 } }
            };*/

            var payload = new DTOs.OkResponse<DTOs.SignupTokenData[]>( ) { data = new[] { new DTOs.SignupTokenData { token = "16363f3d-1cb0-4ebd-9f38-f9034dc9c8b3", duration = 3600 } } };
            return Results.Json(payload, statusCode: 200);
        });

        app.MapPost("/api/auth/signup", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            SignupRequest? body = await req.ReadFromJsonAsync<SignupRequest>();

            if (body == null) return Results.BadRequest();

            string apf = body.apf ?? "";
            string email = (body.email ?? "").Trim().ToLowerInvariant(); // Normalize email
            string password = body.password ?? "";
            bool tos_pp_agree = body.tos_pp_agree;
            string username = (body.username ?? "").Trim();

            // --- 1. Input Validation (Must match Frontend) ---

            if (!tos_pp_agree) {
                return ErrorResult("You must agree to the terms of service and privacy policy.");
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) {
                return ErrorResult("Username, email, and password are required.");
            }

            if (!UsernameRegex.IsMatch(username)) {
                return ErrorResult("Username must be 3-20 characters and alphanumeric/underscore only.");
            }

            if (!EmailRegex.IsMatch(email)) {
                return ErrorResult("Invalid email format.");
            }

            if (password.Length < 3 || password.Length > 128) {
                return ErrorResult("Password must be between 3 and 128 characters.");
            }

            if (!PasswordComplexityRegex.IsMatch(password)) {
                return ErrorResult("Password must contain at least one letter and one number.");
            }

            // --- 2. Database Checks ---

            // Quick check (Optimization before hashing)
            if (await usersDb.Users.AnyAsync(u => u.Username == username || u.Email == email)) {
                return ErrorResult("Username or email already exists.");
            }

            // --- 3. Processing ---

            // Hash the password
            string passwordHash = helpers.AuthHelpers.HashPassword(password);

            // Generate unique User ID
            string userId = await GenerateUniqueUserIdAsync(usersDb);

            var newUser = new Models.User {
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                UserId = userId,
                CreatedAt = DateTime.UtcNow // Ensure you have this field or remove if not
            };

            try {
                usersDb.Users.Add(newUser);
                await usersDb.SaveChangesAsync();
            }
            catch (DbUpdateException) {
                // Catch race condition: Two users registered exact same name at exact same time
                // The DB unique constraint will throw here.
                return ErrorResult("Username or email already exists."); 
            }


            return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = null }, statusCode: 200);
        });
    }

    // --- Helper Methods ---

    private static IResult ErrorResult(string message) {
        return Results.Json(new DTOs.ErrResponse { status = "err", msg = message }, statusCode: 400);
    }

    private static async Task<string> GenerateUniqueUserIdAsync(Sql.UsersDbContext db) {
        // Generates a random numeric string between 6 and 10 digits
        // Uses crypto-secure RNG
        string userId;
        bool exists;
        do {
            // Generate a secure random integer
            int randomInt = RandomNumberGenerator.GetInt32(0, 1000000000);

            // Format to string, pad if necessary to meet minimum 6 digits
            // Note: Original logic was 6-10 digits.
            userId = randomInt.ToString();

            if (userId.Length < 6) {
                userId = userId.PadLeft(6, '0');
            }

            // Check collision
            exists = await db.Users.AnyAsync(u => u.UserId == userId);
        } while (exists);

        return userId;
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
