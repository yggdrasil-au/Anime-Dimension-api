// models/Notification.cs
using System;
using System.Diagnostics.CodeAnalysis;

namespace ASP.NETCoreWebApi.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class Notification {
    public Int32 Id { get; set; }
    public Int32? UserAccountId { get; set; }
    public Int32 TypeId { get; set; }
    public DateTime CreatedAt { get; set; }

    // JSON fields stored in DB as text (doc, etc.)
    public string? DocJson { get; set; }

    public string? ProfileUrl { get; set; }
    public string? State { get; set; } // "READ"/"UNREAD"
}


[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class NotificationDto {
    public NotificationData? notification { get; set; }
    public string? state { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class NotificationData {
    public Int32 id { get; set; }
    public Int32 user_account_id { get; set; }
    public Int32 type_id { get; set; }
    public string? created_at { get; set; }
    public DocData? doc { get; set; }
    public string? profile_url { get; set; }
    public string? time_ago { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class DocData {
    public string? announcement_link { get; set; }
    public string? announcement_header { get; set; }
}
