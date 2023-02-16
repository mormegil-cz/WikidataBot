using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestConsole.Integration.FotbalIdnes;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public class RemapFotbalIdnesReferences
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Migrating changed fotbal.idnes.cz IDs in references", EditGroupId);

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
  ?item ?prop ?propNode.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  ?propNode prov:wasDerivedFrom ?refNode. 
  ?refNode pr:P3663 ?ident.
  BIND (STRLEN(?ident) AS ?len)
  FILTER(?len = 7)
}
LIMIT 100
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
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, new[] { "cs" });

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var editedClaims = new HashSet<Claim>();
                foreach (var claim in entity.Claims)
                {
                    foreach (var reference in claim.References)
                    {
                        var idnesSnaks = reference.Snaks.Where(s => s.PropertyId == WikidataProperties.FotbalIdnesId && ((string)s.DataValue).Length == 7).ToHashSet();
                        if (idnesSnaks.Count == 0 || editedClaims.Contains(claim)) continue;

                        editedClaims.Add(claim);

                        foreach (var editedSnak in idnesSnaks)
                        {
                            var snakIdx = reference.Snaks.IndexOf(editedSnak);
                            var fixedSnak = new Snak(WikidataProperties.ReferenceUrl, FotbalIdnesApi.GetOldIdentUrl((string)editedSnak.DataValue), BuiltInDataTypes.Url);
                            reference.Snaks[snakIdx] = fixedSnak;
                        }
                    }
                }

                if (editedClaims.Count == 0)
                {
                    // ??
                    await Console.Error.WriteLineAsync($"WARNING! Nothing to edit at entity {entityId}");
                    problematicItems.Add(entityId);
                    continue;
                }

                var edits = editedClaims.Select(c => new EntityEditEntry(nameof(Entity.Claims), c)).ToList();
                await Console.Error.WriteLineAsync($"Editing {editedClaims.Count} claim(s) in {entityId} ({counter}/{count})");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}