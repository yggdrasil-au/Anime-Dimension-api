// api/users.cs

using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using System.Linq;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ASP.NETCoreWebApi;

public static class UsersEndpoints {

    public static void MapUsersEndpoints(this IEndpointRouteBuilder app) {

        // GET /api/users/profile?username=foo
        app.MapGet("/api/users/profile", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            string uname = (req.Query["username"].ToString() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(uname) || !Regex.IsMatch(uname, @"^[a-zA-Z0-9]+$")) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Invalid or missing username" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == uname);
            if (user == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "User not found" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 404);
            }

            string avatarUrl = $"/images/users/avatars/{Uri.EscapeDataString(user.UserId)}.webp";
            string bannerUrl = $"/assets/images/users/backgrounds/{Uri.EscapeDataString(user.UserId)}.webp";

            // Ensure supporting tables exist
            await EnsureUserProfileTables(usersDb);

            // Gather profile extras (joined date, watch stats, ratings)
            string joinedIso = DateTime.UtcNow.ToString("o");
            long minutesWatched = 0;
            long watched = 0, watching = 0, want = 0, stalled = 0, dropped = 0, wont = 0;
            Dictionary<string, long> ratingsMap = new Dictionary<string, long> {
                ["5.0"] = 0,
                ["4.5"] = 0,
                ["4.0"] = 0,
                ["3.5"] = 0,
                ["3.0"] = 0,
                ["2.5"] = 0,
                ["2.0"] = 0,
                ["1.5"] = 0,
                ["1.0"] = 0,
                ["0.5"] = 0,
            };

            // Use a separate SQLite connection for raw operations
            await using (SqliteConnection conn = new SqliteConnection(usersDb.Database.GetDbConnection().ConnectionString)) {
                await conn.OpenAsync();

                // Ensure base rows exist
                await EnsureUserProfileRows(conn, user.UserId);

                // Read joined
                await using (SqliteCommand cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"SELECT JoinedAt FROM UserProfiles WHERE UserId = $uid LIMIT 1";
                    cmd.Parameters.AddWithValue("$uid", user.UserId);
                    Object? joinedObj = await cmd.ExecuteScalarAsync();
                    if (joinedObj != null && joinedObj != DBNull.Value)
                        joinedIso = Convert.ToString(joinedObj) ?? joinedIso;
                }

                // Read watch stats
                await using (SqliteCommand cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"SELECT MinutesWatched, Watched, Watching, Want, Stalled, Dropped, Wont FROM UserWatchStats WHERE UserId = $uid LIMIT 1";
                    cmd.Parameters.AddWithValue("$uid", user.UserId);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        minutesWatched = reader.GetInt64(0);
                        watched = reader.GetInt64(1);
                        watching = reader.GetInt64(2);
                        want = reader.GetInt64(3);
                        stalled = reader.GetInt64(4);
                        dropped = reader.GetInt64(5);
                        wont = reader.GetInt64(6);
                    }
                }

                // Read ratings histogram
                await using (SqliteCommand cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"SELECT R50, R45, R40, R35, R30, R25, R20, R15, R10, R05 FROM UserRatings WHERE UserId = $uid LIMIT 1";
                    cmd.Parameters.AddWithValue("$uid", user.UserId);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        ratingsMap["5.0"] = reader.GetInt64(0);
                        ratingsMap["4.5"] = reader.GetInt64(1);
                        ratingsMap["4.0"] = reader.GetInt64(2);
                        ratingsMap["3.5"] = reader.GetInt64(3);
                        ratingsMap["3.0"] = reader.GetInt64(4);
                        ratingsMap["2.5"] = reader.GetInt64(5);
                        ratingsMap["2.0"] = reader.GetInt64(6);
                        ratingsMap["1.5"] = reader.GetInt64(7);
                        ratingsMap["1.0"] = reader.GetInt64(8);
                        ratingsMap["0.5"] = reader.GetInt64(9);
                    }
                }
            }

            long ratingsTotal = ratingsMap.Values.Aggregate(0L, (acc, v) => acc + v);

            DTOs.UserProfileData profile = new DTOs.UserProfileData {
                user_id = user.UserId,
                user_name = user.Username,
                avatar_url = avatarUrl,
                banner_url = bannerUrl,
                joined_at = joinedIso,
                watch_minutes = minutesWatched,
                stats = new DTOs.UserStatsData {
                    watched = watched, watching = watching, want = want, stalled = stalled, dropped = dropped, wont = wont
                },
                ratings = ratingsMap,
                ratings_total = ratingsTotal
            };
            return Results.Json(new DTOs.OkResponse<DTOs.UserProfileData> { data = profile },
                (JsonTypeInfo<DTOs.OkResponse<DTOs.UserProfileData>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.UserProfileData>))!);
        });

        // GET /api/users/me
        app.MapGet("/api/users/me", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            string? token = req.Cookies["session_token"];
            if (string.IsNullOrEmpty(token)) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Not authenticated" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 401);
            }

            Models.UserSession? session = await usersDb.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.SessionToken == token);
            if (session == null || session.ExpiresAt < DateTime.UtcNow) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Invalid or expired session" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 401);
            }

            Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == session.UserId);
            if (user == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "User not found" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 404);
            }

            string avatarUrl = $"/images/users/avatars/{Uri.EscapeDataString(user.UserId)}.webp";
            string bannerUrl = $"/assets/images/users/backgrounds/{Uri.EscapeDataString(user.UserId)}.webp";

            DTOs.UserMeData me = new DTOs.UserMeData { user_id = user.UserId, user_name = user.Username, avatar_url = avatarUrl, banner_url = bannerUrl };
            return Results.Json(new DTOs.OkResponse<DTOs.UserMeData> { data = me },
                (JsonTypeInfo<DTOs.OkResponse<DTOs.UserMeData>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<DTOs.UserMeData>))!);
        });

        // POST /api/users/me/avatar (multipart/form-data, field: avatar)
        app.MapPost("/api/users/me/avatar", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            Models.User? me = await ResolveUserFromSession(req, usersDb);
            if (me == null)
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Not authenticated" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 401);

            IFormCollection form = await req.ReadFormAsync();
            IFormFile? file = form.Files["avatar"];
            if (file == null || file.Length == 0) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Missing avatar file" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            // Save to /images/users/avatars/{user_id}.webp (relative to working directory)
            string outDir = Path.Combine("images", "users", "avatars");
            Directory.CreateDirectory(outDir);
            string outPathWebp = Path.Combine(outDir, $"{me.UserId}.webp");

            using (FileStream stream = File.Create(outPathWebp)) {
                await file.CopyToAsync(stream);
            }

            string avatarUrl = $"/images/users/avatars/{Uri.EscapeDataString(me.UserId)}.webp";
            return Results.Json(new DTOs.OkResponse<object> { data = new { avatar_url = avatarUrl } },
                (JsonTypeInfo<DTOs.OkResponse<object>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<object>))!);
        });

        // POST /api/users/me/banner (multipart/form-data, field: banner)
        app.MapPost("/api/users/me/banner", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            Models.User? me = await ResolveUserFromSession(req, usersDb);
            if (me == null)
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Not authenticated" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 401);

            IFormCollection form = await req.ReadFormAsync();
            IFormFile? file = form.Files["banner"];
            if (file == null || file.Length == 0) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Missing banner file" },
                    (JsonTypeInfo<DTOs.ErrResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.ErrResponse))!,
                    statusCode: 400);
            }

            // Save to /assets/images/users/backgrounds/{user_id}.webp (relative to working directory)
            string outDir = Path.Combine("assets", "images", "users", "backgrounds");
            Directory.CreateDirectory(outDir);
            string outPathWebp = Path.Combine(outDir, $"{me.UserId}.webp");

            using (FileStream stream = File.Create(outPathWebp)) {
                await file.CopyToAsync(stream);
            }

            string bannerUrl = $"/assets/images/users/backgrounds/{Uri.EscapeDataString(me.UserId)}.webp";
            return Results.Json(new DTOs.OkResponse<object> { data = new { banner_url = bannerUrl } },
                (JsonTypeInfo<DTOs.OkResponse<object>>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.OkResponse<object>))!);
        });
    }

    private static async Task<ASP.NETCoreWebApi.Models.User?> ResolveUserFromSession(HttpRequest req, Sql.UsersDbContext usersDb) {
        string? token = req.Cookies["session_token"];
        if (string.IsNullOrEmpty(token))
            return null;
        Models.UserSession? session = await usersDb.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.SessionToken == token);
        if (session == null || session.ExpiresAt < DateTime.UtcNow)
            return null;
        Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == session.UserId);
        return user;
    }
    private static async Task EnsureUserProfileTables(Sql.UsersDbContext usersDb) {
        string createProfiles = @"CREATE TABLE IF NOT EXISTS UserProfiles (
            UserId TEXT PRIMARY KEY,
            JoinedAt TEXT NOT NULL
        );";

        string createWatchStats = @"CREATE TABLE IF NOT EXISTS UserWatchStats (
            UserId TEXT PRIMARY KEY,
            MinutesWatched INTEGER NOT NULL DEFAULT 0,
            Watched INTEGER NOT NULL DEFAULT 0,
            Watching INTEGER NOT NULL DEFAULT 0,
            Want INTEGER NOT NULL DEFAULT 0,
            Stalled INTEGER NOT NULL DEFAULT 0,
            Dropped INTEGER NOT NULL DEFAULT 0,
            Wont INTEGER NOT NULL DEFAULT 0
        );";

        string createRatings = @"CREATE TABLE IF NOT EXISTS UserRatings (
            UserId TEXT PRIMARY KEY,
            R50 INTEGER NOT NULL DEFAULT 0,
            R45 INTEGER NOT NULL DEFAULT 0,
            R40 INTEGER NOT NULL DEFAULT 0,
            R35 INTEGER NOT NULL DEFAULT 0,
            R30 INTEGER NOT NULL DEFAULT 0,
            R25 INTEGER NOT NULL DEFAULT 0,
            R20 INTEGER NOT NULL DEFAULT 0,
            R15 INTEGER NOT NULL DEFAULT 0,
            R10 INTEGER NOT NULL DEFAULT 0,
            R05 INTEGER NOT NULL DEFAULT 0
        );";

        await usersDb.Database.ExecuteSqlRawAsync(createProfiles);
        await usersDb.Database.ExecuteSqlRawAsync(createWatchStats);
        await usersDb.Database.ExecuteSqlRawAsync(createRatings);
    }

    private static async Task EnsureUserProfileRows(SqliteConnection conn, string userId) {
        // Profiles
        await using (SqliteCommand cmd = conn.CreateCommand()) {
            cmd.CommandText = @"INSERT INTO UserProfiles (UserId, JoinedAt)
                                SELECT $uid, $joined
                                WHERE NOT EXISTS (SELECT 1 FROM UserProfiles WHERE UserId = $uid);";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$joined", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Watch stats
        await using (SqliteCommand cmd = conn.CreateCommand()) {
            cmd.CommandText = @"INSERT INTO UserWatchStats (UserId, MinutesWatched, Watched, Watching, Want, Stalled, Dropped, Wont)
                                SELECT $uid, 0, 0, 0, 0, 0, 0, 0
                                WHERE NOT EXISTS (SELECT 1 FROM UserWatchStats WHERE UserId = $uid);";
            cmd.Parameters.AddWithValue("$uid", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Ratings
        await using (SqliteCommand cmd = conn.CreateCommand()) {
            cmd.CommandText = @"INSERT INTO UserRatings (UserId, R50, R45, R40, R35, R30, R25, R20, R15, R10, R05)
                                SELECT $uid, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
                                WHERE NOT EXISTS (SELECT 1 FROM UserRatings WHERE UserId = $uid);";
            cmd.Parameters.AddWithValue("$uid", userId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
