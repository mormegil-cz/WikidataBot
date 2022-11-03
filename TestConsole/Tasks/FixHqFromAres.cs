using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestConsole.Integration.Ares;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public class FixHqFromAres
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing broken HQ location from ARES", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        var labelCache = WikidataCache.CreateLabelCache(wikidataSite, "cs");
        var streetUriCache = WikidataCache.CreateSparqlCache(@"
SELECT ?item WHERE {
    ?item wdt:P4533 '$PARAM$'.
}
", "item", "uri");

        var batch = 0;
        var problematicItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?ico WHERE {
  ?item wdt:P4156 ?ico;
    wdt:P17 wd:Q213;
    (p:P159/pq:P281) ?zip.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  FILTER(REGEX(?zip, '^[^0-9]'))
}
LIMIT 100
"), new Dictionary<string, string> { { "item", "uri" }, { "ico", "literal" } }).ToList();
            if (entities.Count == 0) break;

            var counter = 0;
            var count = entities.Count;
            await Console.Error.WriteLineAsync($" processing {count} entities...");
            foreach (var row in entities)
            {
                ++counter;
                var entityId = GetEntityIdFromUri(row[0]);
                // await Console.Error.WriteLineAsync($"Reading {entityId} ({counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, new[] { "cs" });

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicItems.Add(entityId);
                    continue;
                }

                if (!entity.Claims.ContainsKey("P159"))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain HQ?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var hqClaims = entity.Claims["P159"];
                if (hqClaims.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {hqClaims.Count} HQ claims");
                    problematicItems.Add(entityId);
                    continue;
                }

                var ico = row[1] ?? throw new FormatException("Missing ico in WQS response!");
                var aresData = await AresRestApi.GetAresData(ico);
                if (aresData == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to find IČO '{ico}' for {entityId} in ARES");
                    problematicItems.Add(entityId);
                    continue;
                }

                var hqLocationAddress = aresData.AA;
                if (hqLocationAddress.KS != "203")
                {
                    await Console.Error.WriteLineAsync($"WARNING! Ignoring non-CZ HQ location for {entityId}: {hqLocationAddress.KS}");
                    problematicItems.Add(entityId);
                    continue;
                }

                var currentHqClaim = hqClaims.Single();
                var currentHqMunicipalityQid = (string)currentHqClaim.MainSnak.DataValue;

                var currentHqMunicipalityLabel = await labelCache.Get(currentHqMunicipalityQid);
                if (hqLocationAddress.N != currentHqMunicipalityLabel)
                {
                    await Console.Error.WriteLineAsync($"WARNING! HQ municipality mismatch for {entityId}: {currentHqMunicipalityLabel} vs {hqLocationAddress.N}");
                    problematicItems.Add(entityId);
                    continue;
                }

                
                
                
                // var newHqClaim = new Claim(new Snak("P159", currentHqMunicipalityQid, currentHqClaim.MainSnak.DataType));
                //
                // newHqClaim.Qualifiers.Add();
                //
                //
                // var edits = new List<EntityEditEntry>
                // {
                //     new(nameof(Entity.Claims), currentHqClaim, EntityEditEntryState.Removed),
                //     new(nameof(Entity.Claims), newHqClaim)
                // };
                // await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                // await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}