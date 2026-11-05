using System.Net.Http.Json;
using System.Text.Json;

namespace WabiInsight.IntegrationTests.Api;

public static class ApiClientExtensions
{
    public const string CsrfHeader = "X-XSRF-TOKEN";

    /// <summary>Fetches an antiforgery token (cookie + header value) and applies it to the client.</summary>
    public static async Task RefreshCsrfAsync(this HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/auth/csrf", UriKind.Relative));
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, token.GetProperty("token").GetString());
    }

    /// <summary>Logs in and refreshes the antiforgery token for the new identity.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password)
    {
        await client.RefreshCsrfAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { email, password });
        if (response.IsSuccessStatusCode)
        {
            await client.RefreshCsrfAsync();
        }

        return response;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string path, T body)
        => client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string path)
        => client.GetAsync(new Uri(path, UriKind.Relative));

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
