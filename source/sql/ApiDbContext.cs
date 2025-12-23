// sql/ApiDbContext.cs

namespace ASP.NETCoreWebApi.Sql;

public class ApiDbContext:Microsoft.EntityFrameworkCore.DbContext {
    public ApiDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<ApiDbContext> options):base(options) { }
    public Microsoft.EntityFrameworkCore.DbSet<Models.Anime> Animes {
        get; set;
    }
    public Microsoft.EntityFrameworkCore.DbSet<Models.User> Users {
        get; set;
    }
    public Microsoft.EntityFrameworkCore.DbSet<Models.Notification> Notifications {
        get; set;
    }

}
