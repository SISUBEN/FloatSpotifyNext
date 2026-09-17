using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FloatSpotify.Localization;

namespace FloatSpotify.Playback;

internal sealed class SpotifyAuthClient
{
    private const string AccountsBase = "https://accounts.spotify.com";
    private const string RedirectUri = "http://127.0.0.1:8888/callback";
    private const string Scopes =
        "user-read-playback-state user-read-currently-playing user-modify-playback-state";

    private readonly Func<string?> _clientIdProvider;
    private readonly HttpClient _httpClient;
    private readonly SpotifySessionStore _sessionStore;
    private readonly SemaphoreSlim _authGate = new(1, 1);
    private SpotifySession? _session;
    private string? _activeClientId;

    public SpotifyAuthClient(
        Func<string?> clientIdProvider,
        HttpClient httpClient,
        SpotifySessionStore sessionStore)
    {
        _clientIdProvider = clientIdProvider;
        _httpClient = httpClient;
        _sessionStore = sessionStore;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            var clientId = GetClientId();
            if (!string.Equals(_activeClientId, clientId, StringComparison.Ordinal))
            {
                _session = _sessionStore.Load(clientId);
                _activeClientId = clientId;
            }

            if (_session is { AccessToken.Length: > 0 } current &&
                current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return current.AccessToken;
            }

            if (_session is { RefreshToken.Length: > 0 } refreshable)
            {
                try
                {
                    _session = await RefreshAsync(clientId, refreshable.RefreshToken, cancellationToken);
                    _sessionStore.Save(_session);
                    return _session.AccessToken;
                }
                catch (SpotifyAuthorizationException)
                {
                    _sessionStore.Clear();
                    _session = null;
                }
            }

            _session = await AuthorizeAsync(clientId, cancellationToken);
            _sessionStore.Save(_session);
            return _session.AccessToken;
        }
        finally
        {
            _authGate.Release();
        }
    }

    public void InvalidateAccessToken(string accessToken)
    {
        if (_session?.AccessToken != accessToken)
            return;

        _session = _session with
        {
            AccessToken = string.Empty,
            ExpiresAt = DateTimeOffset.MinValue
        };
    }

    public async Task<string> ForceAuthorizationAsync(CancellationToken cancellationToken)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            var clientId = GetClientId();
            var replacement = await AuthorizeAsync(clientId, cancellationToken);
            _session = replacement;
            _activeClientId = clientId;
            _sessionStore.Save(replacement);
            return replacement.AccessToken;
        }
        finally
        {
            _authGate.Release();
        }
    }

    private async Task<SpotifySession> AuthorizeAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:8888/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException exception)
        {
            throw new SpotifyAuthorizationException(
                "SpotifyAuth_PortInUse",
                exception);
        }

        var authorizationUrl =
            $"{AccountsBase}/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&state={Uri.EscapeDataString(state)}";

        Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

        try
        {
            while (true)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                if (!string.Equals(context.Request.Url?.AbsolutePath, "/callback", StringComparison.Ordinal))
                {
                    await WriteBrowserResponseAsync(context.Response, HttpStatusCode.NotFound, "Not found.");
                    continue;
                }

                var error = context.Request.QueryString["error"];
                var returnedState = context.Request.QueryString["state"];
                var code = context.Request.QueryString["code"];

                if (!string.Equals(returnedState, state, StringComparison.Ordinal))
                {
                    await WriteBrowserResponseAsync(
                        context.Response,
                        HttpStatusCode.BadRequest,
                        Loc.T("SpotifyAuth_Browser_StateMismatch"));
                    throw new SpotifyAuthorizationException("SpotifyAuth_StateMismatch");
                }

                if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
                {
                    await WriteBrowserResponseAsync(
                        context.Response,
                        HttpStatusCode.BadRequest,
                        Loc.T("SpotifyAuth_Browser_Cancelled"));
                    throw new SpotifyAuthorizationException("SpotifyAuth_Cancelled");
                }

                await WriteBrowserResponseAsync(
                    context.Response,
                    HttpStatusCode.OK,
                    Loc.T("SpotifyAuth_Browser_Success"));
                return await ExchangeCodeAsync(clientId, code, verifier, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<SpotifySession> ExchangeCodeAsync(
        string clientId,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        });

        return await RequestTokenAsync(clientId, content, null, cancellationToken);
    }

    private async Task<SpotifySession> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        return await RequestTokenAsync(clientId, content, refreshToken, cancellationToken);
    }

    private async Task<SpotifySession> RequestTokenAsync(
        string clientId,
        HttpContent content,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(
            $"{AccountsBase}/api/token",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SpotifyAuthorizationException("SpotifyAuth_Expired");

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
            ? expiresElement.GetInt32()
            : 3600;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : existingRefreshToken;

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new SpotifyAuthorizationException("SpotifyAuth_NoAccessToken");

        return new SpotifySession(
            clientId,
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string message)
    {
        var body = Encoding.UTF8.GetBytes(
            $"<!doctype html><meta charset=\"utf-8\"><title>FloatSpotify</title>" +
            $"<body style=\"font:18px system-ui;padding:48px;background:#111;color:#fff\">{WebUtility.HtmlEncode(message)}</body>");

        response.StatusCode = (int)statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
        response.Close();
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string GetClientId()
    {
        var clientId = _clientIdProvider()?.Trim();
        if (clientId is not { Length: 32 } || clientId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new SpotifyAuthorizationException("SpotifyAuth_MissingClientId");
        }

        return clientId;
    }
}

/// <summary>
/// Spotify 授权失败。<see cref="Exception.Message"/> 是**取词那一刻**的译文，
/// 所以界面上要跨语言存活的地方（比如悬浮窗里的授权错误）必须存 <see cref="Key"/> 再自己翻译。
/// </summary>
internal sealed class SpotifyAuthorizationException : Exception
{
    public SpotifyAuthorizationException(string messageKey)
        : base(Loc.T(messageKey)) => Key = messageKey;

    public SpotifyAuthorizationException(string messageKey, Exception innerException)
        : base(Loc.T(messageKey), innerException) => Key = messageKey;

    /// <summary><see cref="Strings"/> 里的文案 key。</summary>
    public string Key { get; }
}
