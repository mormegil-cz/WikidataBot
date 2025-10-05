using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixSchoolDescriptionDeclension
{
    private static readonly HashSet<string> LanguageCs = ["cs"];

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing description declension after Rejskol import", EditGroupId);

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
                SELECT ?item (SAMPLE(?wrongDescription) AS ?badDesc) (SAMPLE(?correctDescription) AS ?goodDesc) WHERE {
                {
                  { ?item wdt:P31 wd:Q9842 }
                  UNION
                  { ?item wdt:P31 wd:Q126807 }
                  UNION
                  { ?item wdt:P31 wd:Q159334 }
                }
                ?item
                  wdt:P17 wd:Q213;
                  (wdt:P131/p:P1448/pq:P7018) ?lexemeSense.
                ?lexeme ontolex:sense ?lexemeSense.
                OPTIONAL {
                  ?lexeme ontolex:lexicalForm ?lexemeFormNomS.
                  ?lexemeFormNomS wikibase:grammaticalFeature wd:Q131105, wd:Q110786.
                  ?lexemeFormNomS ontolex:representation ?noms.
                }
                OPTIONAL {
                  ?lexeme ontolex:lexicalForm ?lexemeFormNomP.
                  ?lexemeFormNomP wikibase:grammaticalFeature wd:Q131105, wd:Q146786.
                  ?lexemeFormNomP ontolex:representation ?nomp.
                }
                OPTIONAL {
                  ?lexeme ontolex:lexicalForm ?lexemeFormLocS.
                  ?lexemeFormLocS wikibase:grammaticalFeature wd:Q202142, wd:Q110786.
                  ?lexemeFormLocS ontolex:representation ?locs.
                }
                OPTIONAL {
                  ?lexeme ontolex:lexicalForm ?lexemeFormLocP.
                  ?lexemeFormLocP wikibase:grammaticalFeature wd:Q202142, wd:Q146786.
                  ?lexemeFormLocP ontolex:representation ?locp.
                }
                BIND(STRLANG(CONCAT("škola v ", STR(COALESCE(?noms, ?nomp))), "cs") AS ?wrongDescription)
                ?item schema:description ?wrongDescription.
                BIND(STRLANG(CONCAT("škola v ", STR(COALESCE(?locs, ?locp))), "cs") AS ?correctDescription)
                FILTER (?wrongDescription != ?correctDescription)
                MINUS {
                  VALUES ?item {
                """ + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
                  }
                }
                GROUP BY ?item
                LIMIT 400"
            ), new Dictionary<string, string> { { "item", "uri" }, { "badDesc", "literal" }, { "goodDesc", "literal" } }).ToList();
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
                var badDesc = row[1] ?? throw new FormatException("Missing badDesc in WQS response!");
                var goodDesc = row[2] ?? throw new FormatException("Missing goodDesc in WQS response!");

                // await Console.Error.WriteLineAsync($"Reading {entityId} ({counter}/{count})");
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchDescriptions, LanguageCs);

                if (!entity.Descriptions.ContainsLanguage("cs"))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain Czech description?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var goodDescNbsp = goodDesc.Replace(" v ", " v ");

                var currentCzechDescription = entity.Descriptions["cs"];
                if (currentCzechDescription == goodDescNbsp)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has already been processed?");
                    problematicItems.Add(entityId);
                    continue;
                }
                if (currentCzechDescription != badDesc)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has unexpected description?!?");
                    problematicItems.Add(entityId);
                    continue;
                }

                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync([
                    new EntityEditEntry(nameof(entity.Descriptions), new WbMonolingualText("cs", goodDescNbsp))
                ], EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}