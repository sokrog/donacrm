using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class UserErrorsTests
{
    [Fact]
    public void Describe_KeepsRussianDomainMessages()
    {
        Assert.Equal("Продажа не найдена.", UserErrors.Describe(new InvalidOperationException("Продажа не найдена."), "fallback"));
        Assert.Equal("Нет доступа.", UserErrors.Describe(new GoogleApiException("Нет доступа.", System.Net.HttpStatusCode.Forbidden), "fallback"));
    }

    [Fact]
    public void Describe_HidesTechnicalMessages()
    {
        Assert.Equal("fallback", UserErrors.Describe(new InvalidOperationException("Sequence contains no elements"), "fallback"));
        Assert.Equal("fallback", UserErrors.Describe(new HttpRequestException("Failed to fetch"), "fallback"));
        Assert.Equal("fallback", UserErrors.Describe(new IOException("Диск недоступен"), "fallback"));
    }
}
