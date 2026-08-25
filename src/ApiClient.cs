using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OwlUsageTray;

internal sealed class OwlApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.owlai.tech/api/v1/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly SessionStore _sessionStore;
    private StoredSession? _session;

    public OwlApiClient(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
        _session = sessionStore.Load();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OwlUsageTray/1.0");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
    }

    public bool HasSession => _session is not null && !string.IsNullOrWhiteSpace(_session.RefreshToken);

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var envelope = await PostEnvelopeAsync<LoginResponse>(
            "auth/login",
            new { email, password },
            cancellationToken);

        if (envelope.Requires2Fa)
        {
            throw new InvalidOperationException("该账号启用了两步验证，当前版本暂不支持 TOTP 登录。");
        }

        if (string.IsNullOrWhiteSpace(envelope.AccessToken) ||
            string.IsNullOrWhiteSpace(envelope.RefreshToken))
        {
            throw new InvalidOperationException("登录成功，但服务器没有返回完整会话令牌。");
        }

        _session = new StoredSession
        {
            AccessToken = envelope.AccessToken,
            RefreshToken = envelope.RefreshToken,
            ExpiresAt = DateTimeOffset.Now.AddSeconds(Math.Max(60, envelope.ExpiresIn))
        };
        _sessionStore.Save(_session);
    }

    public async Task<ProgressResponse> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            throw new AuthenticationRequiredException("请先登录。");
        }

        if (_session.ExpiresAt <= DateTimeOffset.Now.AddMinutes(2))
        {
            await RefreshTokenAsync(cancellationToken);
        }

        ProgressResponse progress;
        using (var response = await SendAuthorizedGetAsync(
                   "subscriptions/progress?timezone=Asia%2FShanghai",
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var items = await ReadEnvelopeAsync<List<ProgressResponse>>(response, cancellationToken);
            if (items.Count == 0)
            {
                throw new InvalidOperationException("当前账号没有生效中的订阅套餐。");
            }

            progress = items[0];
        }

        // The compact progress endpoint intentionally omits reset-return quota.
        // The subscriptions page reads these two values from the active
        // subscription endpoint, so enrich the same refresh result here.
        try
        {
            using var response = await SendAuthorizedGetAsync(
                "subscriptions/active",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            // The endpoint returns data as an array, even though only one
            // active subscription is expected for this account.
            var activeItems = await ReadEnvelopeAsync<List<ActiveSubscriptionUsage>>(response, cancellationToken);
            var active = activeItems.FirstOrDefault(item =>
                progress.Progress.Id == 0 || item.Id == progress.Progress.Id);
            if (active is not null)
            {
                progress.Progress.Monthly.ResetReturnAmountUsd = active.ResetReturnAmountUsd;
                progress.Progress.Monthly.ResetReturnUsedUsd = active.ResetReturnUsedUsd;
            }
        }
        catch (AuthenticationRequiredException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            // Reset-return data is supplemental; keep the main progress usable
            // when this secondary request is temporarily unavailable.
        }
        catch (JsonException)
        {
            // Keep compatibility with older server response shapes.
        }
        catch (InvalidOperationException)
        {
            // A failure in the supplemental endpoint must not hide the main
            // progress response from the user.
        }

        return progress;
    }

    public void ClearSession()
    {
        _session = null;
        _sessionStore.Delete();
    }

    private async Task<HttpResponseMessage> SendAuthorizedGetAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await SendGetRequestAsync(path, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        await RefreshTokenAsync(cancellationToken);
        response = await SendGetRequestAsync(path, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        throw new AuthenticationRequiredException("登录状态已失效，请重新登录。");
    }

    private async Task<HttpResponseMessage> SendGetRequestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            lastResponse?.Dispose();
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);
            request.Headers.TryAddWithoutValidation("X-User-UI-Request", "1");

            lastResponse = await _http.SendAsync(request, cancellationToken);
            if (lastResponse.StatusCode is not HttpStatusCode.NotFound and
                not HttpStatusCode.BadGateway and
                not HttpStatusCode.ServiceUnavailable)
            {
                return lastResponse;
            }

            if (attempt < 2)
            {
                await Task.Delay(350 * (attempt + 1), cancellationToken);
            }
        }

        return lastResponse!;
    }

    private async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (_session is null || string.IsNullOrWhiteSpace(_session.RefreshToken))
        {
            throw new AuthenticationRequiredException("没有可续期的登录状态。");
        }

        try
        {
            var refreshed = await PostEnvelopeAsync<LoginResponse>(
                "auth/refresh",
                new { refresh_token = _session.RefreshToken },
                cancellationToken);

            _session.AccessToken = refreshed.AccessToken;
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                _session.RefreshToken = refreshed.RefreshToken;
            }
            _session.ExpiresAt = DateTimeOffset.Now.AddSeconds(Math.Max(60, refreshed.ExpiresIn));
            _sessionStore.Save(_session);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ClearSession();
            throw new AuthenticationRequiredException("登录状态已失效，请重新登录。", exception);
        }
    }

    private async Task<T> PostEnvelopeAsync<T>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadEnvelopeAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken);
        if (envelope is null)
        {
            throw new InvalidOperationException("服务器返回了空响应。");
        }

        if (envelope.Code != 0 || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope.Message ?? $"接口错误：{envelope.Code}");
        }

        return envelope.Data;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException(string message) : base(message) { }
    public AuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException) { }
}
