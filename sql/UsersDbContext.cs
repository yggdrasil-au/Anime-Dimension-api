// Sql/UsersDbContext.cs
using ASP.NETCoreWebApi.Models;

namespace ASP.NETCoreWebApi.Sql;

public class UsersDbContext:DbContext {
    public UsersDbContext(DbContextOptions<UsersDbContext> options):base(options) { }
    public DbSet<UserSession> UserSessions {
        get; set;
    }

    public DbSet<User> Users {
        get; set;
    }
}
