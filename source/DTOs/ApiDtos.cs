// DTOs/ApiDtos.cs
using System;
using System.Collections.Generic;

namespace ASP.NETCoreWebApi.DTOs;

public sealed class ErrResponse {
    public required string status { get; init; } = "err";
    public required string msg { get; init; }
}

public sealed class SimpleStatusResponse {
    public required string status { get; init; }
    public object? data { get; init; }
}

public sealed class OkResponse<T> {
    public string status { get; init; } = "ok";
    public required T data { get; init; }
}

// Suggestions shapes
public sealed class SuggestionItem {
    public long id { get; init; }
    public string slug { get; init; } = string.Empty;
    public string title { get; init; } = string.Empty;
    public string? url { get; init; }
    public string? thumbnailUrl { get; init; }
    public string? year { get; init; }
    public string? type { get; init; }
    public string? altTitle { get; init; }
    public string? altText { get; init; }
    public string? summary { get; init; }
    public string? synopsis { get; init; }
    public string? studio { get; init; }
    public double? rating { get; init; }
    public long? dataId { get; init; }
    public string? dataEpisodeType { get; init; }
    public int? dataTotalEpisodes { get; init; }
    public string[]? tags { get; init; }
    public string[]? notes { get; init; }
}

public sealed class SuggestionsListResponse {
    public string status { get; init; } = "ok";
    public List<SuggestionItem> list { get; init; } = new();
    public bool? success { get; init; }
    public string? season { get; init; }
}

// Users endpoints
public sealed class UserMeData {
    public required string user_id { get; init; }
    public required string user_name { get; init; }
    public string? avatar_url { get; init; }
    public string? banner_url { get; init; }
}

public sealed class UserStatsData {
    public long watched { get; init; }
    public long watching { get; init; }
    public long want { get; init; }
    public long stalled { get; init; }
    public long dropped { get; init; }
    public long wont { get; init; }
}

public sealed class UserProfileData {
    public required string user_id { get; init; }
    public required string user_name { get; init; }
    public string? avatar_url { get; init; }
    public string? banner_url { get; init; }
    public string? joined_at { get; init; }
    public long watch_minutes { get; init; }
    public required UserStatsData stats { get; init; }
    public required Dictionary<string, long> ratings { get; init; }
    public long ratings_total { get; init; }
}

// Login shapes
public class LoginOkData {
    public required string user_id { get; init; }
    public required string user_name { get; init; }
}
public sealed class LoginOkDataMobile : LoginOkData {
    public required string session_token { get; init; }
}

public sealed class ValidateStateData {
    public required string user_id { get; init; }
    public required string user_name { get; init; }
    public DateTime expiresAt { get; init; }
}

// Notifications list envelope
public sealed class NotificationsEnvelope {
    public string status { get; init; } = "ok";
    public required NotificationsData data { get; init; }
}
public sealed class NotificationsData {
    public required List<ASP.NETCoreWebApi.Models.NotificationDto> notifications { get; init; }
    public int new_count { get; init; }
}

// Debug file-not-found payload for /www
public sealed class DebugNotFoundResponse {
    public string message { get; init; } = "File not found";
    public string? requested { get; init; }
    public string? root { get; init; }
    public List<string>? candidates { get; init; }
    public string? cwd { get; init; }
}

// Auth/signup token
public sealed class SignupTokenData {
    public required string token { get; init; }
    public int duration { get; init; }
}

// Small wrappers for file upload responses
public sealed class AvatarUrlData { public required string avatar_url { get; init; } }
public sealed class BannerUrlData { public required string banner_url { get; init; } }
