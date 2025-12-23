// api/TestEndpoints.cs
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;

using System;
using ASP.NETCoreWebApi.helpers;


namespace ASP.NETCoreWebApi;

public static class TestEndpoints {

    public static void MapTestEndpoints(this IEndpointRouteBuilder app) {

        app.MapGet("/test/mail", async (HttpRequest req, [FromServices] Sql.UsersDbContext usersDb, ILogger<Program> logger) => {
            try {

                string returnval = Mail.SendEmail(
                    toName: "Test User",
                    toEmail: "testuser@anime-dimension.com",
                    subject: "Test Email",
                    body: "This is a test email from Anime Dimension."
                );

                return Results.Json(returnval);
            } catch (Exception ex) {
                logger.LogError("500: Exception during session validation: {Message}", ex.Message);
                return Results.Json(data: null, statusCode: 500);
            }
        });

    }

}
