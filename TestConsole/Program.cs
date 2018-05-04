using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;

namespace TestConsole
{
    class Program
    {
        private const string Version = "0.1";

        private const string UserAgent = "MormegilsBot/" + Version + " (mormegil@centrum.cz) " + WikiClient.WikiClientUserAgent;

        private const string WikidataApiEndpoint = "https://www.wikidata.org/w/api.php";
        private const string QueryEndpoint = "https://query.wikidata.org/sparql";

        static void Main(string[] args)
        {
            try
            {
                Run(args).Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static async Task Run(string[] args)
        {
            var wikiClient = new WikiClient {ClientUserAgent = UserAgent};
            var wikidataSite = new WikiSite(wikiClient, WikidataApiEndpoint);
            await wikidataSite.Initialization;
            foreach (var row in GetEntities(await GetSparqlResults(@"SELECT ?item ?ruian WHERE { ?item wdt:P4533 ?ruian . MINUS { ?item wdt:P281 [] } } LIMIT 20"), new Dictionary<string, string> {{"item", "uri"}, {"ruian", "literal"}}))
            {
                var entityId = GetEntityIdFromUri(row[0]);
                var qCimicka = new Entity(wikidataSite, entityId);
                await qCimicka.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});
                Console.WriteLine("{0}: {1} [#{2}]", entityId, qCimicka.Labels.FirstOrDefault(), row[1]);
            }
        }

        private static Task<string> GetSparqlResults(string sparql)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            // TODO: URI building and escaping
            var uriBuilder = new UriBuilder(QueryEndpoint) {Query = "format=json&query=" + Uri.EscapeUriString(sparql)};
            return client.GetStringAsync(uriBuilder.Uri);
        }

        private static IEnumerable<IList<string>> GetEntities(string queryResultJson, IDictionary<string, string> fields)
        {
            var queryResultObj = JObject.Parse(queryResultJson);
            foreach (var result in queryResultObj["results"]["bindings"])
            {
                var row = new List<string>(fields.Count);
                foreach (var field in fields)
                {
                    var item = result[field.Key];
                    var itemType = item["type"].Value<string>();
                    if (itemType != field.Value) throw new FormatException($"{field.Value} expected, {itemType} found");
                    row.Add(item["value"].Value<string>());
                }

                yield return row;
            }
        }

        private static string GetEntityIdFromUri(string entityUri)
        {
            return new Uri(entityUri).AbsolutePath.Split('/').LastOrDefault();
        }
    }
}