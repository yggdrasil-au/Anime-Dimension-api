// sql/ApiDbContext.cs

namespace ASP.NETCoreWebApi.Sql;

public class ApiDbContext:DbContext {
    public ApiDbContext(DbContextOptions<ApiDbContext> options):base(options) { }
    public DbSet<Models.Anime> Animes {
        get; set;
    }
    public DbSet<Models.User> Users {
        get; set;
    }
    public DbSet<Models.Notification> Notifications {
        get; set;
    }

}
