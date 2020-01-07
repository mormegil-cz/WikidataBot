using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class ListSparqlQuery
    {
        public static async Task Run(WikiSite wikidataSite)
        {
            foreach (var row in GetEntities(await GetSparqlResults(@"
SELECT ?item ?lat ?lon WHERE {
  ?item wdt:P17 wd:Q213.
  ?item wdt:P722 [].
  ?item p:P625 ?coordsStmt.
  ?coordsStmt psv:P625 ?coords.
  ?coordsStmt prov:wasDerivedFrom/pr:P143 wd:Q1551807.
  ?coords wikibase:geoPrecision ?prec.
  FILTER (?prec > 0.13)
  ?coords wikibase:geoLongitude ?lon.
  ?coords wikibase:geoLatitude ?lat.
}
"), new Dictionary<string, string> {{"item", "uri"}, {"lat", "literal"}, {"lon", "literal"}}))
            {
                var entityId = GetEntityIdFromUri(row[0]);
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});
                Console.WriteLine("{0}: ({1}) {2}", entityId, entity.Labels.FirstOrDefault(), String.Join("; ", row.Skip(1)));
            }
        }
    }
}