// sql/ApiDbContext.cs
using ASP.NETCoreWebApi.Models;

namespace ASP.NETCoreWebApi.Sql;

public class ApiDbContext:DbContext {
    public ApiDbContext(DbContextOptions<ApiDbContext> options):base(options) { }
    public DbSet<Anime> Animes {
        get; set;
    }
    public DbSet<User> Users {
        get; set;
    }
    public DbSet<Notification> Notifications {
        get; set;
    }

}
