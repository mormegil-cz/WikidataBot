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

public class RemapFotbalIdnes
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Migrating changed fotbal.idnes.cz IDs", EditGroupId);

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
SELECT ?item ?ident WHERE {
  ?item wdt:P3663 ?ident.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  BIND (STRLEN(?ident) AS ?len)
  FILTER(?len = 7)
}
LIMIT 100
"), new Dictionary<string, string> { { "item", "uri" }, { "ident", "literal" } }).ToList();
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

                if (!entity.Claims.ContainsKey(WikidataProperties.FotbalIdnesId))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain HQ?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var identClaims = entity.Claims[WikidataProperties.FotbalIdnesId];
                if (identClaims.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {identClaims.Count} fotbal.idnes.cz claims");
                    problematicItems.Add(entityId);
                    continue;
                }
                var currentIdentClaim = identClaims.Single();
                var currentIdentMainSnak = currentIdentClaim.MainSnak;
                var currentIdent = (string)currentIdentMainSnak.DataValue ?? throw new FormatException("Missing ident in WQS response!");
                if (currentIdent.Length != 7)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has probably been fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }

                var convertedIdent = await FotbalIdnesApi.ConvertOldIdent(currentIdent);
                if (convertedIdent == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to convert '{currentIdent}' for {entityId}");
                    problematicItems.Add(entityId);
                    continue;
                }

                currentIdentMainSnak.DataValue = convertedIdent;
                currentIdentClaim.References.Add(
                    new ClaimReference(
                        new Snak(WikidataProperties.ReferenceUrl, FotbalIdnesApi.GetOldIdentUrl(currentIdent), BuiltInDataTypes.Url),
                        new Snak(WikidataProperties.AccessDate, accessDate, BuiltInDataTypes.Time)
                    )
                );

                var edits = new List<EntityEditEntry>
                {
                    new(nameof(Entity.Claims), currentIdentClaim)
                };
                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}