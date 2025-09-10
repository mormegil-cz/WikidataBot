using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestConsole.Integration.AresV3;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixNameFromRejskol
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing broken official name from Rejskol", EditGroupId);

    private const string EntityQidAres = "Q8182488";

    public static async Task Run(WikiSite wikidataSite)
    {
        var accessTimestamp = DateTime.UtcNow;
        var accessDate = new WbTime(accessTimestamp.Year, accessTimestamp.Month, accessTimestamp.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);

        var batch = 0;
        var problematicItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?ico WHERE {
  ?item wdt:P4156 ?ico;
    wdt:P1448 ?name.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  FILTER(CONTAINS(?name, '|'))
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
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, ["cs"]);

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

                var ico = row[1] ?? throw new FormatException("Missing ico in WQS response!");
                var aresData = await AresRestApi.GetAresData(ico);
                if (aresData == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to find IČO '{ico}' for {entityId} in ARES");
                    problematicItems.Add(entityId);
                    continue;
                }

                var aresName = aresData.ObchodniJmeno;
                if (aresName == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! No official name in ARES for {entityId} ({ico})");
                    problematicItems.Add(entityId);
                    continue;
                }

                var edits = new List<EntityEditEntry>();
                var problematic = false;

                var nameClaims = entity.Claims[WikidataProperties.OfficialName];

                foreach (var currentNameClaim in nameClaims.Where(claim => claim.Rank != "deprecated"))
                {
                    var currentNameMonolingualText = (WbMonolingualText?) currentNameClaim.MainSnak.DataValue;
                    if (currentNameMonolingualText?.Language != "cs")
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} uses {currentNameMonolingualText?.Language}");
                        continue;
                    }

                    var currentName = currentNameMonolingualText.GetValueOrDefault().Text;
                    if (!currentName.Contains('|'))
                    {
                        // not this one
                        continue;
                    }

                    if (!SimilarEnough(currentName, aresName))
                    {
                        // not this one
                        continue;
                    }

                    // var accessDate = WbTime.FromDateTime(DateTime.UtcNow, WikibaseTimePrecision.Second);

                    if (aresName == currentName)
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Official name in ARES for {entityId} ({ico}) includes the pipe?!");
                        problematic = true;
                        continue;
                    }

                    if (currentNameClaim.Qualifiers.Any())
                    {
                        await Console.Error.WriteLineAsync($"WARNING! {entityId} is qualified with unsupported qualifier");
                        problematic = true;
                        continue;
                    }

                    currentNameClaim.MainSnak.DataValue = new WbMonolingualText("cs", aresName);

                    currentNameClaim.References.Add(new ClaimReference(
                        new Snak(WikidataProperties.StatedIn, EntityQidAres, BuiltInDataTypes.WikibaseItem),
                        new Snak(WikidataProperties.ReferenceUrl, AresRestApi.GetAresUrl(ico), BuiltInDataTypes.Url),
                        new Snak(WikidataProperties.AccessDate, accessDate, BuiltInDataTypes.Time)
                    ));

                    edits.Add(new EntityEditEntry(nameof(Entity.Claims), currentNameClaim));
                }

                if (edits.Count == 0)
                {
                    if (!problematic) await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has probably been fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }

                await Console.Error.WriteAsync($"Editing {entityId} ({counter}/{count})");
                if (nameClaims.Count != 1) await Console.Error.WriteAsync($" ({edits.Count}/{nameClaims.Count} names changed)");
                await Console.Error.WriteLineAsync();
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }

    private static bool SimilarEnough(string name1, string name2)
        => name1.Where(IsLetterOrDigit).SequenceEqual(name2.Where(IsLetterOrDigit));

    private static bool IsLetterOrDigit(char arg) => Char.IsLetter(arg) || Char.IsAsciiDigit(arg);
}