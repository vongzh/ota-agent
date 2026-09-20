using StayOta.Agent.Abstractions.Security;
using Xunit;

namespace StayOta.Agent.Tests;

public class RequestScopeTests
{
    [Fact]
    public void Sanitize_KeepsAlnumDashUnderscoreDot()
    {
        var scope = new RequestScope("Acme_Demo-01.v2");
        Assert.Equal("acme_demo-01.v2", scope.ScopeId);
        Assert.Equal("scope:acme_demo-01.v2:", scope.KeyPrefix);
    }

    [Fact]
    public void Empty_HasNoPrefix()
    {
        Assert.Null(RequestScope.Empty.ScopeId);
        Assert.Equal("", RequestScope.Empty.KeyPrefix);
        Assert.Equal("", new RequestScope("@@@").KeyPrefix);
    }

    [Fact]
    public void Context_PushRestoresPrior()
    {
        Assert.Equal("", RequestScopeContext.CurrentScope.KeyPrefix);
        using (RequestScopeContext.Push(new RequestScope("tenant-a")))
        {
            Assert.Equal("scope:tenant-a:", RequestScopeContext.CurrentScope.KeyPrefix);
            using (RequestScopeContext.Push(new RequestScope("tenant-b")))
            {
                Assert.Equal("scope:tenant-b:", RequestScopeContext.CurrentScope.KeyPrefix);
            }
            Assert.Equal("scope:tenant-a:", RequestScopeContext.CurrentScope.KeyPrefix);
        }
        Assert.Equal("", RequestScopeContext.CurrentScope.KeyPrefix);
    }
}
