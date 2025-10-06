using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixSchoolFounderRoleQualifier
{
    private static readonly HashSet<string> NoLanguages = [];

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing qualifier for school founder after Rejskol import", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        var batch = 0;
        var problematicItems = new HashSet<string>();
        var processedItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(
                """
                SELECT DISTINCT ?item WHERE {
                    ?item p:P749/pq:P2868 wd:Q4479442;
                        wdt:P6370 [].
                    MINUS {
                        VALUES ?item {
                """ + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
                    }
                }
                LIMIT 400"
            ), new Dictionary<string, string> { { "item", "uri" } }).ToList();
            if (entities.Count == 0) break;

            var counter = 0;
            var count = entities.Count;
            await Console.Error.WriteLineAsync($" processing {count} entities...");
            foreach (var row in entities)
            {
                ++counter;
                var entityId = GetEntityIdFromUri(row[0]);
                if (!processedItems.Add(entityId))
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} has already been processed!");
                    problematicItems.Add(entityId);
                    continue;
                }

                // await Console.Error.WriteLineAsync($"Reading {entityId} ({counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, NoLanguages);

                if (!entity.Claims.ContainsKey(WikidataProperties.ParentOrganization))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain parent organization?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var parentOrgs = entity.Claims[WikidataProperties.ParentOrganization];
                if (parentOrgs.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {parentOrgs.Count} parent organizations?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var parentOrgClaim = parentOrgs.Single();

                if (parentOrgClaim.Qualifiers.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {parentOrgClaim.Qualifiers.Count} parent organization qualifiers");
                    problematicItems.Add(entityId);
                    continue;
                }
                var qualifier = parentOrgClaim.Qualifiers.Single();
                if (qualifier.PropertyId == WikidataProperties.ObjectRole)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} has been probably fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }
                if (qualifier.PropertyId != WikidataProperties.SubjectRole)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} uses a {qualifier.PropertyId} qualifier");
                    problematicItems.Add(entityId);
                    continue;
                }
                parentOrgClaim.Qualifiers.Clear();
                parentOrgClaim.Qualifiers.Add(new Snak(WikidataProperties.ObjectRole, qualifier.RawDataValue ?? throw new FormatException("Raw data value unavailable"),
                    qualifier.DataType ?? throw new FormatException("Data type unavailable")));

                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync([
                    new EntityEditEntry(nameof(entity.Claims), parentOrgClaim)
                ], EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}