// Program.cs
global using Microsoft.EntityFrameworkCore;
global using System.Globalization;
global using ASP.NETCoreWebApi.Sql;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Collections;
using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ASP.NETCoreWebApi;

public class Program {
    static void ValidateSqliteConnection(String dbPath) {
        using SqliteConnection? connection = new SqliteConnection(connectionString: $"Data Source={dbPath}");
        try {
            connection.Open();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 1;";
            cmd.ExecuteScalar(); // Run a trivial query
        } catch (Exception ex) {
            throw new InvalidOperationException(message: $"Failed to open or validate SQLite DB at {dbPath}", ex);
        }
    }
    static Boolean TableExists(String dbPath, String tableName) {
        using SqliteConnection conn = new SqliteConnection(connectionString: $"Data Source={dbPath}");
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = $table";
        cmd.Parameters.AddWithValue(parameterName: "$table", tableName);

        Int64? result = (Int64?)cmd.ExecuteScalar();
        return result > 0;
    }

    static Boolean FilesAreIdentical(String a, String b) {
        if (!File.Exists(a) || !File.Exists(b))
            return false;
        FileInfo fa = new FileInfo(a);
        FileInfo fb = new FileInfo(b);
        if (fa.Length != fb.Length)
            return false;
        using SHA256 ha = SHA256.Create();
        using SHA256 hb = SHA256.Create();
        using FileStream sa = File.OpenRead(a);
        using FileStream sb = File.OpenRead(b);
        Byte[] da = ha.ComputeHash(sa);
        Byte[] db = hb.ComputeHash(sb);
        return StructuralComparisons.StructuralEqualityComparer.Equals(da, db);
    }

