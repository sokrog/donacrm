using System.Net;
using Dona.Crm.Web.Services;
using Xunit;

namespace Dona.Crm.Core.Tests;

public sealed class GoogleErrorMessagesTests
{
    private const string Prefix = "Не удалось скачать";

    [Fact]
    public void Unauthorized_asks_to_sign_in_again()
    {
        var text = GoogleErrorMessages.Describe(HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"Invalid Credentials"}}""", Prefix);

        Assert.Equal("Сессия Google истекла. Войдите в Google ещё раз.", text);
    }

    [Fact]
    public void Insufficient_scopes_returns_scope_message()
    {
        var text = GoogleErrorMessages.Describe(HttpStatusCode.Forbidden, """{"error":{"message":"Request had insufficient authentication scopes."}}""", Prefix);

        Assert.StartsWith(Prefix, text);
        Assert.Contains("разрешение на Google Drive", text);
    }

    [Theory]
    [InlineData("rateLimitExceeded")]
    [InlineData("userRateLimitExceeded")]
    [InlineData("quotaExceeded")]
    public void Forbidden_rate_limit_reasons_map_to_throttling_message(string reason)
    {
        var body = $$$"""{"error":{"code":403,"message":"Limit","errors":[{"reason":"{{{reason}}}"}]}}""";

        Assert.Equal("Google временно ограничил запросы. Повторите через минуту.", GoogleErrorMessages.Describe(HttpStatusCode.Forbidden, body, Prefix));
    }

    [Fact]
    public void Too_many_requests_maps_to_throttling_message()
    {
        Assert.Equal("Google временно ограничил запросы. Повторите через минуту.", GoogleErrorMessages.Describe(HttpStatusCode.TooManyRequests, "", Prefix));
    }

    [Fact]
    public void Not_found_maps_to_missing_file_message()
    {
        Assert.Equal("Файл в Google Drive не найден.", GoogleErrorMessages.Describe(HttpStatusCode.NotFound, """{"error":{"message":"File not found: abc."}}""", Prefix));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Server_errors_map_to_unavailable_message(HttpStatusCode status)
    {
        Assert.Equal("Сервис Google временно недоступен. Повторите позже.", GoogleErrorMessages.Describe(status, "<html>oops</html>", Prefix));
    }

    [Fact]
    public void Invalid_grant_maps_to_revoked_access_message()
    {
        var text = GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""", "Google OAuth вернул ошибку");

        Assert.Equal("Доступ к Google отозван или истёк. Войдите заново.", text);
    }

    [Fact]
    public void Other_errors_use_extracted_message_never_raw_json()
    {
        var text = GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Bad parameter","errors":[{"reason":"invalidParameter"}]}}""", Prefix);

        Assert.Equal($"{Prefix}: Bad parameter", text);
        Assert.DoesNotContain("{", text);
    }

    [Fact]
    public void Oauth_error_description_is_preferred_over_error_code()
    {
        var text = GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, """{"error":"invalid_request","error_description":"Missing required parameter"}""", Prefix);

        Assert.Equal($"{Prefix}: Missing required parameter", text);
    }

    [Fact]
    public void Long_detail_is_truncated()
    {
        var body = $$$"""{"error":{"message":"{{{new string('x', 500)}}}"}}""";

        var text = GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, body, Prefix);

        Assert.True(text.Length <= Prefix.Length + 2 + 201);
    }

    [Fact]
    public void Non_json_body_is_truncated_and_empty_body_falls_back_to_status()
    {
        Assert.StartsWith(Prefix, GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, new string('y', 1000), Prefix));
        Assert.Equal($"{Prefix} (HTTP 400).", GoogleErrorMessages.Describe(HttpStatusCode.BadRequest, null, Prefix));
    }

    [Fact]
    public async Task Exception_exposes_status_code()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };

        var exception = await GoogleErrorMessages.CreateExceptionAsync(response, Prefix, default);

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
