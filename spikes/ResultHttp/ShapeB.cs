using CodoMetis.TypeKit;

namespace ResultHttp;

/// <summary>
/// Shape B: the handler returns the result itself and a filter turns it into a response. Typed here, so
/// the measurement is of what ASP.NET does with such an endpoint; a package would get the type
/// arguments from the result itself (a visitor, as GeneratedValueObjectAttribute.Accept does).
/// </summary>
public static class ResultFilter
{
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> For<T, TError>()
        where T : notnull where TError : notnull =>
        async (context, next) => await next(context) is Result<T, TError> result ? result.ToHttpResult() : null;
}
