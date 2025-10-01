using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixSchoolLabels
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing useless labels from Rejskol import", EditGroupId);

    private static readonly FrozenSet<string> DroppedSuffixes =
    [
        ", příspěvková organizace",
        " - příspěvková organizace",
        " – příspěvková organizace",
        "-příspěvková organizace",
        "–příspěvková organizace",
        " příspěvková organizace",
    ];

    public static async Task Run(WikiSite wikidataSite)
    {
        var batch = 0;
        var problematicItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item WHERE {
  ?item wdt:P31 wd:Q9842;
        wdt:P1448 ?name;
        rdfs:label ""Základní škola""@cs.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  FILTER (?name != ""Základní škola""@cs)
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
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims | EntityQueryOptions.FetchLabels | EntityQueryOptions.FetchAliases, null);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    problematicItems.Add(entityId);
                    continue;
                }

                if (!entity.Claims.ContainsKey(WikidataProperties.OfficialName))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain official name?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var problematic = false;

                var nameClaims = entity.Claims[WikidataProperties.OfficialName].Where(claim => claim.Rank != "deprecated").ToList();
                if (nameClaims.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {nameClaims.Count} non-deprecated name claims");
                    problematicItems.Add(entityId);
                    continue;
                }

                var currentNameMonolingualText = (WbMonolingualText?)nameClaims.Single().MainSnak.DataValue;
                if (currentNameMonolingualText?.Language != "cs")
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} uses {currentNameMonolingualText?.Language}");
                    problematicItems.Add(entityId);
                    continue;
                }
                var officialName = currentNameMonolingualText.GetValueOrDefault().Text;
                var shortenedOfficialName = ShortenOfficialName(officialName);

                // remove redundant labels
                var edits = entity.Labels
                    .Where(label => label.Text == "Základní škola" && label.Language != "cs" && label.Language != "mul")
                    .Select(label => new EntityEditEntry(nameof(Entity.Labels), label, EntityEditEntryState.Removed))
                    .ToList();

                // remove redundant aliases
                edits.AddRange(
                    entity.Aliases
                        .Where(alias => alias.Text == "Základní škola" || (alias.Text == officialName && alias.Language != "mul") || alias.Text.Replace("|", "") == officialName)
                        .Select(alias => new EntityEditEntry(nameof(Entity.Aliases), alias, EntityEditEntryState.Removed))
                );

                if (!entity.Labels.ContainsLanguage("cs") || entity.Labels["cs"] == "Základní škola")
                {
                    var czechLabel = new WbMonolingualText("cs", shortenedOfficialName);
                    edits.Add(new EntityEditEntry(nameof(Entity.Labels), czechLabel));
                }
                if (!entity.Labels.ContainsLanguage("mul") || entity.Labels["mul"] == "Základní škola")
                {
                    var mulLabel = new WbMonolingualText("mul", shortenedOfficialName);
                    edits.Add(new EntityEditEntry(nameof(Entity.Labels), mulLabel));
                }
                if (officialName != shortenedOfficialName && entity.Aliases.TryGetMonolingualTexts("mul").All(alias => alias.Text != officialName))
                {
                    var mulAlias = new WbMonolingualText("mul", officialName);
                    edits.Add(new EntityEditEntry(nameof(Entity.Aliases), mulAlias));
                }

                if (edits.Count == 0)
                {
                    if (!problematic) await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has probably been fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }

                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }

    private static string ShortenOfficialName(string officialName) => DropDistrict(DropSuffix(officialName));

    private static string DropSuffix(string officialName)
    {
        var droppedSuffix = DroppedSuffixes.FirstOrDefault(officialName.EndsWith);
        return droppedSuffix == null
            ? officialName
            : officialName[..^droppedSuffix.Length];
    }

    private static readonly char[] Separators = ",;:–0123456789".ToArray();

    private static string DropDistrict(string officialName)
    {
        var index = officialName.LastIndexOfAny(Separators);
        if (index <= 0 || index > officialName.Length - 8) return officialName;
        return officialName.Substring(index, 8) == ", okres "
            ? officialName[..index]
            : officialName;
    }
}