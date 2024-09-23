using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
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
    private static readonly HashSet<String> serverBlacklist = new()
    {
        // Request signature required
        "gensokyo.social", "grapheneos.social", "icosahedron.website", "masto.donte.com.br", "mastodon.art", "merveilles.town", "pleroma.envs.net", "projectmushroom.social", "scholar.social", "tenforward.social", "vt.social", "crimew.gay", "kind.social", "octodon.social", "scicomm.xyz",
        "mastodon.ie", "akademienl.social", "mastodonapp.uk", "infosec.exchange", "flipping.rocks", "botsin.space", "indieweb.social", "climatejustice.social",

        // Forbidden
        "counter.social", "quey.org",

        // No account published date
        "qoto.org", "pawoo.net", "social.weho.st", "people.kernel.org", "pixelfed.social", "write.as", "gnusocial.net", "podlibre.social", "open.audio", "social.saghul.net", "social.kernel.org", "neenster.org",

        // DNS failure
        "mastodon.technology", "mastodon.etalab.gouv.fr", "quitter.im", "mastoforce.social", "socialscience.re", "m.sclo.nl", "mstdn.soc", "mastodon.soc", "social.bitcast.info", "mastodon.huma-num.fr", "mstdn.sci", "social.numerama.com", "joura.host",
        "nzserver.co", "mastodon.lol", "mastodon.se",

        // SSL failure
        "mstdn.appliedecon.social", "camerondcampbell.masto.host", "mastodonten.de", "peertube.video", "soc.ialis.me", "mastodon.mikegerwitz.com", "pirati.cc",

        // Timeout
        "eupublic.social", "content.town", "masthead.social", "campaign.openworlds.info",

        // Connection refused
        "oyd.social",

        // Invalid JSON received
        "social.csswg.org", "mastodon.at", "mail.huji.ac.il", "koyu.space", "alexsirac.com", "retrotroet.com",
    };

    private static readonly Regex reAccountParseFormat = new(@"^([^@]+)@([^@/%]*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    [Pure]
    public static bool ShouldProcess(string? mastodonAccountId, string entityId)
    {
        var match = reAccountParseFormat.Match(mastodonAccountId ?? "");
        if (!match.Success)
        {
            Console.Error.WriteLine($"Invalid/unexpected account ID format at {entityId}: '{mastodonAccountId}'");
            return false;
        }

        if (serverBlacklist.Contains(match.Groups[2].Value))
        {
            Console.Error.WriteLine($"Skipping blacklisted server in '{mastodonAccountId}' at {entityId}");
            return false;
        }

        return true;
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

        if (serverBlacklist.Contains(server))
        {
            await Console.Error.WriteLineAsync($"Skipping blacklisted server in '{mastodonAccountId}' at {entityId}");
            return null;
        }

        JsonResourceDescriptor? descriptor;
        try
        {
            webFingerHttpClient.DefaultRequestHeaders.Date = DateTimeOffset.UtcNow;
            var response = await webFingerHttpClient.GetAsync($"https://{server}/.well-known/webfinger?resource=acct:{Uri.EscapeDataString(mastodonAccountId)}&rel=self");
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
        catch (UriFormatException e)
        {
            await Console.Error.WriteLineAsync($"Invalid server in '{mastodonAccountId}' for {entityId}: {e.Message}");
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
            await Console.Error.WriteLineAsync($"Suspicious profile URL of '{mastodonAccountId}' at {entityId}: '{profileUrl}'");
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