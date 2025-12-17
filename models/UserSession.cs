// Models/UserSession.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class UserSession {
    public Int32 Id { get; set; }
    public String UserId { get; set; } = null!;
    public String SessionToken { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
