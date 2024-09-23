using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;

namespace TestConsole
{
    public static class WikidataTools
    {
        public const string ProductName = "MormegilsBot";
        public const string Version = "0.1";

        public const string UserAgent = $"${ProductName}/${Version} (mormegil@centrum.cz) ${WikiClient.WikiClientUserAgent}";

        public const string WikidataApiEndpoint = "https://www.wikidata.org/w/api.php";
        public const string CommonsApiEndpoint = "https://commons.wikimedia.org/w/api.php";
        public const string WikidataQueryServiceEndpoint = "https://query.wikidata.org/sparql";
        public const string CommonsQueryServiceEndpoint = "https://commons-query.wikimedia.org/sparql";
        public const string CommonsQueryServiceOAuthCookieName = "wcqsOAuth";

        public static async Task<WikiSite> Init(string apiEndpoint)
        {
            var wikiClient = new WikiClient { ClientUserAgent = UserAgent };
            var wikidataSite = new WikiSite(wikiClient, apiEndpoint);
            await wikidataSite.Initialization;

            return wikidataSite;
        }

        public static Task<string> GetSparqlResults([StringSyntax("Sparql")] string sparql) => GetSparqlResults(WikidataQueryServiceEndpoint, null, null, sparql);

        public static Task<string> GetSparqlResults(string queryServiceEndpoint, string? cookieName, string? cookieValue, [StringSyntax("Sparql")] string sparql)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            client.DefaultRequestHeaders.Add("Accept", "application/sparql-results+json");
            if (cookieName != null)
            {
                client.DefaultRequestHeaders.Add("Cookie", cookieName + "=" + cookieValue);
            }
            // TODO: Alternate POSTed form for long queries
            // TODO: URI building and escaping
            // TODO: Collapse multiple tabs to shorten the string
            var uriBuilder = new UriBuilder(queryServiceEndpoint) { Query = "format=json&query=" + Uri.EscapeDataString(sparql) };
            try
            {
                return client.GetStringAsync(uriBuilder.Uri);
            }
            catch (HttpRequestException)
            {
                Console.Error.WriteLine("Error in SPARQL: " + sparql);
                throw;
            }
        }

        public static IEnumerable<IList<string?>> GetEntities(string queryResultJson, IDictionary<string, string> fields)
        {
            var queryResultObj = JObject.Parse(queryResultJson);
            foreach (var result in (queryResultObj["results"] ?? throw new FormatException())["bindings"] ?? throw new FormatException())
            {
                var row = new List<string?>(fields.Count);
                foreach (var (key, value) in fields)
                {
                    var item = result[key];
                    if (item == null)
                    {
                        row.Add(null);
                    }
                    else
                    {
                        var itemType = (item["type"] ?? throw new FormatException()).Value<string>();
                        if (itemType != value) throw new FormatException($"{value} expected, {itemType} found");
                        row.Add((item["value"] ?? throw new FormatException()).Value<string>());
                    }
                }

                yield return row;
            }
        }

        public static IEnumerable<IList<string>> GetResultsFromApi(JToken results, IList<string> treeSelector, IList<string> fields)
        {
            var pointer = results;
            foreach (var branch in treeSelector) pointer = pointer[branch];
            foreach (var result in pointer)
            {
                yield return fields.Select(field => result[field].Value<string>()).ToList();
            }
        }

        public static string GetEntityIdFromUri(string? entityUri) => entityUri == null ? "" : new Uri(entityUri).AbsolutePath.Split('/').Last();

        public static string GetStatementIdFromUri(string statementUri) => ReplaceFirst(new Uri(statementUri).AbsolutePath.Split('/').Last(), '-', '$');

        private static string ReplaceFirst(string str, char from, char to)
        {
            var index = str.IndexOf(from);
            return index < 0 ? str : str[..index] + to + str[(index + 1)..];
        }

        public static string GenerateRandomEditGroupId()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[10];
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static string MakeEditSummary(string summary, string editGroupId) => $"{summary} ([[:toollabs:editgroups/b/CB/{editGroupId}|details]])";

        public static async Task<string?> GetLabel(WikiSite wikidataSite, string qid, string language)
        {
            var entity = new Entity(wikidataSite, qid);
            await entity.RefreshAsync(EntityQueryOptions.FetchLabels, new[] { language });
            return entity.Labels[language];
        }

        public static string EncodeUrlParameters(FormattableString url) =>
            String.Format(
                CultureInfo.InvariantCulture,
                url.Format,
                url.GetArguments()
                    .Select(a => (object) Uri.EscapeDataString(a?.ToString() ?? ""))
                    .ToArray()
            );

        public static IEnumerable<TSource[]> Batch<TSource>(
            this IEnumerable<TSource> source, int size)
        {
            var bucket = new TSource[size];
            var count = 0;

            foreach (var item in source)
            {
                bucket[count++] = item;
                if (count == size)
                {
                    yield return bucket;
                    count = 0;
                }
            }

            if (count > 0) yield return bucket.Take(count).ToArray();
        }
    }
}