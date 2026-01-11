// Serialization/AppJsonContext.cs
using System.Text.Json.Serialization;

namespace ASP.NETCoreWebApi.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.ErrResponse))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.SimpleStatusResponse))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<string?>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.LoginOkData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.LoginOkDataMobile>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.ValidateStateData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.UserMeData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.UserProfileData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.AvatarUrlData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.BannerUrlData>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.OkResponse<ASP.NETCoreWebApi.DTOs.SignupTokenData[]>))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.SuggestionsListResponse))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.SuggestionItem[]))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.DebugNotFoundResponse))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.AnimeEndpoints.AnimeDto))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.AnimeEndpoints.AnimeDto[]))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.LoginEndpoints.AnimeLoginRequest))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.Models.DocData))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.Models.NotificationDto))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.Models.NotificationData))]
[JsonSerializable(typeof(ASP.NETCoreWebApi.DTOs.NotificationsEnvelope))]
public partial class AppJsonContext : JsonSerializerContext { }
