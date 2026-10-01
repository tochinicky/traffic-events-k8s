using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TrafficEvents.IngestApi;

/// <summary>Simple shared-key auth for sensors. Constant-time compare to avoid timing leaks.</summary>
public sealed class ApiKeyFilter(IOptions<IngestOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var expected = options.Value.ApiKey;
        var provided = context.HttpContext.Request.Headers[IngestOptions.ApiKeyHeader].ToString();

        if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, provided))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        return await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
