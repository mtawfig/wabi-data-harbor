using WabiInsight.Core.Common;

namespace WabiInsight.Api.Authentication;

public sealed class HttpRequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpRequestContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? CorrelationId => _accessor.HttpContext?.TraceIdentifier;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
