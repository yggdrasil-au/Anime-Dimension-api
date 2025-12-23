// models/Anime.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class Anime {
    public Int32 Id {
        get; set;
    }
    public string Type { get; set; } = "anime";
    public required string Title {
        get; set;
    }
    public required string Url {
        get; set;
    }
    public required string ThumbnailUrl {
        get; set;
    }
    public required string Tooltip {
        get; set;
    }
}