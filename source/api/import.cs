// api/import.cs

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ASP.NETCoreWebApi;

public static class ImportEndpoints {

    public static void MapImportEndpoints(this IEndpointRouteBuilder app) {

        // POST /api/users/me/import/preview
        app.MapPost("/api/users/me/import/preview", async (HttpRequest req, Sql.UsersDbContext usersDb, AnimeDbOptions animeDb) => {
            var me = await AnimeListEndpoints.ResolveUserFromSession(req, usersDb);
            if (me == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Authentication required" }, statusCode: 401);
            }

            JsonDocument doc;
            try {
                doc = await JsonDocument.ParseAsync(req.Body);
            } catch (Exception ex) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = $"Invalid JSON payload: {ex.Message}" }, statusCode: 400);
            }

            JsonElement root = doc.RootElement;
            JsonElement entriesElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out JsonElement eArr) && eArr.ValueKind == JsonValueKind.Array) {
                entriesElement = eArr;
            } else if (root.ValueKind == JsonValueKind.Array) {
                entriesElement = root;
            } else {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "JSON missing 'entries' array" }, statusCode: 400);
            }

            // Existing tracked slugs for this user
            var existingSlugs = new HashSet<string>(
                await usersDb.UserAnimeLists.AsNoTracking()
                    .Where(e => e.UserId == me.UserId)
                    .Select(e => e.AnimeSlug.ToLowerInvariant())
                    .ToListAsync()
            );

            var matchedList = new List<object>();
            var unmatchedList = new List<object>();

            await using var conn = new SqliteConnection($"Data Source={animeDb.DbPath}");
            await conn.OpenAsync();

            // Prepared queries for fast title/slug resolution
            // 1. ApAnMap (exact AP primary or alternate title / slug)
            await using var apMapCmd = conn.CreateCommand();
            apMapCmd.CommandText = @"
                SELECT a.id, a.slug, a.title, a.thumbnail_url, a.type, a.year, a.data_total_episodes
                FROM ApAnMap m
                JOIN anime a ON a.id = m.AnId
                WHERE m.ApTitle = $name COLLATE NOCASE OR m.ApSlug = $slug
                LIMIT 1;";
            apMapCmd.Parameters.Add("$name", SqliteType.Text);
            apMapCmd.Parameters.Add("$slug", SqliteType.Text);

            // 2. Exact Title on anime table
            await using var exactTitleCmd = conn.CreateCommand();
            exactTitleCmd.CommandText = @"
                SELECT id, slug, title, thumbnail_url, type, year, data_total_episodes
                FROM anime
                WHERE title = $name COLLATE NOCASE
                LIMIT 1;";
            exactTitleCmd.Parameters.Add("$name", SqliteType.Text);

            // 3. Exact Slug on anime table
            await using var exactSlugCmd = conn.CreateCommand();
            exactSlugCmd.CommandText = @"
                SELECT id, slug, title, thumbnail_url, type, year, data_total_episodes
                FROM anime
                WHERE slug = $slug
                LIMIT 1;";
            exactSlugCmd.Parameters.Add("$slug", SqliteType.Text);

            // 4. Alt Title substring match
            await using var altTitleCmd = conn.CreateCommand();
            altTitleCmd.CommandText = @"
                SELECT id, slug, title, thumbnail_url, type, year, data_total_episodes
                FROM anime
                WHERE alt_title LIKE $nameLike
                LIMIT 1;";
            altTitleCmd.Parameters.Add("$nameLike", SqliteType.Text);

            int totalCount = 0;
            int alreadyTrackedCount = 0;

            foreach (var item in entriesElement.EnumerateArray()) {
                totalCount++;

                string name = "";
                if (item.TryGetProperty("name", out var pName) && pName.ValueKind == JsonValueKind.String) {
                    name = (pName.GetString() ?? "").Trim();
                } else if (item.TryGetProperty("title", out var pTitle) && pTitle.ValueKind == JsonValueKind.String) {
                    name = (pTitle.GetString() ?? "").Trim();
                }

                if (string.IsNullOrEmpty(name)) continue;

                string rawStatus = "";
                if (item.TryGetProperty("status", out var pStatus)) {
                    rawStatus = (pStatus.GetString() ?? "").Trim().ToLowerInvariant();
                }
                int status = ParseImportStatus(rawStatus);

                int eps = 0;
                if (item.TryGetProperty("eps", out var pEps) && pEps.TryGetInt32(out int eVal)) {
                    eps = Math.Max(0, eVal);
                } else if (item.TryGetProperty("episodes", out var pEp2) && pEp2.TryGetInt32(out int eVal2)) {
                    eps = Math.Max(0, eVal2);
                }

                double? rating = null;
                if (item.TryGetProperty("rating", out var pRating) && pRating.TryGetDouble(out double rVal) && rVal > 0) {
                    rating = Math.Min(5.0, Math.Max(0.5, Math.Round(rVal * 2.0) / 2.0));
                }

                int times = 1;
                if (item.TryGetProperty("times", out var pTimes) && pTimes.TryGetInt32(out int tVal) && tVal > 0) {
                    times = tVal;
                }

                string slugAttempt = Slugify(name);

                // Execute Matching Pipeline
                MatchedAnimeRecord? matched = null;

                // Pass 1: ApAnMap
                apMapCmd.Parameters["$name"].Value = name;
                apMapCmd.Parameters["$slug"].Value = slugAttempt;
                await using (var reader = await apMapCmd.ExecuteReaderAsync()) {
                    if (await reader.ReadAsync()) {
                        matched = ReadRecord(reader);
                    }
                }

                // Pass 2: Exact Title
                if (matched == null) {
                    exactTitleCmd.Parameters["$name"].Value = name;
                    await using var reader = await exactTitleCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        matched = ReadRecord(reader);
                    }
                }

                // Pass 3: Exact Slug
                if (matched == null) {
                    exactSlugCmd.Parameters["$slug"].Value = slugAttempt;
                    await using var reader = await exactSlugCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        matched = ReadRecord(reader);
                    }
                }

                // Pass 4: Alt Title Pattern
                if (matched == null && name.Length >= 4) {
                    altTitleCmd.Parameters["$nameLike"].Value = $"%{name}%";
                    await using var reader = await altTitleCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) {
                        matched = ReadRecord(reader);
                    }
                }

                if (matched != null) {
                    bool isTracked = existingSlugs.Contains(matched.slug.ToLowerInvariant());
                    if (isTracked) alreadyTrackedCount++;

                    matchedList.Add(new {
                        external_name = name,
                        matched_id = matched.id,
                        matched_slug = matched.slug,
                        matched_title = matched.title,
                        thumbnail_url = matched.thumbnail_url ?? "/data/images/prod/assets/inc/img/logos/dimension-kun.svg",
                        type = matched.type ?? "TV",
                        year = matched.year,
                        total_episodes = matched.data_total_episodes,
                        status = status,
                        status_name = AnimeListEndpoints.StatusToName(status),
                        episodes_watched = eps,
                        rating = rating,
                        times_watched = times,
                        already_in_list = isTracked
                    });
                } else {
                    unmatchedList.Add(new {
                        external_name = name,
                        status = status,
                        status_name = AnimeListEndpoints.StatusToName(status),
                        episodes_watched = eps,
                        rating = rating,
                        times_watched = times,
                        reason = "newer_or_unindexed",
                        message = "This title was not found in our database. It may be a newer release or seasonal broadcast not yet indexed in our catalog."
                    });
                }
            }

            return Results.Json(new {
                status = "ok",
                summary = new {
                    total_entries = totalCount,
                    matched_count = matchedList.Count,
                    unmatched_count = unmatchedList.Count,
                    already_tracked_count = alreadyTrackedCount
                },
                matched_entries = matchedList,
                unmatched_entries = unmatchedList
            });
        });

        // POST /api/users/me/import/confirm
        app.MapPost("/api/users/me/import/confirm", async (HttpRequest req, Sql.UsersDbContext usersDb) => {
            var me = await AnimeListEndpoints.ResolveUserFromSession(req, usersDb);
            if (me == null) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "Authentication required" }, statusCode: 401);
            }

            ConfirmImportPayload? body;
            try {
                body = await req.ReadFromJsonAsync<ConfirmImportPayload>();
            } catch (Exception ex) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = $"Invalid JSON: {ex.Message}" }, statusCode: 400);
            }

            if (body == null || body.entries == null || body.entries.Count == 0) {
                return Results.Json(new DTOs.ErrResponse { status = "err", msg = "No entries to import" }, statusCode: 400);
            }

            bool overwrite = body.overwrite_existing;

            // Fetch existing user entries
            var existingMap = await usersDb.UserAnimeLists
                .Where(e => e.UserId == me.UserId)
                .ToDictionaryAsync(e => e.AnimeSlug.ToLowerInvariant(), e => e);

            int insertedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;

            DateTime now = DateTime.UtcNow;

            foreach (var item in body.entries) {
                if (string.IsNullOrWhiteSpace(item.anime_slug)) continue;
                string cleanSlug = item.anime_slug.Trim().ToLowerInvariant();

                int status = item.status;
                if (status < 1 || status > 6) status = 2; // Default to watching

                int eps = Math.Max(0, item.episodes_watched);
                double? rating = item.rating.HasValue && item.rating.Value > 0
                    ? Math.Min(5.0, Math.Max(0.5, Math.Round(item.rating.Value * 2.0) / 2.0))
                    : null;
                int times = Math.Max(1, item.times_watched);

                if (existingMap.TryGetValue(cleanSlug, out var existing)) {
                    if (overwrite) {
                        existing.Status = status;
                        existing.EpisodesWatched = eps;
                        existing.Rating = rating;
                        existing.TimesWatched = times;
                        existing.UpdatedAt = now;
                        updatedCount++;
                    } else {
                        skippedCount++;
                    }
                } else {
                    var newEntry = new Models.UserAnimeListEntry {
                        UserId = me.UserId,
                        AnimeId = item.anime_id,
                        AnimeSlug = cleanSlug,
                        Status = status,
                        EpisodesWatched = eps,
                        Rating = rating,
                        TimesWatched = times,
                        Notes = item.notes,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    usersDb.UserAnimeLists.Add(newEntry);
                    existingMap[cleanSlug] = newEntry;
                    insertedCount++;
                }
            }

            await usersDb.SaveChangesAsync();
            await AnimeListEndpoints.RecalculateUserStatsAsync(usersDb, me.UserId);

            return Results.Json(new {
                status = "ok",
                data = new {
                    inserted_count = insertedCount,
                    updated_count = updatedCount,
                    skipped_count = skippedCount,
                    total_processed = insertedCount + updatedCount + skippedCount
                }
            });
        });
    }

    private class MatchedAnimeRecord {
        public int id { get; set; }
        public required string slug { get; set; }
        public required string title { get; set; }
        public string? thumbnail_url { get; set; }
        public string? type { get; set; }
        public int? year { get; set; }
        public int? data_total_episodes { get; set; }
    }

    private static MatchedAnimeRecord ReadRecord(SqliteDataReader r) {
        return new MatchedAnimeRecord {
            id = r.GetInt32(0),
            slug = r.GetString(1),
            title = r.GetString(2),
            thumbnail_url = r.IsDBNull(3) ? null : r.GetString(3),
            type = r.IsDBNull(4) ? null : r.GetString(4),
            year = r.IsDBNull(5) ? null : r.GetInt32(5),
            data_total_episodes = r.IsDBNull(6) ? null : r.GetInt32(6)
        };
    }

    private static int ParseImportStatus(string raw) {
        return raw switch {
            "watched" or "completed" or "1" => 1,
            "watching" or "current" or "2" => 2,
            "dropped" or "3" => 3,
            "want to watch" or "wanttowatch" or "want" or "plan to watch" or "4" => 4,
            "stalled" or "on-hold" or "on_hold" or "paused" or "5" => 5,
            "won't watch" or "wontwatch" or "wont" or "6" => 6,
            _ => 2
        };
    }

    private static string Slugify(string text) {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string str = text.ToLowerInvariant();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", "-").Trim('-');
        return str;
    }

    public class ConfirmImportPayload {
        public bool overwrite_existing { get; set; } = false;
        public List<ConfirmImportItem>? entries { get; set; }
    }

    public class ConfirmImportItem {
        public int anime_id { get; set; }
        public string? anime_slug { get; set; }
        public int status { get; set; }
        public int episodes_watched { get; set; }
        public double? rating { get; set; }
        public int times_watched { get; set; } = 1;
        public string? notes { get; set; }
    }
}
