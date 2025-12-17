// api/notifications.cs

namespace ASP.NETCoreWebApi;
using ASP.NETCoreWebApi.Models;
using ASP.NETCoreWebApi.Sql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ASP.NETCoreWebApi.Serialization;

public static class NotificationsEndpoints {
    public static void MapNotificationsEndpoints(this IEndpointRouteBuilder app) {

        // TODO: Implement the mark read functionality
        /*getMarkReadUrl(e) {
            return `https://api.anime-dimension.com/api/notifications/read?notification_id=${e}`;
        }*/

        app.MapGet("/api/notifications", async (ApiDbContext db) => {
            System.Collections.Generic.List<Notification> notifications = await db.Notifications
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            DateTime now = DateTime.UtcNow;

            System.Collections.Generic.IEnumerable<NotificationDto> dtoList = notifications.Select(n => {
                JsonTypeInfo<DocData> docInfo = (JsonTypeInfo<DocData>)AppJsonContext.Default.GetTypeInfo(typeof(DocData))!;
                DocData? doc = JsonSerializer.Deserialize(n.DocJson ?? "{}", docInfo);

                return new NotificationDto {
                    state = n.State,
                    notification = new NotificationData {
                        id = n.Id,
                        user_account_id = n.UserAccountId ?? 0,
                        type_id = n.TypeId,
                        created_at = n.CreatedAt.ToString("o"), // ISO format
                        doc = doc,
                        profile_url = n.ProfileUrl,
                        time_ago = GetTimeAgo(n.CreatedAt, now)
                    }
                };
            });

            DTOs.NotificationsEnvelope payload = new DTOs.NotificationsEnvelope {
                data = new DTOs.NotificationsData {
                    notifications = dtoList.ToList(),
                    new_count = 0
                }
            };
            return Results.Json(payload, (JsonTypeInfo<DTOs.NotificationsEnvelope>)AppJsonContext.Default.GetTypeInfo(typeof(DTOs.NotificationsEnvelope))!);
        });
    }

    private static String GetTimeAgo(DateTime created, DateTime now) {
        TimeSpan diff = now - created;

        if (diff.TotalDays >= 60)
            return $"{(Int32)(diff.TotalDays / 30)}mo";
        if (diff.TotalDays >= 30)
            return "1mo";
        return diff.TotalDays >= 14
            ? $"{(Int32)(diff.TotalDays / 7)}w"
            : diff.TotalDays >= 1
            ? $"{(Int32)diff.TotalDays}d"
            : diff.TotalHours >= 1 ? $"{(Int32)diff.TotalHours}h" : diff.TotalMinutes >= 1 ? $"{(Int32)diff.TotalMinutes}m" : "just now";
    }
}
