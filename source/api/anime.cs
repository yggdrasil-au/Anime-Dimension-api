using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using ASP.NETCoreWebApi.Serialization;
using System.Text.Json.Serialization.Metadata;


namespace ASP.NETCoreWebApi;

public static class AnimeEndpoints {
    public static void MapAnimeEndpoints(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) {
        // GET detail (used by SSR and CSR), these are not currently in use only ssg offline Static build in use
        app.MapGet("/api/anime/by-slug/{slug}", (string slug, AnimeDbOptions db) => {
            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest(new {
                    status = "error",
                    msg = "Invalid slug."
                });

            try {
                AnimeDto? dto = GetAnimeBySlug(db.DbPath, slug);
                return dto is null
                ? Results.NotFound(new { status = "error", msg = "Anime not found." })
                : Results.Json(dto, (JsonTypeInfo<AnimeEndpoints.AnimeDto>)AppJsonContext.Default.GetTypeInfo(typeof(AnimeEndpoints.AnimeDto))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query anime: {ex.Message}");
            }
        });

        // ssr Accept POST too for convenience
        app.MapPost("/api/anime/by-slug/{slug}", (string slug, AnimeDbOptions db) => {
            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest(new {
                    status = "error",
                    msg = "Invalid slug."
                });

            try {
                AnimeDto? dto = GetAnimeBySlug(db.DbPath, slug);
                return dto is null
                ? Results.NotFound(new { status = "error", msg = "Anime not found." })
                : Results.Json(dto, (JsonTypeInfo<AnimeEndpoints.AnimeDto>)AppJsonContext.Default.GetTypeInfo(typeof(AnimeEndpoints.AnimeDto))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query anime: {ex.Message}");
            }
        });

        // ssg-csr all page Post all anime data for csr
        app.MapPost("/api/anime/all", (AnimeDbOptions db) => {
            try {
                System.Collections.Generic.List<AnimeDto> animes = new System.Collections.Generic.List<AnimeDto>();
                using Microsoft.Data.Sqlite.SqliteConnection conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db.DbPath}");
                conn.Open();

                System.Collections.Generic.List<string> slugs = new System.Collections.Generic.List<string>();
                using (Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand()) {
                    cmd.CommandText = "SELECT slug FROM anime ORDER BY title ASC;";
                    using Microsoft.Data.Sqlite.SqliteDataReader reader = cmd.ExecuteReader();
                    while (reader.Read()) {
                        slugs.Add(reader.GetString(0));
                    }
                }

                foreach (string slug in slugs) {
                    AnimeDto? dto = GetAnimeBySlug(conn, slug);
                    if (dto != null)
                        animes.Add(dto);
                }

                return Results.Json(animes.ToArray(), (JsonTypeInfo<AnimeEndpoints.AnimeDto[]>)AppJsonContext.Default.GetTypeInfo(typeof(AnimeEndpoints.AnimeDto[]))!);
            } catch (Exception ex) {
                return Results.Problem($"Failed to query anime list: {ex.Message}");
            }
        });

    }

    static bool ColumnExists(Microsoft.Data.Sqlite.SqliteConnection conn, string table, string column) {
        using Microsoft.Data.Sqlite.SqliteCommand c = conn.CreateCommand();
        c.CommandText = "PRAGMA table_info(" + table + ")";
        using Microsoft.Data.Sqlite.SqliteDataReader r = c.ExecuteReader();
        while (r.Read()) {
            string name = r.GetString(1); // name
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static System.Collections.Generic.HashSet<string> ReaderColumns(Microsoft.Data.Sqlite.SqliteDataReader reader) {
        System.Collections.Generic.HashSet<string> set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (Int32 i = 0; i < reader.FieldCount; i++)
            set.Add(reader.GetName(i));
        return set;
    }

    static T? ReadOrDefault<T>(Microsoft.Data.Sqlite.SqliteDataReader reader, string name) {
        int idx;
        try { idx = reader.GetOrdinal(name); } catch (IndexOutOfRangeException) { return default; }
        if (idx < 0 || reader.IsDBNull(idx)) return default;
        object val = reader.GetValue(idx);

        // Fast-path common types without reflection heavy patterns
        if (typeof(T) == typeof(string)) return (T)(object)Convert.ToString(val)!;
        if (typeof(T) == typeof(long)) return (T)(object)Convert.ToInt64(val);
        if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(val);
        if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(val);
        if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(val);

        Type t = typeof(T);
        Type? underlying = Nullable.GetUnderlyingType(t);
        if (underlying != null) {
            object converted = Convert.ChangeType(val, underlying);
            // For Nullable<T>, just cast boxed value which is already of underlying type
            return (T)converted;
        }
        return (T)Convert.ChangeType(val, t);
    }

    static AnimeDto? GetAnimeBySlug(string dbPath, string slug) {
        using Microsoft.Data.Sqlite.SqliteConnection conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return GetAnimeBySlug(conn, slug);
    }

    static AnimeDto? GetAnimeBySlug(Microsoft.Data.Sqlite.SqliteConnection conn, string slug) {
        // Fetch base row
        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        bool hasTooltip = ColumnExists(conn, "anime", "tooltip");
        bool hasAltText = ColumnExists(conn, "anime", "alt_text");
        bool hasTotalEpisodes = ColumnExists(conn, "anime", "data_total_episodes");

        // Build SELECT list dynamically based on available columns to support multiple schema versions
        System.Text.StringBuilder select = new System.Text.StringBuilder();
        select.Append("SELECT id, slug, title, alt_title, thumbnail_url, synopsis, \"year\", \"type\"");
        if (hasAltText) select.Append(", alt_text");
        if (hasTooltip) select.Append(", tooltip");
        if (hasTotalEpisodes) select.Append(", data_total_episodes");
        select.Append(" FROM anime WHERE slug = $slug LIMIT 1;");
        cmd.CommandText = select.ToString();
        cmd.Parameters.AddWithValue("$slug", slug);

        using Microsoft.Data.Sqlite.SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        System.Collections.Generic.HashSet<string> cols = ReaderColumns(reader);
        long id = ReadOrDefault<long>(reader, "id");
        string rowSlug = ReadOrDefault<string>(reader, "slug") ?? slug;
        string? title = ReadOrDefault<string>(reader, "title") ?? rowSlug;
        string? alt_title = ReadOrDefault<string>(reader, "alt_title");
        string? alt_text = hasAltText ? ReadOrDefault<string>(reader, "alt_text") : null;
        string? thumb = ReadOrDefault<string>(reader, "thumbnail_url");
        string? summary = null;
        string? synopsis = ReadOrDefault<string>(reader, "synopsis");
        string? tooltip = hasTooltip ? ReadOrDefault<string>(reader, "tooltip") : null;
        string? year = ReadOrDefault<string>(reader, "year");
        string? type = ReadOrDefault<string>(reader, "type");
        // data_id moved to external_id.external_numeric_id via anime_id in new schema
        string? data_id = null;
        string? ep_type = null; // data_episode_type no longer exists in new schema
        string? total_eps = hasTotalEpisodes ? ReadOrDefault<long?>(reader, "data_total_episodes")?.ToString() : null;

        reader.Close();

        // Fetch external_numeric_id as data_id (first available for this anime)
        try {
            using Microsoft.Data.Sqlite.SqliteCommand ecmd = conn.CreateCommand();
            ecmd.CommandText = @"SELECT external_numeric_id FROM external_id WHERE anime_id = $id ORDER BY id ASC LIMIT 1;";
            ecmd.Parameters.AddWithValue("$id", id);
            object? ev = ecmd.ExecuteScalar();
            if (ev != null && ev != DBNull.Value) {
                // external_numeric_id may be stored as INTEGER; keep the legacy contract as string
                data_id = Convert.ToInt64(ev).ToString();
            }
        } catch {
            // Ignore lookup failures; keep data_id as null
        }

        // Fetch tags
        System.Collections.Generic.List<string> tags = new System.Collections.Generic.List<string>();
        using (Microsoft.Data.Sqlite.SqliteCommand tcmd = conn.CreateCommand()) {
            tcmd.CommandText = @"
                SELECT t.name FROM tag t
                JOIN anime_tag at ON at.tag_id = t.id
                WHERE at.anime_id = $id ORDER BY t.name;";
            tcmd.Parameters.AddWithValue("$id", id);
            using Microsoft.Data.Sqlite.SqliteDataReader tr = tcmd.ExecuteReader();
            while (tr.Read())
                tags.Add(tr.GetString(0));
        }

        // If tooltip is missing in DB, synthesize a simple one compatible with CSR tooltip parser
        if (string.IsNullOrWhiteSpace(tooltip)) {
            Func<string, string> enc = (string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            parts.Add($"<h5 class=\"theme-font\">{enc(title ?? rowSlug)}</h5>");
            if (!string.IsNullOrWhiteSpace(type) || !string.IsNullOrWhiteSpace(year)) {
                System.Collections.Generic.List<string> li = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(type))
                    li.Add($"<li class=\"type\">{enc(type!)}</li>");
                if (!string.IsNullOrWhiteSpace(year))
                    li.Add($"<li class=\"iconYear\">{enc(year!)}</li>");
                parts.Add($"<ul class=\"entryBar\">{string.Join(string.Empty, li)}</ul>");
            }
            string? sum = summary ?? synopsis;
            if (!string.IsNullOrWhiteSpace(sum))
                parts.Add($"<p>{enc(sum!)}</p>");
            if (tags.Count > 0) {
                string tagLis = string.Join(string.Empty, tags.Select(t => $"<li>{enc(t)}</li>"));
                parts.Add($"<div class=\"tags\"><h4>Tags</h4><ul>{tagLis}</ul></div>");
            }
            tooltip = string.Join(string.Empty, parts);
        }

        return new AnimeDto(
            title: title ?? string.Empty,
            slug: rowSlug,
            thumbnailUrl: thumb ?? string.Empty,
            summary: summary ?? synopsis ?? string.Empty,
            year: year ?? string.Empty,
            type: type ?? string.Empty,
            alt_title: alt_title,
            data_id: data_id,
            data_episode_type: ep_type,
            data_total_episodes: total_eps,
            @class: null,
            synopsis: synopsis,
            tags: tags.ToArray(),
            alt_text: alt_text,
            tooltip: tooltip
        );
    }

    public record AnimeDto(
        string? title,
        string slug,
        string thumbnailUrl,
        string summary,
        string year,
        string type,
        string? alt_title = null,
        string? data_id = null,
        string? data_episode_type = null,
        string? data_total_episodes = null,
        string[]? @class = null,
        string? synopsis = null,
        string[]? tags = null,
        string? alt_text = null,
        string? tooltip = null
    );
}
