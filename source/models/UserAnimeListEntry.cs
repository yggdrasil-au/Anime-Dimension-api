// models/UserAnimeListEntry.cs
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[Table("UserAnimeList")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class UserAnimeListEntry {
    public Int32 Id { get; set; }
    public required string UserId { get; set; }
    public Int32 AnimeId { get; set; }
    public required string AnimeSlug { get; set; }
    public Int32 Status { get; set; } // 1=Watched, 2=Watching, 3=Dropped, 4=WantToWatch, 5=Stalled, 6=WontWatch
    public Int32 EpisodesWatched { get; set; }
    public double? Rating { get; set; } // 0.5 to 5.0
    public Int32 TimesWatched { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

