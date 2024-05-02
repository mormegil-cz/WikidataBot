using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;

namespace TestConsole.Tasks;

using static WikidataTools;

public class FixupNkcrFieldOfWork
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixup of wrong field-of-work assignment via NKČR", EditGroupId);
    private static readonly string[] Languages = { };

    private const string SearchedValue = "Q7724161";
    private const string ReplacedByValue = "Q5398426";

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
	?item p:P101 ?fow.
    ?fow ps:P101 wd:" + SearchedValue + @";
         prov:wasDerivedFrom/pr:P248 wd:Q13550863.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
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

                if (!entity.Claims.ContainsKey(WikidataProperties.FieldOfWork))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain FOW?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var claims = entity.Claims[WikidataProperties.FieldOfWork];
                var problem = false;
                var edits = new List<EntityEditEntry>();
                foreach (var claim in claims)
                {
                    if (claim.Rank == "deprecated")
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has a deprecated FOW statement");
                        problem = true;
                        continue;
                    }

                    var fowValue = (string)claim.MainSnak.DataValue;
                    if (fowValue == SearchedValue)
                    {
                        if (!claim.References.Any(r => r.Snaks.Any(rs => rs.PropertyId == WikidataProperties.StatedIn && (string)rs.DataValue == "Q13550863")))
                        {
                            await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has a FOW statement not referenced with NKČR");
                            problem = true;
                            continue;
                        }
                        claim.MainSnak.DataValue = ReplacedByValue;
                        edits.Add(new(nameof(Entity.Claims), claim));
                    }
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