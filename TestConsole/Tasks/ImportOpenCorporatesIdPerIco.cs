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
    public static class ImportOpenCorporatesIdPerIco
    {
        private static readonly string EditGroupId = GenerateRandomEditGroupId();
        private static readonly string EditSummary = MakeEditSummary("Adding OpenCorporates ID for Czech companies per IČO", EditGroupId);

        public static async Task Run(WikiSite wikidataSite)
        {
            var batch = 0;
            var problematicItems = new HashSet<string>();
            while (true)
            {
                ++batch;
                await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
                var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?ico WHERE {
  ?item wdt:P4156 ?ico;
    wdt:P17 wd:Q213.
  VALUES ?form {
    wd:Q3742494
    wd:Q15646299
    wd:Q43749575
    wd:Q279014
    wd:Q56517350
    wd:Q12041908
    wd:Q43751707
    wd:Q16917889
    wd:Q56457912
  }
  ?item wdt:P1454 ?form.
  MINUS { ?item wdt:P1320 _:b3. }
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
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

                    if (entity.Claims.ContainsKey("P1320"))
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} already contains ID");
                        problematicItems.Add(entityId);
                        continue;
                    }

                    var ico = row[1];
                    var openCorporatesId = "cz/" + ico;

                    var edits = new List<EntityEditEntry>
                    {
                        new(nameof(Entity.Claims), new Claim(new Snak("P1320", openCorporatesId, BuiltInDataTypes.ExternalId))),
                    };
                    await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                    await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                }
            }

            await Console.Error.WriteLineAsync("Done!");
        }
    }
}