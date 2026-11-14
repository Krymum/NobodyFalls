namespace NobodyFalls.Api.Http;

/// <summary>
/// Cross-site request forgery guard (ADR 0007). Every state-changing API call must
/// carry <c>X-NF-Request: 1</c>. A cross-site page cannot add a custom header without
/// a CORS preflight, which the API never grants, so a forged form or link cannot act
/// with the player's cookie. Combined with <c>SameSite=Lax</c> cookies.
/// </summary>
internal sealed class RequireApiHeader : IEndpointFilter
{
    public const string Header = "X-NF-Request";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var safe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
        if (!safe && !request.Headers.ContainsKey(Header))
        {
            return ValueTask.FromResult<object?>(
                Problems.Code(StatusCodes.Status400BadRequest, "request.missing_header", $"State-changing requests need the {Header} header."));
        }

        return next(context);
    }
}
