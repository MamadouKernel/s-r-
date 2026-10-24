using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Soro.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace Soro.Integration.Tests;

public class ApiExceptionHandlerTests
{
    private static async Task<(int Status, string Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddOptions().AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance).AddProblemDetails()
            .BuildServiceProvider();
        var handled = await new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);
        Assert.True(handled);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Fact]
    public async Task An_unreadable_request_body_is_a_400_with_a_stable_code_and_no_detail_of_the_failure()
    {
        var (status, body) = await HandleAsync(new BadHttpRequestException("Failed to read parameter at C:\\secret\\path", 400));

        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Contains("bad_request", body);
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public async Task Any_other_failure_stays_a_500_without_leaking_its_message()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("password=hunter2"));

        Assert.Equal((int)HttpStatusCode.InternalServerError, status);
        Assert.DoesNotContain("hunter2", body);
    }
}
