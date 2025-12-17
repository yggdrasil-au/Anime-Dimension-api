// api/suggestions.cs
using Microsoft.Data.Sqlite;
using System.Linq;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;


namespace ASP.NETCoreWebApi;
public static class SuggestionsEndpoints {

    public static void MapSuggestionsEndpoints(this IEndpointRouteBuilder app) {

        // Basic suggestions endpoint backed by SQLite
        app.MapPost("/api/suggestions/get", async (HttpRequest req, AnimeDbOptions db) => {
            Int32 total = 12;
            try {
                if (req.HasFormContentType) {
                    IFormCollection form = await req.ReadFormAsync();
                    if (Int32.TryParse(form["totalRequested"].FirstOrDefault(), out Int32 n) && n > 0 && n <= 60)
                        total = n;
                }
            } catch { /* ignore */ }

            try {
                List<DTOs.SuggestionItem> list = new List<DTOs.SuggestionItem>();
                using SqliteConnection conn = new SqliteConnection($"Data Source={db.DbPath}");
                await conn.OpenAsync();
                // Introspect optional columns/tables to keep compatibility with older DBs
                Boolean hasStudio = false, hasRatingCol = false, hasNotes = false;
                Boolean hasTags = false, hasReviews = false;
                Boolean hasAltText = false, hasDataEpisodeType = false, hasTotalEpisodes = false;
                Boolean hasExternalId = false;
                using (SqliteCommand info = conn.CreateCommand()) {
                    info.CommandText = "PRAGMA table_info(anime)";
                    using SqliteDataReader ir = await info.ExecuteReaderAsync();
                    while (await ir.ReadAsync()) {
                        String col = ir.GetString(1);
                        if (String.Equals(col, "studio", StringComparison.OrdinalIgnoreCase))
                            hasStudio = true;
                        else if (String.Equals(col, "rating", StringComparison.OrdinalIgnoreCase))
                            hasRatingCol = true;
                        else if (String.Equals(col, "notes", StringComparison.OrdinalIgnoreCase))
                            hasNotes = true;
                        else if (String.Equals(col, "alt_text", StringComparison.OrdinalIgnoreCase))
                            hasAltText = true;
                        else if (String.Equals(col, "data_episode_type", StringComparison.OrdinalIgnoreCase))
                            hasDataEpisodeType = true;
                        else if (String.Equals(col, "data_total_episodes", StringComparison.OrdinalIgnoreCase))
                            hasTotalEpisodes = true;
                    }
                }
                using (SqliteCommand mt = conn.CreateCommand()) {
                    mt.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('anime_tag','tag','review','external_id')";
                    using SqliteDataReader mr = await mt.ExecuteReaderAsync();
                    HashSet<String> found = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                    while (await mr.ReadAsync())
                        found.Add(mr.GetString(0));
                    hasTags = found.Contains("anime_tag") && found.Contains("tag");
                    hasReviews = found.Contains("review");
                    hasExternalId = found.Contains("external_id");
                }

                // Build dynamic SQL including optional joins/columns
                // Stable positions:
                // 0 id, 1 slug, 2 title, 3 thumbnail, 4 alt_title, 5 alt_text, 6 synopsis, 7 year, 8 type,
                // 9 data_id, 10 data_episode_type, 11 data_total_episodes, 12 studio, 13 rating_base, 14 tags_concat, 15 rating_avg, 16 notes_raw
                String cols = @"a.id, a.slug, a.title, a.thumbnail_url, a.alt_title";
                cols += hasAltText ? ", a.alt_text" : ", NULL as alt_text";
                cols += @", a.synopsis, a.""year"", a.""type""";
                cols += hasExternalId ? ", MIN(e.external_numeric_id) as data_id" : ", NULL as data_id";
                cols += hasDataEpisodeType ? ", a.data_episode_type" : ", NULL as data_episode_type";
                cols += hasTotalEpisodes ? ", a.data_total_episodes" : ", NULL as data_total_episodes";
                if (hasStudio)
                    cols += ", a.studio";
                else
                    cols += ", NULL as studio";
                if (hasRatingCol)
                    cols += ", a.rating as rating_base";
                else
                    cols += ", NULL as rating_base";
                if (hasTags)
                    cols += ", GROUP_CONCAT(t.name) as tags_concat";
                else
                    cols += ", NULL as tags_concat";
                if (hasReviews)
                    cols += ", AVG(rv.rating) as rating_avg";
                else
                    cols += ", NULL as rating_avg";
                if (hasNotes)
                    cols += ", a.notes as notes_raw";
                else
                    cols += ", NULL as notes_raw";

                String from = " FROM anime a ";
                if (hasTags)
                    from += " LEFT JOIN anime_tag at ON at.anime_id = a.id LEFT JOIN tag t ON t.id = at.tag_id ";
                if (hasReviews)
                    from += " LEFT JOIN review rv ON rv.anime_id = a.id ";
                if (hasExternalId)
                    from += " LEFT JOIN external_id e ON e.anime_id = a.id ";

                String where = " WHERE a.slug IS NOT NULL ";
                String group = " GROUP BY a.id ";
                String order = " ORDER BY a.title ";

                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {cols}{from}{where}{group}{order} LIMIT $n;";
                cmd.Parameters.AddWithValue("$n", total);
                using SqliteDataReader r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) {
                    Int64 id = r.GetInt64(0);
                    String slug = r.IsDBNull(1) ? String.Empty : r.GetString(1);
                    String title = r.IsDBNull(2) ? slug : r.GetString(2);
                    String? thumb = r.IsDBNull(3) ? null : r.GetString(3);
                    String? altTitle = r.IsDBNull(4) ? null : r.GetString(4);
                    String? altText = r.IsDBNull(5) ? null : r.GetString(5);
                    String? synopsis = r.IsDBNull(6) ? null : r.GetString(6);
                    String? year = r.IsDBNull(7) ? null : r.GetString(7);
                    String? typeTxt = r.IsDBNull(8) ? null : r.GetString(8);
                    Int64? dataId = r.IsDBNull(9) ? (Int64?)null : r.GetInt64(9);
                    String? dataEpisodeType = r.IsDBNull(10) ? null : r.GetString(10);
                    Int32? dataTotalEpisodes = r.IsDBNull(11) ? (Int32?)null : r.GetInt32(11);
                    String? studio = r.IsDBNull(12) ? null : r.GetString(12);
                    // rating_base at 13, tags_concat at 14, rating_avg at 15, notes_raw at 16 depending on flags
                    Double? ratingBase = null;
                    String? tagsConcat = null;
                    Double? ratingAvg = null;
                    String? notesRaw = null;
                    Int32 idx = 13;
                    if (hasRatingCol) {
                        ratingBase = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasTags) {
                        tagsConcat = r.IsDBNull(idx) ? null : r.GetString(idx);
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasReviews) {
                        ratingAvg = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasNotes) {
                        notesRaw = r.IsDBNull(idx) ? null : r.GetString(idx);
                    }

                    String[]? tagsArr = null;
                    if (!String.IsNullOrEmpty(tagsConcat)) {
                        tagsArr = tagsConcat.Split(',').Select(s => s?.Trim()).Where(s => !String.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
                    }
                    Double? rating = ratingBase ?? (ratingAvg.HasValue ? Math.Round(ratingAvg.Value, 1) : (Double?)null);

                    list.Add(new DTOs.SuggestionItem {
                        id = id,
                        slug = slug,
                        title = title,
                        url = $"/anime/{slug}.html",
                        thumbnailUrl = thumb,
                        year = year,
                        type = typeTxt,
                        altTitle = altTitle,
                        altText = altText,
                        summary = synopsis,
                        synopsis = synopsis,
                        studio = studio,
                        rating = rating,
                        dataId = dataId,
                        dataEpisodeType = dataEpisodeType,
                        dataTotalEpisodes = dataTotalEpisodes,
                        tags = tagsArr,
                        notes = String.IsNullOrWhiteSpace(notesRaw) ? null : notesRaw.Split('|', ',').Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s)).ToArray(),
                    });
                }
                DTOs.SuggestionsListResponse resp = new DTOs.SuggestionsListResponse { list = list, success = true };
                return Results.Json(resp,
                    (JsonTypeInfo<DTOs.SuggestionsListResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SuggestionsListResponse))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query suggestions: {ex.Message}");
            }
        });

        // Popular this week (basic list for now, DB-backed)
        app.MapPost("/api/suggestions/popular_this_week", async (HttpRequest req, AnimeDbOptions db) => {
            Int32 total = 12;
            IFormCollection? form = await req.ReadFormAsync();
            String type = form?["type"].FirstOrDefault()?.ToLowerInvariant() ?? "anime";
            Int32.TryParse(form?["totalRequested"].FirstOrDefault() ?? "12", out total);
            total = Math.Clamp(total, 1, 60);

            try {
                List<DTOs.SuggestionItem> list = new List<DTOs.SuggestionItem>();
                using SqliteConnection conn = new SqliteConnection($"Data Source={db.DbPath}");
                await conn.OpenAsync();
                // Introspect optional columns/tables
                Boolean hasStudio = false, hasRatingCol = false, hasTags = false, hasReviews = false, hasNotes = false;
                Boolean hasAltText = false, hasDataEpisodeType = false, hasTotalEpisodes = false;
                Boolean hasExternalId = false;
                using (SqliteCommand info = conn.CreateCommand()) {
                    info.CommandText = "PRAGMA table_info(anime)";
                    using SqliteDataReader ir = await info.ExecuteReaderAsync();
                    while (await ir.ReadAsync()) {
                        String col = ir.GetString(1);
                        if (String.Equals(col, "studio", StringComparison.OrdinalIgnoreCase))
                            hasStudio = true;
                        else if (String.Equals(col, "rating", StringComparison.OrdinalIgnoreCase))
                            hasRatingCol = true;
                        else if (String.Equals(col, "notes", StringComparison.OrdinalIgnoreCase))
                            hasNotes = true;
                        else if (String.Equals(col, "alt_text", StringComparison.OrdinalIgnoreCase))
                            hasAltText = true;
                        else if (String.Equals(col, "data_episode_type", StringComparison.OrdinalIgnoreCase))
                            hasDataEpisodeType = true;
                        else if (String.Equals(col, "data_total_episodes", StringComparison.OrdinalIgnoreCase))
                            hasTotalEpisodes = true;
                    }
                }
                using (SqliteCommand mt = conn.CreateCommand()) {
                    mt.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('anime_tag','tag','review','external_id')";
                    using SqliteDataReader mr = await mt.ExecuteReaderAsync();
                    HashSet<String> found = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                    while (await mr.ReadAsync())
                        found.Add(mr.GetString(0));
                    hasTags = found.Contains("anime_tag") && found.Contains("tag");
                    hasReviews = found.Contains("review");
                    hasExternalId = found.Contains("external_id");
                }

                String cols = @"a.id, a.slug, a.title, a.thumbnail_url, a.alt_title";
                cols += hasAltText ? ", a.alt_text" : ", NULL as alt_text";
                cols += @", a.synopsis, a.""year"", a.""type""";
                cols += hasExternalId ? ", MIN(e.external_numeric_id) as data_id" : ", NULL as data_id";
                cols += hasDataEpisodeType ? ", a.data_episode_type" : ", NULL as data_episode_type";
                cols += hasTotalEpisodes ? ", a.data_total_episodes" : ", NULL as data_total_episodes";
                if (hasStudio)
                    cols += ", a.studio";
                else
                    cols += ", NULL as studio";
                if (hasRatingCol)
                    cols += ", a.rating as rating_base";
                else
                    cols += ", NULL as rating_base";
                if (hasTags)
                    cols += ", GROUP_CONCAT(t.name) as tags_concat";
                else
                    cols += ", NULL as tags_concat";
                if (hasReviews)
                    cols += ", AVG(rv.rating) as rating_avg";
                else
                    cols += ", NULL as rating_avg";
                if (hasNotes)
                    cols += ", a.notes as notes_raw";
                else
                    cols += ", NULL as notes_raw";

                String from = " FROM anime a ";
                if (hasTags)
                    from += " LEFT JOIN anime_tag at ON at.anime_id = a.id LEFT JOIN tag t ON t.id = at.tag_id ";
                if (hasReviews)
                    from += " LEFT JOIN review rv ON rv.anime_id = a.id ";
                if (hasExternalId)
                    from += " LEFT JOIN external_id e ON e.anime_id = a.id ";
                String where = " WHERE a.slug IS NOT NULL ";
                String group = " GROUP BY a.id ";
                String order = " ORDER BY RANDOM() ";

                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {cols}{from}{where}{group}{order} LIMIT $n;";
                cmd.Parameters.AddWithValue("$n", total);
                using SqliteDataReader r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) {
                    Int64 id = r.GetInt64(0);
                    String slug = r.IsDBNull(1) ? String.Empty : r.GetString(1);
                    String title = r.IsDBNull(2) ? slug : r.GetString(2);
                    String? thumb = r.IsDBNull(3) ? null : r.GetString(3);
                    String? altTitle = r.IsDBNull(4) ? null : r.GetString(4);
                    String? altText = r.IsDBNull(5) ? null : r.GetString(5);
                    String? synopsis = r.IsDBNull(6) ? null : r.GetString(6);
                    String? year = r.IsDBNull(7) ? null : r.GetString(7);
                    String? typeTxt = r.IsDBNull(8) ? null : r.GetString(8);
                    Int64? dataId = r.IsDBNull(9) ? (Int64?)null : r.GetInt64(9);
                    String? dataEpisodeType = r.IsDBNull(10) ? null : r.GetString(10);
                    Int32? dataTotalEpisodes = r.IsDBNull(11) ? (Int32?)null : r.GetInt32(11);
                    String? studio = r.IsDBNull(12) ? null : r.GetString(12);
                    Double? ratingBase = null;
                    String? tagsConcat = null;
                    Double? ratingAvg = null;
                    String? notesRaw = null;
                    Int32 idx = 13;
                    if (hasRatingCol) {
                        ratingBase = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasTags) {
                        tagsConcat = r.IsDBNull(idx) ? null : r.GetString(idx);
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasReviews) {
                        ratingAvg = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasNotes) {
                        notesRaw = r.IsDBNull(idx) ? null : r.GetString(idx);
                    }

                    String[]? tagsArr = null;
                    if (!String.IsNullOrEmpty(tagsConcat))
                        tagsArr = tagsConcat.Split(',').Select(s => s?.Trim()).Where(s => !String.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
                    Double? rating = ratingBase ?? (ratingAvg.HasValue ? Math.Round(ratingAvg.Value, 1) : (Double?)null);

                    list.Add(new DTOs.SuggestionItem {
                        id = id,
                        slug = slug,
                        title = title,
                        url = $"/anime/{slug}.html",
                        thumbnailUrl = thumb,
                        year = year,
                        type = typeTxt,
                        altTitle = altTitle,
                        altText = altText,
                        summary = synopsis,
                        synopsis = synopsis,
                        studio = studio,
                        rating = rating,
                        dataId = dataId,
                        dataEpisodeType = dataEpisodeType,
                        dataTotalEpisodes = dataTotalEpisodes,
                        tags = tagsArr,
                        notes = String.IsNullOrWhiteSpace(notesRaw) ? null : notesRaw.Split('|', ',').Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s)).ToArray(),
                    });
                }
                DTOs.SuggestionsListResponse resp = new DTOs.SuggestionsListResponse { list = list };
                return Results.Json(resp,
                    (JsonTypeInfo<DTOs.SuggestionsListResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SuggestionsListResponse))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query popular: {ex.Message}");
            }
        });

        // This season (basic list for now, DB-backed)
        // Later: filter by a dedicated season column (winter/spring/summer/fall) and year
        app.MapPost("/api/suggestions/this_season", async (HttpRequest req, AnimeDbOptions db) => {
            Int32 total = 12;
            IFormCollection? form = await req.ReadFormAsync();
            String season = form?["season"].FirstOrDefault()?.ToLowerInvariant() ?? ""; // unused for now
            Int32.TryParse(form?["totalRequested"].FirstOrDefault() ?? "12", out total);
            total = Math.Clamp(total, 1, 60);

            try {
                List<DTOs.SuggestionItem> list = new List<DTOs.SuggestionItem>();
                using SqliteConnection conn = new SqliteConnection($"Data Source={db.DbPath}");
                await conn.OpenAsync();

                // Optional columns/tables
                Boolean hasStudio = false, hasRatingCol = false, hasTags = false, hasReviews = false, hasNotes = false;
                Boolean hasAltText = false, hasDataEpisodeType = false, hasTotalEpisodes = false;
                Boolean hasExternalId = false;
                using (SqliteCommand info = conn.CreateCommand()) {
                    info.CommandText = "PRAGMA table_info(anime)";
                    using SqliteDataReader ir = await info.ExecuteReaderAsync();
                    while (await ir.ReadAsync()) {
                        String col = ir.GetString(1);
                        if (String.Equals(col, "studio", StringComparison.OrdinalIgnoreCase))
                            hasStudio = true;
                        else if (String.Equals(col, "rating", StringComparison.OrdinalIgnoreCase))
                            hasRatingCol = true;
                        else if (String.Equals(col, "notes", StringComparison.OrdinalIgnoreCase))
                            hasNotes = true;
                        else if (String.Equals(col, "alt_text", StringComparison.OrdinalIgnoreCase))
                            hasAltText = true;
                        else if (String.Equals(col, "data_episode_type", StringComparison.OrdinalIgnoreCase))
                            hasDataEpisodeType = true;
                        else if (String.Equals(col, "data_total_episodes", StringComparison.OrdinalIgnoreCase))
                            hasTotalEpisodes = true;
                    }
                }
                using (SqliteCommand mt = conn.CreateCommand()) {
                    mt.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('anime_tag','tag','review','external_id')";
                    using SqliteDataReader mr = await mt.ExecuteReaderAsync();
                    HashSet<String> found = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                    while (await mr.ReadAsync())
                        found.Add(mr.GetString(0));
                    hasTags = found.Contains("anime_tag") && found.Contains("tag");
                    hasReviews = found.Contains("review");
                    hasExternalId = found.Contains("external_id");
                }

                // Prioritize most recent by 4-digit year; then title
                String cols = @"a.id, a.slug, a.title, a.thumbnail_url, a.alt_title";
                cols += hasAltText ? ", a.alt_text" : ", NULL as alt_text";
                cols += @", a.synopsis, a.""year"", a.""type""";
                cols += hasExternalId ? ", MIN(e.external_numeric_id) as data_id" : ", NULL as data_id";
                cols += hasDataEpisodeType ? ", a.data_episode_type" : ", NULL as data_episode_type";
                cols += hasTotalEpisodes ? ", a.data_total_episodes" : ", NULL as data_total_episodes";
                if (hasStudio)
                    cols += ", a.studio";
                else
                    cols += ", NULL as studio";
                if (hasRatingCol)
                    cols += ", a.rating as rating_base";
                else
                    cols += ", NULL as rating_base";
                if (hasTags)
                    cols += ", GROUP_CONCAT(t.name) as tags_concat";
                else
                    cols += ", NULL as tags_concat";
                if (hasReviews)
                    cols += ", AVG(rv.rating) as rating_avg";
                else
                    cols += ", NULL as rating_avg";
                if (hasNotes)
                    cols += ", a.notes as notes_raw";
                else
                    cols += ", NULL as notes_raw";
                String from = " FROM anime a ";
                if (hasTags)
                    from += " LEFT JOIN anime_tag at ON at.anime_id = a.id LEFT JOIN tag t ON t.id = at.tag_id ";
                if (hasReviews)
                    from += " LEFT JOIN review rv ON rv.anime_id = a.id ";
                if (hasExternalId)
                    from += " LEFT JOIN external_id e ON e.anime_id = a.id ";

                String where = " WHERE a.slug IS NOT NULL ";
                String group = " GROUP BY a.id ";
                String order = @" ORDER BY CASE WHEN a.""year"" GLOB '[0-9][0-9][0-9][0-9]*' THEN 0 ELSE 1 END, a.""year"" DESC, a.title ASC ";

                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {cols}{from}{where}{group}{order} LIMIT $n;";
                cmd.Parameters.AddWithValue("$n", total);

                using SqliteDataReader r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) {
                    Int64 id = r.GetInt64(0);
                    String slug = r.IsDBNull(1) ? String.Empty : r.GetString(1);
                    String title = r.IsDBNull(2) ? slug : r.GetString(2);
                    String? thumb = r.IsDBNull(3) ? null : r.GetString(3);
                    String? altTitle = r.IsDBNull(4) ? null : r.GetString(4);
                    String? altText = r.IsDBNull(5) ? null : r.GetString(5);
                    String? synopsis = r.IsDBNull(6) ? null : r.GetString(6);
                    String? year = r.IsDBNull(7) ? null : r.GetString(7);
                    String? typeTxt = r.IsDBNull(8) ? null : r.GetString(8);
                    Int64? dataId = r.IsDBNull(9) ? (Int64?)null : r.GetInt64(9);
                    String? dataEpisodeType = r.IsDBNull(10) ? null : r.GetString(10);
                    Int32? dataTotalEpisodes = r.IsDBNull(11) ? (Int32?)null : r.GetInt32(11);
                    String? studio = r.IsDBNull(12) ? null : r.GetString(12);
                    Double? ratingBase = null;
                    String? tagsConcat = null;
                    Double? ratingAvg = null;
                    String? notesRaw = null;
                    Int32 idx = 13;
                    if (hasRatingCol) {
                        ratingBase = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasTags) {
                        tagsConcat = r.IsDBNull(idx) ? null : r.GetString(idx);
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasReviews) {
                        ratingAvg = r.IsDBNull(idx) ? (Double?)null : Convert.ToDouble(r.GetValue(idx));
                        idx++;
                    } else {
                        idx++;
                    }
                    if (hasNotes) {
                        notesRaw = r.IsDBNull(idx) ? null : r.GetString(idx);
                    }

                    String[]? tagsArr = null;
                    if (!String.IsNullOrEmpty(tagsConcat))
                        tagsArr = tagsConcat.Split(',').Select(s => s?.Trim()).Where(s => !String.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
                    Double? rating = ratingBase ?? (ratingAvg.HasValue ? Math.Round(ratingAvg.Value, 1) : (Double?)null);

                    list.Add(new DTOs.SuggestionItem {
                        id = id,
                        slug = slug,
                        title = title,
                        url = $"/anime/{slug}.html",
                        thumbnailUrl = thumb,
                        year = year,
                        type = typeTxt,
                        altTitle = altTitle,
                        altText = altText,
                        summary = synopsis,
                        synopsis = synopsis,
                        studio = studio,
                        rating = rating,
                        dataId = dataId,
                        dataEpisodeType = dataEpisodeType,
                        dataTotalEpisodes = dataTotalEpisodes,
                        tags = tagsArr,
                        notes = String.IsNullOrWhiteSpace(notesRaw) ? null : notesRaw.Split('|', ',').Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s)).ToArray(),
                    });
                }
                DTOs.SuggestionsListResponse resp = new DTOs.SuggestionsListResponse { list = list, season = season, success = true };
                return Results.Json(resp,
                    (JsonTypeInfo<DTOs.SuggestionsListResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SuggestionsListResponse))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query this season: {ex.Message}");
            }
        });

        // get anime related to user anime history, not implemented yet, simply return empty list
        app.MapPost("/api/suggestions/get_related", async (HttpRequest req, AnimeDbOptions db) => {
            DTOs.SuggestionsListResponse resp = new DTOs.SuggestionsListResponse { list = new List<DTOs.SuggestionItem>(), success = true };
            return Results.Json(resp,(JsonTypeInfo<DTOs.SuggestionsListResponse>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.SuggestionsListResponse))!);
        });

    }
}
