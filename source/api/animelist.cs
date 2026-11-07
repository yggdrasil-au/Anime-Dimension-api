// api/animelist.cs

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace ASP.NETCoreWebApi;

public static class AnimeListEndpoints {

    public static void MapAnimeListEndpoints(this IEndpointRouteBuilder app) {

        // GET /api/users/{username}/animelist
        app.MapGet("/api/users/{username}/animelist", async (string username, HttpRequest req, Sql.UsersDbContext usersDb, AnimeDbOptions animeDb) => {
            string uname = (username ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(uname)) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Username is required" }, statusCode: 400);
            }

            Models.User? user = await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == uname.ToLower());
            if (user == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "User not found" }, statusCode: 404);
            }

            await EnsureAnimeListTableExists(usersDb);

            string statusParam = (req.Query["status"].ToString() ?? "all").Trim().ToLowerInvariant();
            string sortParam = (req.Query["sort"].ToString() ?? "user_updated").Trim().ToLowerInvariant();
            string orderParam = (req.Query["order"].ToString() ?? "desc").Trim().ToLowerInvariant();
            string letterParam = (req.Query["letter"].ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string searchParam = (req.Query["search"].ToString() ?? string.Empty).Trim().ToLowerInvariant();

            int statusFilter = ParseStatusFilter(statusParam);

            // Fetch user list entries
            var query = usersDb.UserAnimeLists.AsNoTracking().Where(e => e.UserId == user.UserId);
            if (statusFilter > 0) {
                query = query.Where(e => e.Status == statusFilter);
            }

            List<Models.UserAnimeListEntry> userEntries = await query.ToListAsync();
            if (userEntries.Count == 0) {
                return Results.Json(new DTOs.OkResponse<AnimeListResponseData> {
                    data = new AnimeListResponseData {
                        user_name = user.Username,
                        user_id = user.UserId,
                        total_count = 0,
                        filter_status = statusParam,
                        items = new List<AnimeListItemDto>()
                    }
                }, statusCode: 200);
            }

            // Query Anime metadata from anime-dimension.sqlite3
            Dictionary<string, AnimeDbMeta> animeMap = new Dictionary<string, AnimeDbMeta>(StringComparer.OrdinalIgnoreCase);
            try {
                using var conn = new SqliteConnection($"Data Source={animeDb.DbPath}");
                await conn.OpenAsync();

                List<string> slugs = userEntries.Select(e => e.AnimeSlug).Distinct().ToList();
                foreach (string slug in slugs) {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT id, slug, title, alt_title, thumbnail_url, type, year, data_total_episodes FROM anime WHERE slug = $s LIMIT 1";
                    cmd.Parameters.AddWithValue("$s", slug);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        animeMap[slug] = new AnimeDbMeta {
                            Id = reader.GetInt32(0),
                            Slug = reader.GetString(1),
                            Title = reader.IsDBNull(2) ? slug : reader.GetString(2),
                            AltTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ThumbnailUrl = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Type = reader.IsDBNull(5) ? "TV" : reader.GetString(5),
                            Year = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            TotalEpisodes = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
                        };
                    }
                }
            } catch {
                // Ignore DB read failure on meta, fallback to slug display
            }

            List<AnimeListItemDto> items = new List<AnimeListItemDto>();

            foreach (var entry in userEntries) {
                animeMap.TryGetValue(entry.AnimeSlug, out AnimeDbMeta? meta);

                string title = meta?.Title ?? entry.AnimeSlug.Replace("-", " ");
                string altTitle = meta?.AltTitle ?? "";
                string type = meta?.Type ?? "TV";
                string year = meta?.Year ?? "";
                int totalEps = meta?.TotalEpisodes ?? 0;
                string thumb = meta?.ThumbnailUrl ?? "";

                // Letter filter
                if (!string.IsNullOrEmpty(letterParam)) {
                    if (letterParam == "#" || letterParam == "9") {
                        if (char.IsLetter(title.FirstOrDefault())) continue;
                    } else if (letterParam.Length == 1) {
                        if (char.ToLowerInvariant(title.FirstOrDefault()) != letterParam[0]) continue;
                    }
                }

                // Search query filter
                if (!string.IsNullOrEmpty(searchParam)) {
                    if (!title.Contains(searchParam, StringComparison.OrdinalIgnoreCase) &&
                        !altTitle.Contains(searchParam, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                }

                items.Add(new AnimeListItemDto {
                    id = entry.Id,
                    anime_id = entry.AnimeId > 0 ? entry.AnimeId : (meta?.Id ?? 0),
                    slug = entry.AnimeSlug,
                    title = title,
                    alt_title = altTitle,
                    thumbnail_url = thumb,
                    type = type,
                    year = year,
                    total_episodes = totalEps,
                    community_rating = 0.0,
                    status = entry.Status,
                    status_name = StatusToName(entry.Status),
                    episodes_watched = entry.EpisodesWatched,
                    user_rating = entry.Rating,
                    times_watched = entry.TimesWatched,
                    notes = entry.Notes,
                    updated_at = entry.UpdatedAt.ToString("o")
                });
            }

            // Sorting
            bool isDesc = orderParam == "desc";
            items = sortParam switch {
                "title" => isDesc ? items.OrderByDescending(i => i.title).ToList() : items.OrderBy(i => i.title).ToList(),
                "rating" => isDesc ? items.OrderByDescending(i => i.user_rating ?? -1).ThenBy(i => i.title).ToList() : items.OrderBy(i => i.user_rating ?? 99).ThenBy(i => i.title).ToList(),
                "year" => isDesc ? items.OrderByDescending(i => i.year).ToList() : items.OrderBy(i => i.year).ToList(),
                "eps" => isDesc ? items.OrderByDescending(i => i.episodes_watched).ToList() : items.OrderBy(i => i.episodes_watched).ToList(),
                _ => isDesc ? items.OrderByDescending(i => i.updated_at).ToList() : items.OrderBy(i => i.updated_at).ToList()
            };

            return Results.Json(new DTOs.OkResponse<AnimeListResponseData> {
                data = new AnimeListResponseData {
                    user_name = user.Username,
                    user_id = user.UserId,
                    total_count = items.Count,
                    filter_status = statusParam,
                    items = items
                }
            }, statusCode: 200);
        });

        // GET /api/users/me/animelist/{slug}
        app.MapGet("/api/users/me/animelist/{slug}", async (string slug, HttpRequest req, Sql.UsersDbContext usersDb) => {
            Models.User? me = await ResolveUserFromSession(req, usersDb);
            if (me == null) {
                return Results.Json(new { status = "ok", authenticated = false, data = (object?)null });
            }

            await EnsureAnimeListTableExists(usersDb);

            var entry = await usersDb.UserAnimeLists.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == me.UserId && e.AnimeSlug == slug);

            if (entry == null) {
                return Results.Json(new { status = "ok", authenticated = true, data = (object?)null });
            }

            return Results.Json(new {
                status = "ok",
                authenticated = true,
                data = new {
                    id = entry.Id,
                    anime_id = entry.AnimeId,
                    slug = entry.AnimeSlug,
                    status = entry.Status,
                    status_name = StatusToName(entry.Status),
                    episodes_watched = entry.EpisodesWatched,
                    user_rating = entry.Rating,
                    times_watched = entry.TimesWatched,
                    notes = entry.Notes,
                    updated_at = entry.UpdatedAt.ToString("o")
                }
            });
        });

        // POST /api/users/me/animelist
        app.MapPost("/api/users/me/animelist", async (HttpRequest req, Sql.UsersDbContext usersDb, AnimeDbOptions animeDb) => {
            Models.User? me = await ResolveUserFromSession(req, usersDb);
            if (me == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Not authenticated" }, statusCode: 401);
            }

            await EnsureAnimeListTableExists(usersDb);

            AnimeListUpdateRequest? body = await req.ReadFromJsonAsync<AnimeListUpdateRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.slug)) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Missing anime slug" }, statusCode: 400);
            }

            string cleanSlug = body.slug.Trim().ToLowerInvariant();
            int newStatus = body.status;

            // If status is 0, remove entry
            if (newStatus == 0) {
                var existing = await usersDb.UserAnimeLists
                    .FirstOrDefaultAsync(e => e.UserId == me.UserId && e.AnimeSlug == cleanSlug);
                if (existing != null) {
                    usersDb.UserAnimeLists.Remove(existing);
                    await usersDb.SaveChangesAsync();
                    await RecalculateUserStatsAsync(usersDb, me.UserId);
                }
                return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = "Removed" }, statusCode: 200);
            }

            if (newStatus < 1 || newStatus > 6) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Invalid status. Must be 1 to 6." }, statusCode: 400);
            }

            int animeId = body.anime_id;
            if (animeId <= 0) {
                try {
                    using var conn = new SqliteConnection($"Data Source={animeDb.DbPath}");
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT id FROM anime WHERE slug = $s LIMIT 1";
                    cmd.Parameters.AddWithValue("$s", cleanSlug);
                    var obj = await cmd.ExecuteScalarAsync();
                    if (obj != null && obj != DBNull.Value) animeId = Convert.ToInt32(obj);
                } catch { /* ignore */ }
            }

            var entry = await usersDb.UserAnimeLists
                .FirstOrDefaultAsync(e => e.UserId == me.UserId && e.AnimeSlug == cleanSlug);

            DateTime now = DateTime.UtcNow;

            if (entry == null) {
                entry = new Models.UserAnimeListEntry {
                    UserId = me.UserId,
                    AnimeId = animeId,
                    AnimeSlug = cleanSlug,
                    Status = newStatus,
                    EpisodesWatched = Math.Max(0, body.episodes_watched ?? 0),
                    Rating = body.rating > 0 ? Math.Round(body.rating.Value * 2) / 2 : null,
                    TimesWatched = Math.Max(1, body.times_watched ?? 1),
                    Notes = body.notes,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                usersDb.UserAnimeLists.Add(entry);
            } else {
                entry.Status = newStatus;
                if (animeId > 0) entry.AnimeId = animeId;
                if (body.episodes_watched.HasValue) entry.EpisodesWatched = Math.Max(0, body.episodes_watched.Value);
                if (body.rating.HasValue) {
                    entry.Rating = body.rating.Value > 0 ? Math.Round(body.rating.Value * 2) / 2 : null;
                }
                if (body.times_watched.HasValue) entry.TimesWatched = Math.Max(1, body.times_watched.Value);
                if (body.notes != null) entry.Notes = body.notes;
                entry.UpdatedAt = now;
            }

            await usersDb.SaveChangesAsync();
            await RecalculateUserStatsAsync(usersDb, me.UserId);

            return Results.Json(new {
                status = "ok",
                data = new {
                    id = entry.Id,
                    anime_id = entry.AnimeId,
                    slug = entry.AnimeSlug,
                    status = entry.Status,
                    status_name = StatusToName(entry.Status),
                    episodes_watched = entry.EpisodesWatched,
                    user_rating = entry.Rating,
                    times_watched = entry.TimesWatched,
                    notes = entry.Notes,
                    updated_at = entry.UpdatedAt.ToString("o")
                }
            }, statusCode: 200);
        });

        // DELETE /api/users/me/animelist/{slug}
        app.MapDelete("/api/users/me/animelist/{slug}", async (string slug, HttpRequest req, Sql.UsersDbContext usersDb) => {
            Models.User? me = await ResolveUserFromSession(req, usersDb);
            if (me == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Not authenticated" }, statusCode: 401);
            }

            await EnsureAnimeListTableExists(usersDb);

            string cleanSlug = slug.Trim().ToLowerInvariant();
            var existing = await usersDb.UserAnimeLists
                .FirstOrDefaultAsync(e => e.UserId == me.UserId && e.AnimeSlug == cleanSlug);

            if (existing != null) {
                usersDb.UserAnimeLists.Remove(existing);
                await usersDb.SaveChangesAsync();
                await RecalculateUserStatsAsync(usersDb, me.UserId);
            }

            return Results.Json(new DTOs.SimpleStatusResponse { status = "ok", data = "Removed" }, statusCode: 200);
        });
    }

    private static int ParseStatusFilter(string status) {
        return status switch {
            "watched" or "1" => 1,
            "watching" or "2" => 2,
            "dropped" or "3" => 3,
            "want" or "wanttowatch" or "4" => 4,
            "stalled" or "5" => 5,
            "wont" or "wontwatch" or "6" => 6,
            _ => 0
        };
    }

    public static string StatusToName(int status) {
        return status switch {
            1 => "watched",
            2 => "watching",
            3 => "dropped",
            4 => "want to watch",
            5 => "stalled",
            6 => "won't watch",
            _ => "unknown"
        };
    }

    public static async Task<Models.User?> ResolveUserFromSession(HttpRequest req, Sql.UsersDbContext usersDb) {
        string? token = req.Cookies["session_token"];
        if (string.IsNullOrEmpty(token)) {
            string? authHeader = req.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                token = authHeader.Substring(7).Trim();
            }
        }
        if (string.IsNullOrEmpty(token) && req.Query.TryGetValue("session_token", out var qSessionToken)) {
            token = qSessionToken.ToString();
        }
        if (string.IsNullOrEmpty(token)) return null;

        Models.UserSession? session = await usersDb.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.SessionToken == token);
        if (session == null || session.ExpiresAt < DateTime.UtcNow) return null;

        return await usersDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == session.UserId);
    }

    private static async Task EnsureAnimeListTableExists(Sql.UsersDbContext usersDb) {
        string createTable = @"CREATE TABLE IF NOT EXISTS UserAnimeList (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NOT NULL,
            AnimeId INTEGER NOT NULL DEFAULT 0,
            AnimeSlug TEXT NOT NULL,
            Status INTEGER NOT NULL,
            EpisodesWatched INTEGER NOT NULL DEFAULT 0,
            Rating REAL,
            TimesWatched INTEGER NOT NULL DEFAULT 1,
            Notes TEXT,
            CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(UserId, AnimeSlug)
        );
        CREATE INDEX IF NOT EXISTS idx_useranimelist_user ON UserAnimeList(UserId, Status);";

        await usersDb.Database.ExecuteSqlRawAsync(createTable);
    }

    public static async Task RecalculateUserStatsAsync(Sql.UsersDbContext usersDb, string userId) {
        var list = await usersDb.UserAnimeLists.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();

        long watched = list.Count(e => e.Status == 1);
        long watching = list.Count(e => e.Status == 2);
        long dropped = list.Count(e => e.Status == 3);
        long want = list.Count(e => e.Status == 4);
        long stalled = list.Count(e => e.Status == 5);
        long wont = list.Count(e => e.Status == 6);

        long totalMinutes = list.Sum(e => (long)e.EpisodesWatched * 24L);

        await usersDb.Database.ExecuteSqlRawAsync(@"
            INSERT INTO UserWatchStats (UserId, MinutesWatched, Watched, Watching, Want, Stalled, Dropped, Wont)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})
            ON CONFLICT(UserId) DO UPDATE SET
                MinutesWatched = excluded.MinutesWatched,
                Watched = excluded.Watched,
                Watching = excluded.Watching,
                Want = excluded.Want,
                Stalled = excluded.Stalled,
                Dropped = excluded.Dropped,
                Wont = excluded.Wont;",
            userId, totalMinutes, watched, watching, want, stalled, dropped, wont);

        long r50 = list.Count(e => e.Rating == 5.0);
        long r45 = list.Count(e => e.Rating == 4.5);
        long r40 = list.Count(e => e.Rating == 4.0);
        long r35 = list.Count(e => e.Rating == 3.5);
        long r30 = list.Count(e => e.Rating == 3.0);
        long r25 = list.Count(e => e.Rating == 2.5);
        long r20 = list.Count(e => e.Rating == 2.0);
        long r15 = list.Count(e => e.Rating == 1.5);
        long r10 = list.Count(e => e.Rating == 1.0);
        long r05 = list.Count(e => e.Rating == 0.5);

        await usersDb.Database.ExecuteSqlRawAsync(@"
            INSERT INTO UserRatings (UserId, R50, R45, R40, R35, R30, R25, R20, R15, R10, R05)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})
            ON CONFLICT(UserId) DO UPDATE SET
                R50 = excluded.R50, R45 = excluded.R45, R40 = excluded.R40, R35 = excluded.R35, R30 = excluded.R30,
                R25 = excluded.R25, R20 = excluded.R20, R15 = excluded.R15, R10 = excluded.R10, R05 = excluded.R05;",
            userId, r50, r45, r40, r35, r30, r25, r20, r15, r10, r05);
    }

    private class AnimeDbMeta {
        public int Id { get; set; }
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string AltTitle { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public string Type { get; set; } = "TV";
        public string Year { get; set; } = "";
        public int TotalEpisodes { get; set; }
    }

    public class AnimeListUpdateRequest {
        public string? slug { get; set; }
        public int anime_id { get; set; }
        public int status { get; set; }
        public int? episodes_watched { get; set; }
        public double? rating { get; set; }
        public int? times_watched { get; set; }
        public string? notes { get; set; }
    }

    public class AnimeListResponseData {
        public string user_name { get; set; } = "";
        public string user_id { get; set; } = "";
        public int total_count { get; set; }
        public string filter_status { get; set; } = "all";
        public List<AnimeListItemDto> items { get; set; } = new();
    }

    public class AnimeListItemDto {
        public int id { get; set; }
        public int anime_id { get; set; }
        public string slug { get; set; } = "";
        public string title { get; set; } = "";
        public string alt_title { get; set; } = "";
        public string thumbnail_url { get; set; } = "";
        public string type { get; set; } = "TV";
        public string year { get; set; } = "";
        public int total_episodes { get; set; }
        public double community_rating { get; set; }
        public int status { get; set; }
        public string status_name { get; set; } = "";
        public int episodes_watched { get; set; }
        public double? user_rating { get; set; }
        public int times_watched { get; set; }
        public string? notes { get; set; }
        public string updated_at { get; set; } = "";
    }
}
