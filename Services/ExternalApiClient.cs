namespace HRDashboard.Services;

public sealed class ExternalApiClient(HttpClient client, IConfiguration configuration)
{
    public bool IsConfigured => client.BaseAddress is not null;

    public async Task<ExternalApiStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return new ExternalApiStatus(false, null, "ExternalApi:BaseUrl이 설정되지 않았습니다.");

        var path = configuration["ExternalApi:HealthPath"] ?? "health";
        using var response = await client.GetAsync(path, cancellationToken);
        return new ExternalApiStatus(true, (int)response.StatusCode, response.ReasonPhrase ?? "");
    }
}

public sealed record ExternalApiStatus(bool Configured, int? StatusCode, string Message);
