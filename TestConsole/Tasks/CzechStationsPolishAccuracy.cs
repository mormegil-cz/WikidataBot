using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class CzechStationsPolishAccuracy
    {
        public static async Task Run(WikiSite wikidataSite)
        {
            await Console.Error.WriteLineAsync("Retrieving data from WQS");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?lat ?lon WHERE {
  ?item wdt:P17 wd:Q213.
  ?item wdt:P722 [].
  ?item p:P625 ?coordsStmt.
  ?coordsStmt psv:P625 ?coords.
  ?coordsStmt prov:wasDerivedFrom/pr:P143 wd:Q1551807.
  ?coords wikibase:geoPrecision ?prec.
  FILTER (?prec > 0.14)
  ?coords wikibase:geoLongitude ?lon.
  ?coords wikibase:geoLatitude ?lat.
}
"), new Dictionary<string, string> {{"item", "uri"}, {"lat", "literal"}, {"lon", "literal"}}).ToList();
            var counter = 0;
            var count = entities.Count();
            await Console.Error.WriteLineAsync($"Retrieved {count} entities, processing...");
            foreach (var row in entities)
            {
                var entityId = GetEntityIdFromUri(row[0]);
                await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});

                var coordinateClaim = entity.Claims["P625"].First();
                var coordinateMainSnak = coordinateClaim.MainSnak;
                var originalCoordinate = ((WbGlobeCoordinate) coordinateMainSnak.DataValue);
                coordinateMainSnak.DataValue = new WbGlobeCoordinate(originalCoordinate.Latitude, originalCoordinate.Longitude, 0.0001, originalCoordinate.Globe);

                // Make a some changes
                var edits = new List<EntityEditEntry>
                {
                    // Update a claim
                    new EntityEditEntry(nameof(Entity.Claims), coordinateClaim),
                };
                await Console.Error.WriteLineAsync($"Editing {entityId}");
                await entity.EditAsync(edits, "Fixing precision on CZ train station coordinates imported from plwiki", EntityEditOptions.Bot);
            }

            await Console.Error.WriteLineAsync("Done!");
        }
    }
}