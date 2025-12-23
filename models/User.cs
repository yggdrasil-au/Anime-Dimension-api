// models/User.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class User {
    public Int32 Id { get; set; }
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
}