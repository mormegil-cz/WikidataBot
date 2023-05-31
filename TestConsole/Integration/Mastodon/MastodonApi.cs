using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KristofferStrube.ActivityStreams;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Integration.Mastodon;

public static class MastodonApi
{
    private static readonly Regex reAccountParseFormat = new(@"^([^@]+)@([^@]*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // no port, no IP-address-only, limit length (just some random limit, could be raised)
    private static readonly Regex reUrlValidator = new(@"^https?://[0-9a-z.-]*[a-z][a-z0-9.-]*/.{0,100}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HttpClient webFingerHttpClient = InitHttpClient("application/jrd+json", 5);
    private static readonly HttpClient activityHttpClient = InitHttpClient("application/activity+json", 0);

    private static HttpClient InitHttpClient(string acceptMediaType, int maxAutomaticRedirections)
    {
        var redirectHandler = new HttpClientHandler();
        redirectHandler.AllowAutoRedirect = maxAutomaticRedirections > 0;
        if (maxAutomaticRedirections > 0) redirectHandler.MaxAutomaticRedirections = maxAutomaticRedirections;
        var client = new HttpClient(redirectHandler);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptMediaType));
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(WikidataTools.ProductName, WikidataTools.Version));
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    public static async Task<string?> GetProfileUrl(string? mastodonAccountId, string entityId)
    {
        var match = reAccountParseFormat.Match(mastodonAccountId ?? "");
        if (!match.Success)
        {
            await Console.Error.WriteLineAsync($"Invalid/unexpected account ID format at {entityId}: '{mastodonAccountId}'");
            return null;
        }
        var username = match.Groups[1].Value;
        var server = match.Groups[2].Value;

        JsonResourceDescriptor? descriptor;
        try
        {
            webFingerHttpClient.DefaultRequestHeaders.Date = DateTimeOffset.UtcNow;
            var response = await webFingerHttpClient.GetAsync(WikidataTools.EncodeUrlParameters($"https://{server}/.well-known/webfinger?resource=acct:{mastodonAccountId}&rel=self"));
            if (response.StatusCode != HttpStatusCode.OK)
            {
                await Console.Error.WriteLineAsync($"Unexpected status code from {server}: {response.StatusCode} when fingering '{mastodonAccountId}' at {entityId}");
                return null;
            }

            descriptor = await response.Content.ReadFromJsonAsync<JsonResourceDescriptor>();
        }
        catch (HttpRequestException e)
        {
            await Console.Error.WriteLineAsync($"Error fingering '{mastodonAccountId}' for {entityId}: {e.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Timeout fingering '{mastodonAccountId}' for {entityId}");
            return null;
        }
        catch (JsonException)
        {
            await Console.Error.WriteLineAsync($"Invalid JSON returned when fingering '{mastodonAccountId}' for {entityId}");
            return null;
        }

        var profileUrl = descriptor?.Links?.FirstOrDefault(link => link is { Rel: "self", Type: "application/activity+json" })?.Href;
        if (profileUrl == null)
        {
            await Console.Error.WriteLineAsync($"No profile URL found when fingering '{mastodonAccountId}' for {entityId}");
            return null;
        }

        return profileUrl;
    }

    private static async Task<DateTime?> GetPublishedTimestamp(string profileUrl, string mastodonAccountId, string entityId)
    {
        if (!reUrlValidator.IsMatch(profileUrl))
        {
            await Console.Error.WriteLineAsync($"Suspicious profile URL of '{mastodonAccountId}' at {entityId}: '{profileUrl}");
            return null;
        }

        Actor? actor;
        try
        {
            activityHttpClient.DefaultRequestHeaders.Date = DateTimeOffset.UtcNow;
            var response = await activityHttpClient.GetAsync(profileUrl);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                await Console.Error.WriteLineAsync($"Unexpected status code from '{profileUrl}': {response.StatusCode} when reading '{mastodonAccountId}' for {entityId}");
                return null;
            }

            actor = await response.Content.ReadFromJsonAsync<Actor>();
        }
        catch (HttpRequestException e)
        {
            await Console.Error.WriteLineAsync($"Error reading profile of '{mastodonAccountId}' at '{profileUrl}' for {entityId}: {e.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Timeout reading profile of '{mastodonAccountId}' at '{profileUrl}' for {entityId}");
            return null;
        }
        catch (JsonException)
        {
            await Console.Error.WriteLineAsync($"Invalid JSON returned when reading profile of '{mastodonAccountId}' at '{profileUrl}' for {entityId}");
            return null;
        }

        var published = actor?.Published;
        if (published == null)
        {
            await Console.Error.WriteLineAsync($"No account published date for '{mastodonAccountId}' of {entityId} at '{profileUrl}");
            return null;
        }

        return published;
    }

    public static async Task<WbTime?> GetAccountRegistrationDate(string mastodonAccountId, string profileUrl, string entityId)
    {
        var publishedTimestampOpt = await GetPublishedTimestamp(profileUrl, mastodonAccountId, entityId);
        if (publishedTimestampOpt == null) return null;
        var publishedTimestamp = publishedTimestampOpt.Value;

        return new WbTime(publishedTimestamp.Year, publishedTimestamp.Month, publishedTimestamp.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);
    }
}