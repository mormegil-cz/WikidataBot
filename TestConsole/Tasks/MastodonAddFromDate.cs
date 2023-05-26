using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TestConsole.Integration.Mastodon;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Tasks;

using static WikidataTools;

public class MastodonAddFromDate
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Adding start date to Mastodon accounts", EditGroupId);
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
  ?item p:P4033 ?stmt.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  MINUS { ?stmt wikibase:rank wikibase:DeprecatedRank }
  MINUS { ?stmt pq:P580 [] }
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
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicItems.Add(entityId);
                    continue;
                }

                if (!entity.Claims.ContainsKey(WikidataProperties.Mastodon))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain Mastodon?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var claims = entity.Claims[WikidataProperties.Mastodon];
                var edits = new List<EntityEditEntry>();
                var problem = false;
                foreach (var claim in claims)
                {
                    if (claim.Rank == "deprecated") continue;
                    if (claim.Qualifiers.Any(q => q.PropertyId == WikidataProperties.StartTime)) continue;

                    var mastodonAccountId = (string) claim.MainSnak.DataValue;
                    WbTime? startTime;
                    var profileUrl = await MastodonApi.GetProfileUrl(mastodonAccountId, entityId);
                    if (profileUrl == null)
                    {
                        problematicItems.Add(entityId);
                        problem = true;
                        continue;
                    }

                    startTime = await MastodonApi.GetAccountRegistrationDate(mastodonAccountId, profileUrl, entityId);
                    if (startTime == null)
                    {
                        problematicItems.Add(entityId);
                        problem = true;
                        continue;
                    }

                    claim.Qualifiers.Add(new Snak(WikidataProperties.StartTime, startTime, BuiltInDataTypes.Time));
                    edits.Add(new(nameof(Entity.Claims), claim));
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