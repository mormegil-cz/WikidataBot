using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class UpdateDisambigDescription
    {
        private static readonly string EditGroupId = GenerateRandomEditGroupId();
        private static readonly string EditSummary = MakeEditSummary("Unifying cs descriptions on disambiguations", EditGroupId);

        public static async Task Run(WikiSite wikidataSite)
        {
            var batch = 0;
            var duplicateItems = new HashSet<(string, string)>();
            while (true)
            {
                ++batch;
                await Console.Error.WriteLineAsync($"Batch #{batch} Retrieving data from WQS");
                var entities = GetEntities(await GetSparqlResults(@"
SELECT DISTINCT ?item WHERE {
	?item wdt:P31 wd:Q4167410;
          schema:description 'rozcestník'@cs.
  MINUS {
    VALUES ?item { " + String.Join(' ', duplicateItems.Select(item => "wd:" + item.Item1)) + @" }
  }
}
LIMIT 100
"), new Dictionary<string, string> { { "item", "uri" } }).ToList();
                if (entities.Count == 0) break;

                var counter = 0;
                var count = entities.Count;
                await Console.Error.WriteLineAsync($"Retrieved {count} entities, processing...");
                foreach (var row in entities)
                {
                    var entityId = GetEntityIdFromUri(row[0]);
                    await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{count})");
                    var entity = new Entity(wikidataSite, entityId);
                    await entity.RefreshAsync(EntityQueryOptions.FetchDescriptions, new[] { "cs" });

                    if (entity.Descriptions == null)
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                        duplicateItems.Add((entityId, null));
                        continue;
                    }

                    var currDesc = entity.Descriptions["cs"];
                    if (currDesc != "rozcestník")
                    {
                        if (currDesc != "rozcestník na projektech Wikimedia") await Console.Error.WriteLineAsync($"WARNING! Entity {entityId}: Unexpected description {currDesc}'");
                        duplicateItems.Add((entityId, null));
                        continue;
                    }

                    // Make a some changes
                    var edits = new List<EntityEditEntry>
                    {
                        // Update a claim
                        new(nameof(Entity.Descriptions), new WbMonolingualText("cs", "rozcestník na projektech Wikimedia")),
                    };
                    await Console.Error.WriteLineAsync($"Editing {entityId}");
                    try
                    {
                        await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                    }
                    catch (OperationFailedException e)
                    {
                        var msg = e.ErrorMessage;
                        if (e.ErrorCode == "modification-failed" && msg.Contains("using the same description text"))
                        {
                            var startIdx = msg.IndexOf("[[", StringComparison.Ordinal);
                            var endIdx = msg.IndexOf('|', startIdx + 2);
                            var duplicateItem = msg.Substring(startIdx + 2, endIdx - startIdx - 2);
                            await Console.Error.WriteLineAsync($"DUPLICATE: Entity {entityId} is duplicate to {duplicateItem}");
                            duplicateItems.Add((entityId, duplicateItem));
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            await Console.Error.WriteLineAsync("Done!");
        }
    }
}