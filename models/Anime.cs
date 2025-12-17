// models/Anime.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class Anime {
    public Int32 Id {
        get; set;
    }
    public String Type { get; set; } = "anime";
    public required String Title {
        get; set;
    }
    public required String Url {
        get; set;
    }
    public required String ThumbnailUrl {
        get; set;
    }
    public required String Tooltip {
        get; set;
    }
}