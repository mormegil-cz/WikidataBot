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
    public static class DrobnePamatkyDeprecated
    {
        public static async Task Run(WikiSite wikidataSite)
        {
            await Console.Error.WriteLineAsync("Retrieving data from WQS");
            var entities = GetEntities(await GetSparqlResults(@"
select ?item ?id where {
    ?item p:P6736 [ps:P6736 ?id ; wikibase:rank ?rank ; pq:P2241 wd:Q21441764 ] filter(?rank != wikibase:DeprecatedRank) .
}
"), new Dictionary<string, string> {{"item", "uri"}, {"id", "literal"}}).ToList();
            var counter = 0;
            var count = entities.Count;
            await Console.Error.WriteLineAsync($"Retrieved {count} entities, processing...");
            foreach (var row in entities)
            {
                var entityId = GetEntityIdFromUri(row[0]);
                await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});

                var idClaims = entity.Claims["P6736"].Where(claim => claim.Qualifiers.Any(qualifier => qualifier.PropertyId == "P2241") && claim.Rank == "normal").ToList();
                if (idClaims.Count == 0)
                {
                    await Console.Error.WriteLineAsync($"No more not-yet-deprecated claims in {entityId}");
                    continue;
                }
                foreach (var idClaim in idClaims)
                {
                    idClaim.Rank = "deprecated";
                }

                var edits = idClaims.Select(idClaim => new EntityEditEntry(nameof(Entity.Claims), idClaim)).ToList();
                await Console.Error.WriteLineAsync($"Editing {entityId}");
                await entity.EditAsync(edits, "Deprecating cancelled withdrawn Drobné památky IDs", EntityEditOptions.Bot);
            }

            await Console.Error.WriteLineAsync("Done!");
        }
    }
}