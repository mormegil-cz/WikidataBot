using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Tasks;

using static WikidataTools;

public static class SwitchTopicClassification
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Switching work topic classification to the correct property", EditGroupId);
    private static readonly string[] Languages = { };

    public static async Task Run(WikiSite wikidataSite)
    {
        var batch = 0;
        var problematicItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT DISTINCT ?item WHERE {
  ?item wdt:P1190 [].
  VALUES ?cls {
    wd:Q4167410
    wd:Q3331189
    wd:Q7725634
    wd:Q571
    wd:Q41298
    wd:Q5633421
    wd:Q737498
    wd:Q15695196
    wd:Q1700470
    wd:Q1002697
    wd:Q1238720
    wd:Q773668
  }
  ?item wdt:P31 ?cls.
}
LIMIT 500
"), new Dictionary<string, string> { { "item", "uri" } }).ToList();
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
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicItems.Add(entityId);
                    continue;
                }

                if (!entity.Claims.ContainsKey(WikidataProperties.UdcOfConcept))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain UDC?!");
                    problematicItems.Add(entityId);
                    continue;
                }
                if (entity.Claims.ContainsKey(WikidataProperties.UdcOfTopic))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} already contain UDC of topic?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var claims = entity.Claims[WikidataProperties.UdcOfConcept];
                var problem = false;
                var edits = new List<EntityEditEntry>();
                foreach (var claim in claims)
                {
                    if (claim.Rank == "deprecated")
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has a deprecated UDC statement");
                        continue;
                    }

                    var udcValue = (string)claim.MainSnak.DataValue;

                    var newClaim = new Claim(new Snak(WikidataProperties.UdcOfTopic, udcValue, BuiltInDataTypes.ExternalId));
                    foreach(var oldQual in claim.Qualifiers) newClaim.Qualifiers.Add(oldQual);
                    foreach(var oldRef in claim.References) newClaim.References.Add(oldRef);

                    edits.Add(new(nameof(Entity.Claims), newClaim));
                    edits.Add(new(nameof(Entity.Claims), claim, EntityEditEntryState.Removed));
                }

                if (edits.Count == 0)
                {
                    if (!problem) await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has probably been fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }

                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}