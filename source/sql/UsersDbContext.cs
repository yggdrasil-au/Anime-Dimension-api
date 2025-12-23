// Sql/UsersDbContext.cs
using ASP.NETCoreWebApi.Models;

namespace ASP.NETCoreWebApi.Sql;

public class UsersDbContext:Microsoft.EntityFrameworkCore.DbContext {
    public UsersDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<UsersDbContext> options):base(options) { }
    public Microsoft.EntityFrameworkCore.DbSet<UserSession> UserSessions {
        get; set;
    }

    public Microsoft.EntityFrameworkCore.DbSet<User> Users {
        get; set;
    }
}
