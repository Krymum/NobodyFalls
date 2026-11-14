using Microsoft.AspNetCore.Mvc;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Api.Http;

/// <summary>
/// Turns rule violations into <c>application/problem+json</c>. The stable
/// <c>code</c> is what the front end translates (FR/EN); the title is for developers.
/// </summary>
internal static class Problems
{
    public static IResult From(DomainError error)
    {
        var status = StatusOf(error.Code);
        var problem = new ProblemDetails
        {
            Type = $"https://nobodyfalls.com/errors/{error.Code}",
            Title = error.Message,
            Status = status,
        };
        problem.Extensions["code"] = error.Code;

        if (error == HandErrors.NightResolving)
        {
            return new RetryAfterResult(Results.Problem(problem), seconds: 2);
        }

        return Results.Problem(problem);
    }

    public static IResult Of(Result result) => result.IsSuccess ? Results.NoContent() : From(result.Error!);

    public static IResult Code(int status, string code, string title)
    {
        var problem = new ProblemDetails { Type = $"https://nobodyfalls.com/errors/{code}", Title = title, Status = status };
        problem.Extensions["code"] = code;
        return Results.Problem(problem);
    }

    private static int StatusOf(string code) => code switch
    {
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        "hand.night_resolving" => StatusCodes.Status503ServiceUnavailable,
        "account.verification_failed" => StatusCodes.Status403Forbidden,
        _ when code.Contains("duplicate", StringComparison.Ordinal)
               || code.Contains("already", StringComparison.Ordinal)
               || code.EndsWith(".taken", StringComparison.Ordinal)
               || code.EndsWith("limit_reached", StringComparison.Ordinal)
               || code.EndsWith(".none_left", StringComparison.Ordinal)
               || code.EndsWith(".season_closed", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
