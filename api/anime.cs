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
        app.MapGet("/api/anime/by-slug/{slug}", (String slug, AnimeDbOptions db) => {
            if (String.IsNullOrWhiteSpace(slug))
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
        app.MapPost("/api/anime/by-slug/{slug}", (String slug, AnimeDbOptions db) => {
            if (String.IsNullOrWhiteSpace(slug))
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

    }

    static Boolean ColumnExists(Microsoft.Data.Sqlite.SqliteConnection conn, String table, String column) {
        using Microsoft.Data.Sqlite.SqliteCommand c = conn.CreateCommand();
        c.CommandText = "PRAGMA table_info(" + table + ")";
        using Microsoft.Data.Sqlite.SqliteDataReader r = c.ExecuteReader();
        while (r.Read()) {
            String name = r.GetString(1); // name
            if (String.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static System.Collections.Generic.HashSet<String> ReaderColumns(Microsoft.Data.Sqlite.SqliteDataReader reader) {
        System.Collections.Generic.HashSet<String> set = new System.Collections.Generic.HashSet<String>(StringComparer.OrdinalIgnoreCase);
        for (Int32 i = 0; i < reader.FieldCount; i++)
            set.Add(reader.GetName(i));
        return set;
    }

    static T? ReadOrDefault<T>(Microsoft.Data.Sqlite.SqliteDataReader reader, String name) {
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

    static AnimeDto? GetAnimeBySlug(String dbPath, String slug) {
        using Microsoft.Data.Sqlite.SqliteConnection conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // Fetch base row
        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        Boolean hasTooltip = ColumnExists(conn, "anime", "tooltip");
        Boolean hasAltText = ColumnExists(conn, "anime", "alt_text");
        Boolean hasTotalEpisodes = ColumnExists(conn, "anime", "data_total_episodes");

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
        System.Collections.Generic.HashSet<String> cols = ReaderColumns(reader);
        Int64 id = ReadOrDefault<Int64>(reader, "id");
        String rowSlug = ReadOrDefault<String>(reader, "slug") ?? slug;
        String? title = ReadOrDefault<String>(reader, "title") ?? rowSlug;
        String? alt_title = ReadOrDefault<String>(reader, "alt_title");
        String? alt_text = hasAltText ? ReadOrDefault<String>(reader, "alt_text") : null;
        String? thumb = ReadOrDefault<String>(reader, "thumbnail_url");
        String? summary = null;
        String? synopsis = ReadOrDefault<String>(reader, "synopsis");
        String? tooltip = hasTooltip ? ReadOrDefault<String>(reader, "tooltip") : null;
        String? year = ReadOrDefault<String>(reader, "year");
        String? type = ReadOrDefault<String>(reader, "type");
        // data_id moved to external_id.external_numeric_id via anime_id in new schema
        String? data_id = null;
        String? ep_type = null; // data_episode_type no longer exists in new schema
        String? total_eps = hasTotalEpisodes ? ReadOrDefault<Int64?>(reader, "data_total_episodes")?.ToString() : null;

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
        System.Collections.Generic.List<String> tags = new System.Collections.Generic.List<String>();
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
        if (String.IsNullOrWhiteSpace(tooltip)) {
            Func<String, String> enc = (String s) => System.Net.WebUtility.HtmlEncode(s ?? String.Empty);
            System.Collections.Generic.List<String> parts = new System.Collections.Generic.List<String>();
            parts.Add($"<h5 class=\"theme-font\">{enc(title ?? rowSlug)}</h5>");
            if (!String.IsNullOrWhiteSpace(type) || !String.IsNullOrWhiteSpace(year)) {
                System.Collections.Generic.List<String> li = new System.Collections.Generic.List<String>();
                if (!String.IsNullOrWhiteSpace(type))
                    li.Add($"<li class=\"type\">{enc(type!)}</li>");
                if (!String.IsNullOrWhiteSpace(year))
                    li.Add($"<li class=\"iconYear\">{enc(year!)}</li>");
                parts.Add($"<ul class=\"entryBar\">{String.Join(String.Empty, li)}</ul>");
            }
            String? sum = summary ?? synopsis;
            if (!String.IsNullOrWhiteSpace(sum))
                parts.Add($"<p>{enc(sum!)}</p>");
            if (tags.Count > 0) {
                String tagLis = String.Join(String.Empty, tags.Select(t => $"<li>{enc(t)}</li>"));
                parts.Add($"<div class=\"tags\"><h4>Tags</h4><ul>{tagLis}</ul></div>");
            }
            tooltip = String.Join(String.Empty, parts);
        }

        return new AnimeDto(
            title: title ?? String.Empty,
            slug: rowSlug,
            thumbnailUrl: thumb ?? String.Empty,
            summary: summary ?? synopsis ?? String.Empty,
            year: year ?? String.Empty,
            type: type ?? String.Empty,
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
        String? title,
        String slug,
        String thumbnailUrl,
        String summary,
        String year,
        String type,
        String? alt_title = null,
        String? data_id = null,
        String? data_episode_type = null,
        String? data_total_episodes = null,
        String[]? @class = null,
        String? synopsis = null,
        String[]? tags = null,
        String? alt_text = null,
        String? tooltip = null
    );
}
