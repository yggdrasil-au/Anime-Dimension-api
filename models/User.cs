// models/User.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class User {
    public Int32 Id { get; set; }
    public required String UserId { get; set; }
    public required String Username { get; set; }
    public required String Email { get; set; }
    public required String PasswordHash { get; set; }
}