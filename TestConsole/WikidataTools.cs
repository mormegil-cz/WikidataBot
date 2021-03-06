using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;

namespace TestConsole
{
    public static class WikidataTools
    {
        private const string Version = "0.1";

        private const string UserAgent = "MormegilsBot/" + Version + " (mormegil@centrum.cz) " + WikiClient.WikiClientUserAgent;

        private const string WikidataApiEndpoint = "https://www.wikidata.org/w/api.php";
        private const string QueryEndpoint = "https://query.wikidata.org/sparql";

        public static async Task<WikiSite> Init()
        {
            var wikiClient = new WikiClient {ClientUserAgent = UserAgent};
            var wikidataSite = new WikiSite(wikiClient, WikidataApiEndpoint);
            await wikidataSite.Initialization;

            return wikidataSite;
        }

        public static Task<string> GetSparqlResults(string sparql)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            // TODO: URI building and escaping
            var uriBuilder = new UriBuilder(QueryEndpoint) {Query = "format=json&query=" + Uri.EscapeDataString(sparql)};
            return client.GetStringAsync(uriBuilder.Uri);
        }

        public static IEnumerable<IList<string>> GetEntities(string queryResultJson, IDictionary<string, string> fields)
        {
            var queryResultObj = JObject.Parse(queryResultJson);
            foreach (var result in queryResultObj["results"]["bindings"])
            {
                var row = new List<string>(fields.Count);
                foreach (var (key, value) in fields)
                {
                    var item = result[key];
                    if (item == null)
                    {
                        row.Add(null);
                    }
                    else
                    {
                        var itemType = item["type"].Value<string>();
                        if (itemType != value) throw new FormatException($"{value} expected, {itemType} found");
                        row.Add(item["value"].Value<string>());
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

        public static string GetEntityIdFromUri(string entityUri) => new Uri(entityUri).AbsolutePath.Split('/').LastOrDefault();

        public static string GenerateRandomEditGroupId()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[10];
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static string MakeEditSummary(string summary, string editGroupId) => $"{summary} ([[:toollabs:editgroups/b/CB/{editGroupId}|details]])";
    }
}