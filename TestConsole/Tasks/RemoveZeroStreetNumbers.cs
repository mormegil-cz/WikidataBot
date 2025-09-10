using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class RemoveZeroStreetNumbers
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Removing missing street numbers misrepresented as zero after Rejskol import", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        var batch = 0;
        var problematicStatements = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?stmt WHERE {
  ?stmt pq:P670 '0';
        ps:P159/wdt:P17 wd:Q213;
        prov:wasDerivedFrom/pr:P248 wd:Q61376331.
}
LIMIT 200"), new Dictionary<string, string> { { "stmt", "uri" } }).ToList();
            if (entities.Count == 0) break;

            var counter = 0;
            var count = entities.Count;
            await Console.Error.WriteLineAsync($" processing {count} entities...");
            foreach (var row in entities)
            {
                ++counter;
                var statementUri = row[0];
                var entityId = GetEntityIdFromStatementUri(statementUri);
                var statementId = GetStatementIdFromUri(statementUri);
                // await Console.Error.WriteLineAsync($"Reading {entityId} ({counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, []);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicStatements.Add(statementId);
                    continue;
                }

                var statement = entity.Claims.SingleOrDefault(claim => claim.Id == statementId);
                if (statement == null)
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain statement {statementUri}?!?");
                    problematicStatements.Add(statementId);
                    continue;
                }

                var zeroStreetNumberQualifiers = statement.Qualifiers.Where(q => q is { PropertyId: WikidataProperties.StreetNumber, DataValue: "0" }).ToList();
                if (zeroStreetNumberQualifiers.Count == 0)
                {
                    await Console.Error.WriteLineAsync($"WARNING! No zero street number qualifier in entity {entityId} (statement {statementUri})");
                    problematicStatements.Add(statementId);
                    continue;
                }

                var edits = new List<EntityEditEntry>();
                foreach (var qualifier in zeroStreetNumberQualifiers)
                {
                    statement.Qualifiers.Remove(qualifier);
                }
                edits.Add(new EntityEditEntry(nameof(Entity.Claims), statement));
                ;

                await Console.Error.WriteAsync($"Editing {entityId} ({counter}/{count})");
                if (zeroStreetNumberQualifiers.Count != 1) await Console.Error.WriteAsync($" ({zeroStreetNumberQualifiers.Count} qualifiers removed)");
                await Console.Error.WriteLineAsync();
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }

            if (entities.Count < 200) break;
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}