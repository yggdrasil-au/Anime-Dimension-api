// api/validation.cs

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ASP.NETCoreWebApi;

public static class ValidationsEndpoints {

    public static void MapValidationEndpoints(this IEndpointRouteBuilder app) {

        // validate new users username input
        app.MapPost("/api/validation/username", async (HttpRequest req, UsersDbContext usersDb) => {

            UsernameRequest? body = await req.ReadFromJsonAsync<UsernameRequest>();
            String? username = body?.username;

            if (String.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9]+$")) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Only letters or numbers" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            Boolean userExists = await usersDb.Users.AnyAsync(u => u.Username == username);

            if (userExists) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username is unavailable" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            } else {
                return Results.Json(new DTOs.OkResponse<string?> { data = null },
                    (JsonTypeInfo<DTOs.OkResponse<string?>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<string?>))!,
                    statusCode: 200);
            }
        });
    }

    public class UsernameRequest {
        public String? username {
            get; set;
        }
    }
}