    public static void Main(String[] args) {
        try {
            // Set the culture to ensure consistent date and number formatting
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // --- Service Configuration ---
            // Resolve important directories
            String baseDir = AppContext.BaseDirectory; // In single-file, this is the bundle extraction dir
            String exePath = Environment.ProcessPath ?? String.Empty; // Path to the launched executable
            String exeDir = String.IsNullOrEmpty(exePath) ? baseDir : (Path.GetDirectoryName(exePath) ?? baseDir);
            String cwdDir = Directory.GetCurrentDirectory();

            // Small helpers
            // Helper to pick the first existing file from a list of candidates
            static String? FirstExisting(params String[] candidates) {
                foreach (String p in candidates) {
                    try {
                        if (!String.IsNullOrWhiteSpace(p) && File.Exists(p))
                            return Path.GetFullPath(p);
                    } catch { /* ignore */ }
                }
                return null;
            }

            String? animeDbPath = null;

            static String? ProbeForAnimeDimensionDb(IEnumerable<String> dirs) {
                foreach (String d in dirs) {
                    if (String.IsNullOrWhiteSpace(d))
                        continue;
                    try {
                        String a = Path.Combine(d, "anime-dimension.sqlite3");
                        if (File.Exists(a))
                            return Path.GetFullPath(a);
                    } catch { /* ignore */ }
                }
                return null;
            }


            // Build a probe list of likely directories
            List<String?> probeDirs = new List<String?> {
                exeDir,
                cwdDir,
                baseDir,
                Path.GetFullPath(Path.Combine(exeDir, "..")),
                Path.GetFullPath(Path.Combine(cwdDir, "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..")),
                Path.GetFullPath(Path.Combine(cwdDir, "Anime-dimension-api")),
                Path.GetFullPath(Path.Combine(exeDir, "Anime-dimension-api"))
            };
            animeDbPath = ProbeForAnimeDimensionDb(probeDirs!);

            if (animeDbPath is null) {
                // Try to source from upstream Anime-Dimension-Database-Orchestrator DB if present, then copy next to the executable
                String[] apDbCandidates = new[] {
                                            //Path.Combine(exeDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),
                                            //Path.Combine(baseDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),
                                            Path.Combine(cwdDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),
                                    };
                String? apDb = FirstExisting(apDbCandidates);
                if (!String.IsNullOrEmpty(apDb)) {
                    String target = Path.Combine(cwdDir, "anime-dimension.sqlite3");
                    try {
                        if (!File.Exists(target) || !FilesAreIdentical(apDb, target)) {
                            File.Copy(apDb, target, overwrite: true);
                            Console.WriteLine($"Copied anime-dimension.sqlite3 from {apDb} to {target}");
                        }
                        animeDbPath = target;
                    } catch (Exception ex) {
                        Console.Error.WriteLine(ex.ToString());
                        Console.WriteLine($"Warning: failed to copy anime-dimension.sqlite3 from {apDb} to {target}. Will continue probing other locations.");
                    }
                    String exeTarget = Path.Combine(exeDir, "anime-dimension.sqlite3");
                    try {
                        if (!File.Exists(exeTarget) || !FilesAreIdentical(apDb, exeTarget)) {
                            Directory.CreateDirectory(exeDir);
                            File.Copy(apDb, exeTarget, overwrite: true);
                        }
                        animeDbPath = exeTarget;
                    } catch (Exception ex) {
                        Console.Error.WriteLine(ex.ToString());
                        Console.WriteLine($"Warning: failed to copy anime-dimension.sqlite3 from {apDb} to {exeTarget}. Will continue probing other locations.");
                    }
                }
            }

            // Resolve users DB similarly (env var optional: USERS_DB_PATH)
            String? envUsersDb = Environment.GetEnvironmentVariable("USERS_DB_PATH")?.Trim();
            String? usersDbPath = null;
            if (!String.IsNullOrWhiteSpace(envUsersDb) && File.Exists(envUsersDb)) {
                usersDbPath = Path.GetFullPath(envUsersDb);
            } else {
                usersDbPath = FirstExisting(
                        Path.Combine(exeDir, "users.db"),
                        Path.Combine(cwdDir, "users.db"),
                        Path.Combine(baseDir, "users.db")
                );
                if (usersDbPath is null)
                    usersDbPath = Path.Combine(exeDir, "users.db"); // default expected location
            }

            // Optionally resolve a distinct anime.db (legacy/alternate dataset) via env var or probing
            String? envAltAnimeDb = Environment.GetEnvironmentVariable("ANIME_DB_PATH")?.Trim();
            String? altAnimeDbPath = null;
            if (!String.IsNullOrWhiteSpace(envAltAnimeDb) && File.Exists(envAltAnimeDb)) {
                altAnimeDbPath = Path.GetFullPath(envAltAnimeDb);
            } else {
                // Probe common locations for anime.db specifically
                foreach (String? d in new[] {
                        exeDir, cwdDir, baseDir,
                        Path.GetFullPath(Path.Combine(exeDir, "..")),
                        Path.GetFullPath(Path.Combine(cwdDir, "..")),
                        Path.GetFullPath(Path.Combine(baseDir, "..")),
                    }) {
                    try {
                        String cand = Path.Combine(d!, "anime.db");
                        if (File.Exists(cand)) {
                            altAnimeDbPath = Path.GetFullPath(cand);
                            break;
                        }
                    } catch { /* ignore */ }
                }
            }

            if (String.IsNullOrWhiteSpace(animeDbPath) || !File.Exists(animeDbPath)) {
                List<String> diag = new List<String> {
                    $"exeDir={exeDir}",
                    $"cwdDir={cwdDir}",
                    $"baseDir={baseDir}",
                    $"env ANIME_DIMENSION_DB_PATH={Environment.GetEnvironmentVariable("ANIME_DIMENSION_DB_PATH") ?? "<not-set>"}",
                    $"env ANIME_DB_PATH={Environment.GetEnvironmentVariable("ANIME_DB_PATH") ?? "<not-set>"}"
                };
                throw new FileNotFoundException(
                        "Missing required anime-dimension.sqlite3 file. Probed but not found.\n" +
                        $"Last resolved path: {animeDbPath ?? "<unspecified>"}\n" +
                        String.Join("\n", diag) +
                        "\nHint: set ANIME_DIMENSION_DB_PATH to an absolute path for anime-dimension.sqlite3"
                );
            }

            if (String.IsNullOrWhiteSpace(usersDbPath) || !File.Exists(usersDbPath))
                throw new FileNotFoundException($"Missing required database file: {usersDbPath ?? "<unspecified>"}. Hint: set USERS_DB_PATH to an absolute path for users.db");



            // Validate both
            ValidateSqliteConnection(animeDbPath);
            ValidateSqliteConnection(usersDbPath);

            if (!TableExists(usersDbPath, "Users")) {
                throw new Exception("Table 'Users' not found in users.db");
            }

            // Register anime.db (main)
            builder.Services.AddDbContext<ApiDbContext>(options => options.UseSqlite($"Data Source={animeDbPath}"));
            // Also expose the resolved anime DB path for endpoints that query raw SQLite
            builder.Services.AddSingleton(new AnimeDbOptions { DbPath = animeDbPath });

            // If an alternate anime.db exists, register it as optional singleton for consumers that know about it
            if (!String.IsNullOrWhiteSpace(altAnimeDbPath) && File.Exists(altAnimeDbPath)) {
                // Best-effort validation; don't block startup if it fails
                try {
                    ValidateSqliteConnection(altAnimeDbPath);
                } catch { /* swallow */ }
                builder.Services.AddSingleton(implementationInstance: new AltAnimeDbOptions { DbPath = altAnimeDbPath });
            }

            // Register users.db (separate)
            builder.Services.AddDbContext<UsersDbContext>(options => options.UseSqlite($"Data Source={usersDbPath}"));


            // 1. Add HttpClientFactory for making HTTP requests to external APIs
            builder.Services.AddHttpClient();

            // 3. Add developer exception filter for detailed database error pages during development
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            // Register source-generated JSON metadata to make trimming safe.
            builder.Services.ConfigureHttpJsonOptions(options => {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, ASP.NETCoreWebApi.Serialization.AppJsonContext.Default);
            });

            builder.Services.AddCors(options => {
                options.AddDefaultPolicy(policy => {
                    policy.SetIsOriginAllowed(origin => {
                        // Allow production domains
                        if (origin == "https://anime-dimension.com" || origin.EndsWith(".anime-dimension.com", StringComparison.OrdinalIgnoreCase))
                            return true;
                        // Capacitor (mobile) // IOS and Android respectively
                        if (origin == "capacitor://anime-dimension.com" || origin == "https://localhost.com")
                            return true;
                        // Local development: allow common Astro/Vite ports and localhost/127.0.0.1 (http and https)
                        try {
                            Uri u = new Uri(origin);
                            Boolean isLocalHost = u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("127.0.0.1");
                            if (isLocalHost) {
                                // Typical ports: Astro 4321, Vite 5173/3000, custom 8080/3001
                                HashSet<Int32> allowedPorts = new HashSet<Int32> { 80, 443, 3000, 3001, 4321, 5173, 8080 };
                                if (allowedPorts.Contains(u.Port))
                                    return true;
                            }
                        } catch { /* ignore parse errors */ }

                        // Explicit localhost HTTPS without port
                        return origin == "https://localhost";
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
                });
            });

            WebApplication app = builder.Build();

            // --- API Endpoint Configuration ---

            app.UseCors(); // Enable CORS for the application

            app.Use(async (Microsoft.AspNetCore.Http.HttpContext context, Func<System.Threading.Tasks.Task> next) => {
                String origin = context.Request.Headers["Origin"].ToString();
                if (!String.IsNullOrEmpty(origin)) {
                    ILogger<Program> logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Request Origin: {Origin}", origin);
                    logger.LogInformation("Request Path: {Path}", context.Request.Path);
                    // Log the request method and headers
                    /*logger.LogInformation("Request Method: {Method}", context.Request.Method);
                    foreach (var header in context.Request.Headers) {
                        logger.LogInformation("Header: {Key} = {Value}", header.Key, header.Value);
                    }*/
                }
                await next();
            });

            app.MapAnimeEndpoints();            // Register the anime endpoints (now DB-backed)
            app.MapSuggestionsEndpoints();      // Register the suggestions endpoints (now DB-backed)
            app.MapValidationEndpoints();       // Register the validation endpoints
            app.MapNotificationsEndpoints();    // Register the notifications endpoints
            app.MapAuthEndpoints();             // Register the authentication endpoints
            app.MapLoginEndpoints();            // Register the login endpoints
            app.MapLogoutEndpoints();           // Register the logout endpoints
            app.MapUsersEndpoints();            // Register user profile + upload endpoints
            app.MapwwwEndpoints();              // Register the apis website endpoints

            app.Run();
        } catch (Exception ex) {
            Console.Error.WriteLine("Fatal error during application:");
            Console.Error.WriteLine(ex.ToString());

            Environment.Exit(1);
        }
    }
}
