using System.Reflection;
using System.Text.Json;
using AWYKEN.Shared.Data;
using AWYKEN.Shared.Models;
using AWYKEN.Shared.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AWYKEN.Web.Services;

public static class AwykenApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapAwykenApi(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/v1", out var remainder)
                || remainder.StartsWithSegments("/auth/sign-in")
                || remainder.StartsWithSegments("/auth/sign-up"))
            {
                await next();
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.Length > 0)
            {
                if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var token = authorization[7..].Trim();
                var tokenService = context.RequestServices.GetRequiredService<ApiAccessTokenService>();
                var profileId = await tokenService.ValidateAsync(token, context.RequestAborted);
                if (profileId is null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.RequestServices.GetRequiredService<LocalAccountSession>().SignIn(profileId.Value);
            }

            await next();
        });

        app.MapPost("/api/v1/auth/sign-in", async (
            ApiSignInRequest request,
            IAwykenDataService data,
            LocalAccountSession account,
            ApiAccessTokenService tokens,
            DashboardState state,
            CancellationToken ct) =>
        {
            var error = await data.SignInAsync(request.Email, request.Password, ct);
            if (error is not null || account.ProfileId is not int profileId)
                return Results.Ok(new ApiSessionReply(error ?? "Sign-in failed.", null, null, false, null));

            var isAdmin = await data.IsAdminAsync(ct);
            return Results.Ok(new ApiSessionReply(null, tokens.Issue(profileId), profileId,
                isAdmin, ApiDashboardSnapshot.From(state)));
        }).RequireRateLimiting("auth");

        app.MapPost("/api/v1/auth/sign-up", async (
            ApiSignUpRequest request,
            IAwykenDataService data,
            LocalAccountSession account,
            ApiAccessTokenService tokens,
            DashboardState state,
            CancellationToken ct) =>
        {
            var error = await data.SignUpAsync(request.DisplayName, request.Email, request.Password, ct);
            if (error is not null || account.ProfileId is not int profileId)
                return Results.Ok(new ApiSessionReply(error ?? "Sign-up failed.", null, null, false, null));

            var isAdmin = await data.IsAdminAsync(ct);
            return Results.Ok(new ApiSessionReply(null, tokens.Issue(profileId), profileId,
                isAdmin, ApiDashboardSnapshot.From(state)));
        }).RequireRateLimiting("auth");

        app.MapGet("/api/v1/auth/session", async (
            IAwykenDataService data, LocalAccountSession account, DashboardState state, CancellationToken ct) =>
        {
            await data.InitializeAsync(ct);
            await data.RefreshAsync(DateOnly.FromDateTime(DateTime.Today), ct);
            return Results.Ok(new ApiSessionSnapshot(account.ProfileId, data.IsAuthenticated,
                await data.IsAdminAsync(ct), ApiDashboardSnapshot.From(state)));
        });

        app.MapPost("/api/v1/data/rpc", InvokeDataOperationAsync);

        app.MapGet("/api/v1/calendar/{schoolYearStart:int}", async (
            int schoolYearStart,
            IUmakCalendarService calendar,
            IAwykenDataService data,
            CancellationToken ct) =>
        {
            await data.InitializeAsync(ct);
            var events = await calendar.GetEventsAsync(schoolYearStart, ct);
            return Results.Ok(new ApiCalendarReply(events, calendar.Status));
        });

        app.MapGet("/api/v1/calendar/years", async (
            IUmakCalendarService calendar, IAwykenDataService data, CancellationToken ct) =>
        {
            await data.InitializeAsync(ct);
            return Results.Ok(await calendar.GetSchoolYearsAsync(ct));
        });
    }

    private static async Task<IResult> InvokeDataOperationAsync(
        ApiRpcRequest request,
        IAwykenDataService data,
        DashboardState state,
        CancellationToken cancellationToken)
    {
        // Sign-in and sign-up use dedicated endpoints so credentials are not sent through
        // the generic operation route or echoed in an error response.
        if (request.Method is nameof(IAwykenDataService.SignInAsync)
            or nameof(IAwykenDataService.SignUpAsync)
            or nameof(IAwykenDataService.SignOutAsync))
            return Results.BadRequest(new { error = "Use the authentication endpoints for account access." });

        var method = typeof(IAwykenDataService).GetMethod(request.Method);
        if (method is null || method.ReturnType == typeof(void))
            return Results.BadRequest(new { error = "Unknown data operation." });

        var parameters = method.GetParameters();
        var businessParameters = parameters.Where(parameter => parameter.ParameterType != typeof(CancellationToken)).ToArray();
        if (request.Arguments.Length != businessParameters.Length)
            return Results.BadRequest(new { error = "Invalid data operation arguments." });

        var invokeArguments = new object?[parameters.Length];
        var inputIndex = 0;
        for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
        {
            var parameter = parameters[parameterIndex];
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                invokeArguments[parameterIndex] = cancellationToken;
                continue;
            }

            try
            {
                invokeArguments[parameterIndex] = request.Arguments[inputIndex++].Deserialize(
                    parameter.ParameterType, JsonOptions);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid data operation arguments." });
            }
        }

        try
        {
            if (method.Invoke(data, invokeArguments) is not Task task)
                return Results.BadRequest(new { error = "Unsupported data operation." });

            await task;
            var result = task.GetType().IsGenericType
                ? task.GetType().GetProperty("Result")?.GetValue(task)
                : null;
            var dashboard = request.Method is nameof(IAwykenDataService.RefreshAsync)
                or nameof(IAwykenDataService.RefreshWeekAsync)
                or nameof(IAwykenDataService.RefreshMonthAsync)
                    ? ApiDashboardSnapshot.From(state)
                    : null;

            return Results.Ok(new ApiRpcReply(
                result is null ? null : JsonSerializer.SerializeToElement(result, JsonOptions), dashboard));
        }
        catch (TargetInvocationException exception) when (exception.GetBaseException() is UnauthorizedAccessException)
        {
            return Results.Json(new { error = "This account is not permitted to perform that action." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new { error = "This account is not permitted to perform that action." },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
