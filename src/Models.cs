using System.Text.Json.Serialization;

namespace OwlUsageTray;

internal sealed class ApiEnvelope<T>
{
    public int Code { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

internal sealed class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("requires_2fa")]
    public bool Requires2Fa { get; set; }
}

internal sealed class ProgressResponse
{
    public SubscriptionInfo Subscription { get; set; } = new();
    public UsageProgress Progress { get; set; } = new();
}

internal sealed class SubscriptionInfo
{
    public long Id { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    public SubscriptionGroup Group { get; set; } = new();
}

internal sealed class SubscriptionGroup
{
    public string Name { get; set; } = "";
}

internal sealed class UsageProgress
{
    public long Id { get; set; }

    [JsonPropertyName("group_name")]
    public string GroupName { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("expires_in_days")]
    public int ExpiresInDays { get; set; }

    public UsagePeriod Daily { get; set; } = new();
    public UsagePeriod Weekly { get; set; } = new();
    public UsagePeriod Monthly { get; set; } = new();
}

internal sealed class UsagePeriod
{
    [JsonPropertyName("limit_usd")]
    public decimal LimitUsd { get; set; }

    [JsonPropertyName("used_usd")]
    public decimal UsedUsd { get; set; }

    [JsonPropertyName("remaining_usd")]
    public decimal RemainingUsd { get; set; }

    public decimal Percentage { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset ResetsAt { get; set; }

    [JsonPropertyName("resets_in_seconds")]
    public long ResetsInSeconds { get; set; }

    [JsonPropertyName("reset_return_amount_usd")]
    public decimal ResetReturnAmountUsd { get; set; }

    [JsonPropertyName("reset_return_used_usd")]
    public decimal ResetReturnUsedUsd { get; set; }
}

internal sealed class ActiveSubscriptionUsage
{
    public long Id { get; set; }

    [JsonPropertyName("reset_return_amount_usd")]
    public decimal ResetReturnAmountUsd { get; set; }

    [JsonPropertyName("reset_return_used_usd")]
    public decimal ResetReturnUsedUsd { get; set; }
}

internal sealed class StoredSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class AppSettings
{
    public const int DefaultAcrylicOpacityPercent = 69;
    public const int DefaultBlurStrength = 70;
    public const int DefaultRefreshSeconds = 60;

    public bool WidgetVisible { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public int? WidgetX { get; set; }
    public int? WidgetY { get; set; }
    public int WidgetWidth { get; set; } = WidgetForm.DefaultWidgetWidth;
    public int WidgetHeight { get; set; } = WidgetForm.DefaultWidgetHeight;
    public int AcrylicOpacityPercent { get; set; } = DefaultAcrylicOpacityPercent;
    public int BlurStrength { get; set; } = DefaultBlurStrength;
    public int RefreshSeconds { get; set; } = DefaultRefreshSeconds;
}
