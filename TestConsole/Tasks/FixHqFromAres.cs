using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestConsole.Integration.Ares;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixHqFromAres
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Fixing broken HQ location from ARES", EditGroupId);

    private const string EntityQidAres = "Q8182488";
    private static readonly HashSet<string> KnownQualifierProperties = new() { WikidataProperties.Street, WikidataProperties.ConscriptionNumber, WikidataProperties.StreetNumber, WikidataProperties.ZipCode, WikidataProperties.LocatedInAdminEntity, WikidataProperties.Coordinates };

    public static async Task Run(WikiSite wikidataSite)
    {
        var accessTimestamp = DateTime.UtcNow;
        var accessDate = new WbTime(accessTimestamp.Year, accessTimestamp.Month, accessTimestamp.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);

        var labelCache = WikidataCache.CreateLabelCache(wikidataSite, "cs");
        var streetUriCache = WikidataCache.CreateSparqlCache(@"
SELECT ?item WHERE {
    ?item wdt:P4533 '$PARAM$'.
}
", "item", "uri");
        var municipalityUriCache = WikidataCache.CreateSparqlCache(@"
SELECT ?item WHERE {
    ?item wdt:P7606 '$PARAM$'.
}
", "item", "uri");

        var batch = 0;
        var problematicItems = new HashSet<string>();
        while (true)
        {
            ++batch;
            await Console.Error.WriteAsync($"Batch #{batch} Retrieving data from WQS...");
            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?ico WHERE {
  ?item wdt:P4156 ?ico;
    wdt:P17 wd:Q213;
    (p:P159/pq:P281) ?zip.
  MINUS {
    VALUES ?item { " + String.Join(' ', problematicItems.Select(item => "wd:" + item)) + @" }
  }
  FILTER(REGEX(?zip, '^[^0-9]'))
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

                if (!entity.Claims.ContainsKey(WikidataProperties.HqLocation))
                {
                    // ??!?
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain HQ?!");
                    problematicItems.Add(entityId);
                    continue;
                }

                var hqClaims = entity.Claims[WikidataProperties.HqLocation];
                if (hqClaims.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has {hqClaims.Count} HQ claims");
                    problematicItems.Add(entityId);
                    continue;
                }
                var currentHqClaim = hqClaims.Single();

                if (!currentHqClaim.Qualifiers.Any(q => q.PropertyId == WikidataProperties.ZipCode && !Char.IsNumber(((string)q.DataValue)[0])))
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has probably been fixed already?");
                    problematicItems.Add(entityId);
                    continue;
                }

                // var accessDate = WbTime.FromDateTime(DateTime.UtcNow, WikibaseTimePrecision.Second);

                var ico = row[1] ?? throw new FormatException("Missing ico in WQS response!");
                var aresData = await AresRestApi.GetAresData(ico);
                if (aresData == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to find IČO '{ico}' for {entityId} in ARES");
                    problematicItems.Add(entityId);
                    continue;
                }
                if (aresData.AA?.AU == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! No address data in ARES for {entityId} ({ico})");
                    problematicItems.Add(entityId);
                    continue;
                }

                var hqLocationAddress = aresData.AA;
                if (hqLocationAddress.KS != "203")
                {
                    await Console.Error.WriteLineAsync($"WARNING! Ignoring non-CZ HQ location for {entityId} ({ico}): {hqLocationAddress.KS}");
                    problematicItems.Add(entityId);
                    continue;
                }
                var hqLocationAddressCodes = aresData.AA.AU;

                var municipalityRuian = hqLocationAddressCodes.KO;
                if (municipalityRuian == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! No municipality in ARES for {entityId} ({ico})");
                    problematicItems.Add(entityId);
                    continue;
                }
                var streetRuian = hqLocationAddressCodes.KUL;

                if (currentHqClaim.Qualifiers.Any(q => !KnownQualifierProperties.Contains(q.PropertyId)))
                {
                    await Console.Error.WriteLineAsync($"WARNING! {entityId} is qualified with unsupported qualifier");
                    problematicItems.Add(entityId);
                    continue;
                }

                var aresMunicipalityQid = await GetQidFromCache(municipalityUriCache, municipalityRuian);
                var aresStreetQid = streetRuian == null ? null : await GetQidFromCache(streetUriCache, streetRuian);

                // var currentHqMunicipalityQid = (string)currentHqClaim.MainSnak.DataValue;
                // if (currentHqMunicipalityQid != aresMunicipalityQid)
                // {
                //     await Console.Error.WriteLineAsync($"WARNING! HQ municipality mismatch for {entityId}: {currentHqMunicipalityQid} vs {aresMunicipalityQid} ({hqLocationAddress.N})");
                //     problematicItems.Add(entityId);
                //     continue;
                // }

                var newHqClaim = new Claim(new Snak(WikidataProperties.HqLocation, aresMunicipalityQid, BuiltInDataTypes.WikibaseItem));
                if (aresStreetQid != null) newHqClaim.Qualifiers.Add(new Snak(WikidataProperties.Street, aresStreetQid, BuiltInDataTypes.WikibaseItem));
                if (hqLocationAddressCodes.CDSpecified) newHqClaim.Qualifiers.Add(new Snak(WikidataProperties.ConscriptionNumber, StreetNumberType(hqLocationAddressCodes.TCD, hqLocationAddressCodes.TCDSpecified) + hqLocationAddressCodes.CD, BuiltInDataTypes.String));
                if (hqLocationAddressCodes.CO != null) newHqClaim.Qualifiers.Add(new Snak(WikidataProperties.StreetNumber, hqLocationAddressCodes.CO, BuiltInDataTypes.String));
                if (hqLocationAddressCodes.PSC != null) newHqClaim.Qualifiers.Add(new Snak(WikidataProperties.ZipCode, FormatZip(hqLocationAddressCodes.PSC), BuiltInDataTypes.String));

                newHqClaim.References.Add(new ClaimReference(
                    new Snak(WikidataProperties.StatedIn, EntityQidAres, BuiltInDataTypes.WikibaseItem),
                    new Snak(WikidataProperties.ReferenceUrl, AresRestApi.GetAresUrl(ico), BuiltInDataTypes.Url),
                    new Snak(WikidataProperties.AccessDate, accessDate, BuiltInDataTypes.Time)
                ));

                var edits = new List<EntityEditEntry>
                {
                    new(nameof(Entity.Claims), currentHqClaim, EntityEditEntryState.Removed),
                    new(nameof(Entity.Claims), newHqClaim)
                };
                await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
        }

        await Console.Error.WriteLineAsync("Done!");
    }

    private static string FormatZip(string zip)
    {
        if (zip.Length != 5) throw new FormatException("Unknown ZIP format");
        return zip.Insert(3, "\u00A0");
    }

    private static string StreetNumberType(sbyte type, bool typeSpecified)
    {
        if (!typeSpecified) return "";
        return type switch
        {
            1 => "",
            2 => "č.ev.",
            _ => throw new FormatException($"Unsupported street number type {type}")
        };
    }

    private static async Task<string> GetQidFromCache(WikidataCache<string, IList<string?>> cache, string param)
    {
        var result = await cache.Get(param);
        if (result.Count != 1) throw new FormatException($"Unable to determine entity for '{param}'");
        var uri = result.Single() ?? throw new FormatException("Invalid entity for '{param}'");
        return GetEntityIdFromUri(uri);
    }
}