using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TestConsole.Integration.FotbalIdnes;

public class FotbalIdnesApi
{
    private static readonly Regex reCurrentUriFormat = new Regex(@"^https://www\.idnes\.cz/fotbal/databanka/[^.]*\.Uplr([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HttpClient httpClient = InitHttpClient();

    private static HttpClient InitHttpClient()
    {
        var redirectHandler = new HttpClientHandler();
        redirectHandler.AllowAutoRedirect = false;
        var client = new HttpClient(redirectHandler);
        client.BaseAddress = new Uri("https://fotbal.idnes.cz/databanka.aspx");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(WikidataTools.ProductName, WikidataTools.Version));
        return client;
    }

    public static async Task<string?> ConvertOldIdent(string currentIdent)
    {
        var response = await httpClient.GetAsync("?t=hrac&id=" + currentIdent);
        if (response.StatusCode != HttpStatusCode.Found)
        {
            await Console.Error.WriteLineAsync($"Unexpected status code from iDnes: {response.StatusCode} when retrieving {currentIdent}");
            return null;
        }
        var targetUri = response.Headers.Location?.AbsoluteUri;
        if (targetUri == null)
        {
            await Console.Error.WriteLineAsync($"No redirect location when retrieving {currentIdent}");
            return null;
        }
        var match = reCurrentUriFormat.Match(targetUri);
        if (!match.Success)
        {
            await Console.Error.WriteLineAsync($"Unexpected destination URI when retrieving {currentIdent}: '{targetUri}'");
            return null;
        }
        return match.Groups[1].Value;
    }

    public static string GetOldIdentUrl(string ident) => "https://fotbal.idnes.cz/databanka.aspx?t=hrac&id=" + ident;
}