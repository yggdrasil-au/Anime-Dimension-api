// api/validation.cs

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System;
using Microsoft.EntityFrameworkCore;

namespace ASP.NETCoreWebApi;

public static class ValidationsEndpoints {

    public static void MapValidationEndpoints(this IEndpointRouteBuilder app) {

        // validate new users username input
        app.MapPost("/api/validation/username", async (HttpRequest req, Sql.UsersDbContext usersDb) => {

            UsernameRequest? body = await req.ReadFromJsonAsync<UsernameRequest>();
            string? username = body?.username;

            if (string.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9]+$")) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Only letters or numbers" },
                    statusCode: 400);
            }

            bool userExists = await usersDb.Users.AnyAsync(u => u.Username == username);

            if (userExists) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username is unavailable" },
                    statusCode: 400);
            } else {
                return Results.Json(new DTOs.OkResponse<string?> { data = null },
                    statusCode: 200);
            }
        });
    }

    public class UsernameRequest {
        public string? username {
            get; set;
        }
    }
}
