// Sql/UsersDbContext.cs
using ASP.NETCoreWebApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ASP.NETCoreWebApi.Sql;

public class UsersDbContext:DbContext {
    public UsersDbContext(DbContextOptions<UsersDbContext> options):base(options) { }
    public DbSet<UserSession> UserSessions {
        get; set;
    }

    public DbSet<User> Users {
        get; set;
    }

    public DbSet<UserAnimeListEntry> UserAnimeLists {
        get; set;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<UserAnimeListEntry>().ToTable("UserAnimeList");
    }
}
