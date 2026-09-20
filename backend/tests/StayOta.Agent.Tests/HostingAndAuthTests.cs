using Microsoft.AspNetCore.Http;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Host.Security;
using Xunit;

namespace StayOta.Agent.Tests;

public class HostingGuardsTests
{
    [Fact]
    public void DemoMode_AllowsEmptyApiKey()
    {
        var ex = Record.Exception(() => HostingGuards.Validate(new HostingOptions
        {
            DemoEnabled = true,
            ApiKey = ""
        }));
        Assert.Null(ex);
    }

    [Fact]
    public void NonDemo_RequiresApiKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostingGuards.Validate(new HostingOptions
        {
            DemoEnabled = false,
            ApiKey = ""
        }));
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void NonDemo_WithApiKey_Ok()
    {
        var ex = Record.Exception(() => HostingGuards.Validate(new HostingOptions
        {
            DemoEnabled = false,
            ApiKey = "secret-key"
        }));
        Assert.Null(ex);
    }

    [Fact]
    public void ResetDatabase_RequiresDemo()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostingGuards.Validate(new HostingOptions
        {
            DemoEnabled = false,
            ApiKey = "secret",
            ResetDatabaseOnStartup = true
        }));
        Assert.Contains("ResetDatabaseOnStartup", ex.Message);
    }
}

public class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task EmptyApiKey_PassesThrough()
    {
        var ctx = new DefaultHttpContext();
        var called = false;
        var mw = new ApiKeyMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, Microsoft.Extensions.Options.Options.Create(new HostingOptions { ApiKey = "" }));

        await mw.InvokeAsync(ctx);
        Assert.True(called);
        Assert.NotEqual(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var mw = new ApiKeyMiddleware(_ => Task.CompletedTask,
            Microsoft.Extensions.Options.Options.Create(new HostingOptions { ApiKey = "secret", DemoEnabled = false }));

        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ValidHeader_Passes()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "secret";
        var called = false;
        var mw = new ApiKeyMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, Microsoft.Extensions.Options.Options.Create(new HostingOptions { ApiKey = "secret", DemoEnabled = false }));

        await mw.InvokeAsync(ctx);
        Assert.True(called);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/health/live";
        var called = false;
        var mw = new ApiKeyMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, Microsoft.Extensions.Options.Options.Create(new HostingOptions { ApiKey = "secret", DemoEnabled = false }));

        await mw.InvokeAsync(ctx);
        Assert.True(called);
    }
}
