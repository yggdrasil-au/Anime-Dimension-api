// Program.cs

global using Microsoft.EntityFrameworkCore;


using System.IO;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


using Microsoft.AspNetCore.Builder;
using System;

namespace ASP.NETCoreWebApi;

internal class Program {
    protected Program() {}

    static void ValidateSqliteConnection(string dbPath) {
        using Microsoft.Data.Sqlite.SqliteConnection? connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString: $"Data Source={dbPath}");
        try {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 1;";
            cmd.ExecuteScalar(); // Run a trivial query
        } catch (System.Exception ex) {
            throw new System.InvalidOperationException(message: $"Failed to open or validate SQLite DB at {dbPath}", ex);
        }
    }
    static bool TableExists(string dbPath, string tableName) {
        using Microsoft.Data.Sqlite.SqliteConnection conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString: $"Data Source={dbPath}");
        conn.Open();

        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = $table";
        cmd.Parameters.AddWithValue(parameterName: "$table", tableName);

        long? result = (long?)cmd.ExecuteScalar();
        return result > 0;
    }

    static bool FilesAreIdentical(string a, string b) {
        if (!File.Exists(a) || !File.Exists(b)) {
            return false;
        }
        FileInfo fa = new FileInfo(a);
        FileInfo fb = new FileInfo(b);
        if (fa.Length != fb.Length) {
            return false;
        }
        using System.Security.Cryptography.SHA256 ha = System.Security.Cryptography.SHA256.Create();
        using System.Security.Cryptography.SHA256 hb = System.Security.Cryptography.SHA256.Create();
        using FileStream sa = File.OpenRead(a);
        using FileStream sb = File.OpenRead(b);
        byte[] da = ha.ComputeHash(sa);
        byte[] db = hb.ComputeHash(sb);
        return System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(da, db);
    }

    public static void Main(string[] args) {
        try {
            // Set the culture to ensure consistent date and number formatting
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en-US");
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en-US");

            Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

            // Tell .NET to load your secret file if it exists
            builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

            // --- Service Configuration ---
            // Resolve important directories
            string baseDir = System.AppContext.BaseDirectory; // In single-file, this is the bundle extraction dir
            Console.WriteLine($"Info: Application base directory: {baseDir}");
            string exePath = System.Environment.ProcessPath ?? string.Empty; // Path to the launched executable
            Console.WriteLine($"Info: Executable path: {exePath}");
            string exeDir = string.IsNullOrEmpty(exePath) ? baseDir : (Path.GetDirectoryName(exePath) ?? baseDir);
            Console.WriteLine($"Info: Executable directory: {exeDir}");
            string cwdDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Info: Current working directory: {cwdDir}");

            // Helper to pick the first existing file from a list of candidates
            static string? FirstExisting(params string[] candidates) {
                foreach (string p in candidates) {
                    try {
                        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                            return Path.GetFullPath(p);
                    } catch {
                        Console.Error.WriteLine($"Warning: error checking existence of file path: {p}");
                    }
                }
                return null;
            }

            static string? ProbeForAnimeDimensionDb(IEnumerable<string> dirs) {
                foreach (string d in dirs) {
                    if (string.IsNullOrWhiteSpace(d))
                        continue;
                    try {
                        string a = Path.Combine(d, "anime-dimension.sqlite3");
                        if (File.Exists(a))
                            return Path.GetFullPath(a);
                    } catch {
                        /* ignore */
                    }
                }
                return null;
            }

            // Build a probe list of likely directories
            List<string?> probeDirs = new List<string?> {
                exeDir,
                cwdDir,
                baseDir,
                Path.GetFullPath(Path.Combine(exeDir, "..")),
                Path.GetFullPath(Path.Combine(cwdDir, "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..")),
                Path.GetFullPath(Path.Combine(cwdDir, "Anime-dimension-api")),
                Path.GetFullPath(Path.Combine(exeDir, "Anime-dimension-api"))
            };

            string? animeDbPath = ProbeForAnimeDimensionDb(probeDirs!);
            Console.WriteLine($"Info: Probed anime-dimension.sqlite3 path: {animeDbPath ?? "<not found>"}");

            // if still not found, check env vars (ANIME_DIMENSION_DB_PATH or ANIME_DB_PATH)
            if (animeDbPath is null) {
                Console.WriteLine("Info: anime-dimension.sqlite3 not found in common locations, checking environment variables.");
                // Try to source from upstream Anime-Dimension-Database-Orchestrator DB if present, then copy next to the executable
                string[] apDbCandidates = new[] {
                    //Path.Combine(exeDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),
                    //Path.Combine(baseDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),
                    //Path.Combine(cwdDir, "..", "Anime-Dimension-Database-Orchestrator", "anime-dimension.sqlite3"),

                    // this requires the dotnet command be executed from the Anime-Dimension-api folder
                    Path.Combine(cwdDir, "..", "db", "anime-dimension.sqlite3"), // dev environment location
                };
                string? apDb = FirstExisting(apDbCandidates);
                Console.WriteLine($"Info: Probed Anime-Dimension-Database-Orchestrator anime-dimension.sqlite3 path: {apDb ?? "<not found>"}");
                if (!string.IsNullOrEmpty(apDb)) {
                    Console.WriteLine($"Info: Found anime-dimension.sqlite3 from Anime-Dimension-Database-Orchestrator at {apDb}, copying to working and executable directories.");
                    string target = Path.Combine(cwdDir, "anime-dimension.sqlite3");
                    try {
                        if (!File.Exists(target) || !FilesAreIdentical(apDb, target)) {
                            File.Copy(apDb, target, overwrite: true);
                            System.Console.WriteLine($"Copied anime-dimension.sqlite3 from {apDb} to {target}");
                        }
                        animeDbPath = target;
                        Console.WriteLine($"Info: anime-dimension.sqlite3 now at {animeDbPath}");
                    } catch (System.Exception ex) {
                        System.Console.Error.WriteLine(ex.ToString());
                        System.Console.WriteLine($"Warning: failed to copy anime-dimension.sqlite3 from {apDb} to {target}. Will continue probing other locations.");
                    }
                    string exeTarget = Path.Combine(exeDir, "anime-dimension.sqlite3");
                    try {
                        if (!File.Exists(exeTarget) || !FilesAreIdentical(apDb, exeTarget)) {
                            Directory.CreateDirectory(exeDir);
                            File.Copy(apDb, exeTarget, overwrite: true);
                        }
                        animeDbPath = exeTarget;
                        Console.WriteLine($"Info: anime-dimension.sqlite3 now at {animeDbPath}");
                    } catch (System.Exception ex) {
                        System.Console.Error.WriteLine(ex.ToString());
                        System.Console.WriteLine($"Warning: failed to copy anime-dimension.sqlite3 from {apDb} to {exeTarget}. Will continue probing other locations.");
                    }
                } else {
                    Console.WriteLine("Info: Anime-Dimension-Database-Orchestrator anime-dimension.sqlite3 not found, skipping copy step.");
                }
            } else {
                Console.WriteLine($"Info: anime-dimension.sqlite3 found at {animeDbPath}");
            }

            // Resolve users DB similarly (env var optional: USERS_DB_PATH)
            //string? envUsersDb = System.Environment.GetEnvironmentVariable("USERS_DB_PATH")?.Trim();
            string? usersDbPath = FirstExisting(
                Path.Combine(exeDir, "users.db"), // exeDir is the path to the launched executable
                Path.Combine(cwdDir, "users.db"), // cwdDir is the current working directory
                Path.Combine(baseDir, "users.db"), // baseDir is the directory of the application base (in single-file, the bundle extraction dir)

                Path.Combine(cwdDir, "..", "db", "users.db") // dev environment location
            );
            // if still not found, default to exeDir/users.db
            if (usersDbPath is null) {
                usersDbPath = Path.Combine(exeDir, "users.db"); // default expected location
                // log the path used
                System.Console.WriteLine($"Info: users.db not found in common locations, defaulting to {usersDbPath}");
            } else {
                // log the path found
                System.Console.WriteLine($"Info: users.db found at {usersDbPath}");
            }


            if (string.IsNullOrWhiteSpace(animeDbPath) || !File.Exists(animeDbPath)) {
                List<string> diag = new List<string> {
                    $"exeDir={exeDir}",
                    $"cwdDir={cwdDir}",
                    $"baseDir={baseDir}",
                    //$"env ANIME_DIMENSION_DB_PATH={System.Environment.GetEnvironmentVariable("ANIME_DIMENSION_DB_PATH") ?? "<not-set>"}",
                    //$"env ANIME_DB_PATH={System.Environment.GetEnvironmentVariable("ANIME_DB_PATH") ?? "<not-set>"}"
                };
                throw new FileNotFoundException (
                    "Missing required anime-dimension.sqlite3 file. Probed but not found.\n" +
                    $"Last resolved path: {animeDbPath ?? "<unspecified>"}\n" +
                    string.Join("\n", diag) +
                    "\nHint: set ANIME_DIMENSION_DB_PATH to an absolute path for anime-dimension.sqlite3"
                );
            }

            if (string.IsNullOrWhiteSpace(usersDbPath) || !File.Exists(usersDbPath)) {
                throw new FileNotFoundException($"Missing required database file: {usersDbPath ?? "<unspecified>"}. ensure users.db exists at the required location.");
            }

            // Validate both
            ValidateSqliteConnection(animeDbPath);
            ValidateSqliteConnection(usersDbPath);

            if (!TableExists(usersDbPath, tableName: "Users")) {
                throw new System.Exception(message: "Table 'Users' not found in users.db");
            }

            // Also expose the resolved anime DB path for endpoints that query raw SQLite
            builder.Services.AddSingleton(implementationInstance: new AnimeDbOptions { DbPath = animeDbPath });

            // Register the main anime DB context
            builder.Services.AddDbContext<Sql.ApiDbContext>(options => options.UseSqlite($"Data Source={animeDbPath}"));

            // Register users.db (separate)
            builder.Services.AddDbContext<Sql.UsersDbContext>(options => options.UseSqlite($"Data Source={usersDbPath}"));

            // 1. Add HttpClientFactory for making HTTP requests to external APIs
            builder.Services.AddHttpClient();

            // 3. Add developer exception filter for detailed database error pages during development
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            // Register source-generated JSON metadata to make trimming safe.
            builder.Services.ConfigureHttpJsonOptions (options => {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, ASP.NETCoreWebApi.Serialization.AppJsonContext.Default);
            });

            builder.Services.AddCors(options => {
                options.AddDefaultPolicy(policy => {
                    policy.SetIsOriginAllowed(origin => {
                        // Allow production domains
                        if (origin == "https://anime-dimension.com" || origin.EndsWith(".anime-dimension.com", System.StringComparison.OrdinalIgnoreCase)) {
                            return true;
                        }
                        // Capacitor (mobile) // IOS and Android respectively
                        if (origin == "capacitor://anime-dimension.com" || origin == "https://localhost.com") {
                            return true;
                        }
                        // Local development: allow common Astro/Vite ports and localhost/127.0.0.1 (http and https)
                        try {
                            System.Uri u = new System.Uri(origin);
                            bool isLocalHost = u.Host.Equals("localhost", System.StringComparison.OrdinalIgnoreCase) || u.Host.Equals("127.0.0.1");
                            if (isLocalHost) {
                                // Typical ports: Astro 4321, Vite 5173/3000, custom 8080/3001
                                HashSet<int> allowedPorts = new HashSet<int> { 80, 443, 3000, 3001, 4321, 5173, 8080 };
                                if (allowedPorts.Contains(u.Port)) {
                                    return true;
                                }
                            }
                        } catch {
                            Console.Error.WriteLine($"Warning: failed to parse Origin header value: {origin}");
                        }

                        // Explicit localhost HTTPS without port
                        return origin == "https://localhost";
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
                });
            });

            Microsoft.AspNetCore.Builder.WebApplication app = builder.Build();

            // Load the key into your static class once at startup
            ASP.NETCoreWebApi.helpers.Mail.ApiKey = app.Configuration["MailChannels:ApiKey"];

            // --- API Endpoint Configuration ---

            app.UseCors(); // Enable CORS for the application

            app.Use(async (Microsoft.AspNetCore.Http.HttpContext context, System.Func<System.Threading.Tasks.Task> next) => {
                string origin = context.Request.Headers["Origin"].ToString();
                if (!string.IsNullOrEmpty(origin)) {
                    Microsoft.Extensions.Logging.ILogger<Program> logger = context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
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
#if DEBUG
            app.MapTestEndpoints();             // Register the test endpoints
#endif

            app.Run();
        } catch (System.Exception ex) {
            System.Console.Error.WriteLine("Fatal error during application:");
            System.Console.Error.WriteLine(ex.ToString());

            System.Environment.Exit(1);
        }
    }
}
