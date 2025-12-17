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
    public String? DocJson { get; set; }

    public String? ProfileUrl { get; set; }
    public String? State { get; set; } // "READ"/"UNREAD"
}


[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class NotificationDto {
    public NotificationData? notification { get; set; }
    public String? state { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class NotificationData {
    public Int32 id { get; set; }
    public Int32 user_account_id { get; set; }
    public Int32 type_id { get; set; }
    public String? created_at { get; set; }
    public DocData? doc { get; set; }
    public String? profile_url { get; set; }
    public String? time_ago { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class DocData {
    public String? announcement_link { get; set; }
    public String? announcement_header { get; set; }
}
