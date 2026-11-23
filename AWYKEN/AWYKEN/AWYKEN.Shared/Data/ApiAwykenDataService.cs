using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AWYKEN.Shared.Services;

namespace AWYKEN.Shared.Data;

/// <summary>
/// HTTP-backed implementation of the existing data-service contract. It lets the MAUI
/// screens keep their current API while all database work moves to AWYKEN.Web.
/// </summary>
public sealed class ApiAwykenDataService : DispatchProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HttpClient _http = null!;
    private IApiTokenStore _tokens = null!;
    private LocalAccountSession _session = null!;
    private DashboardState _state = null!;

    public static IAwykenDataService Create(
        HttpClient http, IApiTokenStore tokens, LocalAccountSession session, DashboardState state)
    {
        var proxy = Create<IAwykenDataService, ApiAwykenDataService>();
        var implementation = (ApiAwykenDataService)(object)proxy;
        implementation._http = http;
        implementation._tokens = tokens;
        implementation._session = session;
        implementation._state = state;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new InvalidOperationException("The API data service received an invalid operation.");

        args ??= [];
        if (targetMethod.Name == "get_IsAuthenticated")
            return _session.IsAuthenticated;
        if (targetMethod.Name == nameof(IAwykenDataService.InitializeAsync))
            return InitializeAsync(GetCancellationToken(args));
        if (targetMethod.Name == nameof(IAwykenDataService.SignInAsync))
            return SignInAsync((string)args[0]!, (string)args[1]!, GetCancellationToken(args));
        if (targetMethod.Name == nameof(IAwykenDataService.SignUpAsync))
            return SignUpAsync((string)args[0]!, (string)args[1]!, (string)args[2]!, GetCancellationToken(args));
        if (targetMethod.Name == nameof(IAwykenDataService.SignOutAsync))
            return SignOutAsync(GetCancellationToken(args));

        var businessArguments = args.Where((_, index) =>
            targetMethod.GetParameters()[index].ParameterType != typeof(CancellationToken)).ToArray();
        var cancellationToken = GetCancellationToken(args);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
            return CallVoidAsync(targetMethod.Name, businessArguments, cancellationToken);

        var resultType = returnType.GetGenericArguments()[0];
        return typeof(ApiAwykenDataService)
            .GetMethod(nameof(CallAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(this, [targetMethod.Name, businessArguments, cancellationToken]);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ApiSessionSnapshot snapshot;
        try
        {
            snapshot = await ReadSessionAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            await _tokens.ClearAsync(cancellationToken);
            _session.SignOut();
            snapshot = await ReadSessionAsync(cancellationToken);
        }
        if (snapshot is null)
            throw new InvalidOperationException("The AWYKEN server returned an empty session response.");

        if (snapshot.IsAuthenticated && snapshot.ProfileId is int profileId
            && await _tokens.GetAsync(cancellationToken) is not null)
            _session.SignIn(profileId);
        else
        {
            _session.SignOut();
            await _tokens.ClearAsync(cancellationToken);
        }
        _state.ReplaceFromApiSnapshot(snapshot.Dashboard);
    }

    private async Task<ApiSessionSnapshot> ReadSessionAsync(CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/api/v1/auth/session", null, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ApiSessionSnapshot>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The AWYKEN server returned an empty session response.");
    }

    private async Task<string?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var reply = await AuthenticateAsync("/api/v1/auth/sign-in",
            new ApiSignInRequest(email, password), cancellationToken);
        return await ApplyAuthReplyAsync(reply, cancellationToken);
    }

    private async Task<string?> SignUpAsync(string displayName, string email, string password,
        CancellationToken cancellationToken)
    {
        var reply = await AuthenticateAsync("/api/v1/auth/sign-up",
            new ApiSignUpRequest(displayName, email, password), cancellationToken);
        return await ApplyAuthReplyAsync(reply, cancellationToken);
    }

    private async Task<ApiSessionReply> AuthenticateAsync<T>(string path, T body, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ApiSessionReply>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The AWYKEN server returned an empty sign-in response.");
    }

    private async Task<string?> ApplyAuthReplyAsync(ApiSessionReply reply, CancellationToken cancellationToken)
    {
        if (reply.Error is not null || reply.AccessToken is null || reply.ProfileId is null)
            return reply.Error ?? "The server did not issue a user session.";

        await _tokens.SetAsync(reply.AccessToken, cancellationToken);
        _session.SignIn(reply.ProfileId.Value);
        if (reply.Dashboard is not null)
            _state.ReplaceFromApiSnapshot(reply.Dashboard);
        return null;
    }

    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _tokens.ClearAsync(cancellationToken);
        _session.SignOut();
        await InitializeAsync(cancellationToken);
    }

    private async Task CallVoidAsync(string method, object?[] arguments, CancellationToken cancellationToken)
    {
        _ = await SendRpcAsync(method, arguments, cancellationToken);
    }

    private async Task<T?> CallAsync<T>(string method, object?[] arguments, CancellationToken cancellationToken)
    {
        var reply = await SendRpcAsync(method, arguments, cancellationToken);
        if (reply.Result is null || reply.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;
        return reply.Result.Value.Deserialize<T>(JsonOptions);
    }

    private async Task<ApiRpcReply> SendRpcAsync(string method, object?[] arguments,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/v1/data/rpc",
            new ApiRpcRequest(method, arguments.Select(argument =>
                JsonSerializer.SerializeToElement(argument, JsonOptions)).ToArray()), cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var reply = await response.Content.ReadFromJsonAsync<ApiRpcReply>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The AWYKEN server returned an empty data response.");
        if (reply.Dashboard is not null)
            _state.ReplaceFromApiSnapshot(reply.Dashboard);
        return reply;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        var token = await _tokens.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Your AWYKEN session expired. Sign in again.");

        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message)
                ? $"The AWYKEN server returned {(int)response.StatusCode}."
                : $"The AWYKEN server returned {(int)response.StatusCode}: {message}",
            null, response.StatusCode);
    }

    private static CancellationToken GetCancellationToken(object?[] args) =>
        args.LastOrDefault() is CancellationToken token ? token : CancellationToken.None;
}
