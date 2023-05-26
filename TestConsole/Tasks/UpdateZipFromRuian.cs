using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public class UpdateZipFromRuian
{
    private const string BasePath = @"y:\_mine\wikidata-imports\psc";
    private static readonly DateOnly ImportCsvDate = new(2023, 04, 30);
    private static readonly DateTime ImportTimestamp = DateTime.UtcNow;

    private static readonly string ImportCsvFile = $"{ImportCsvDate:yyyyMMdd}_OB_ADR_csv.zip";
    private static readonly WbTime importCsvWbTime = new(ImportCsvDate.Year, ImportCsvDate.Month, ImportCsvDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);
    private static readonly string importDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static readonly WbTime importWbTime = new(ImportTimestamp.Year, ImportTimestamp.Month, ImportTimestamp.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Updating ZIP codes for streets according to RÚIAN", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1250 = Encoding.GetEncoding(1250);

        // RUIAN -> [known PSČ]
        var itemsWithZips = new Dictionary<int, HashSet<string>>();
        // QID -> [new/missing PSČ]
        var zipsForStreet = new Dictionary<string, HashSet<string>>();
        // [unknown RUIAN]
        var unknownRuianIDs = new HashSet<int>();

        // RUIAN -> QID
        var itemForRuian = new Dictionary<int, string>();
        foreach (var row in GetEntities(await GetSparqlResults(@"
SELECT ?item ?ruian ?zip WHERE {
  ?item wdt:P4533 ?ruian.
  OPTIONAL { ?item wdt:P281 ?zip }
}
"), new Dictionary<string, string> { { "item", "uri" }, { "ruian", "literal" }, { "zip", "literal" } }))
        {
            var qid = row[0]!;
            var ruian = Int32.Parse(row[1]!, CultureInfo.InvariantCulture);
            var zipCode = row[2];

            if (itemForRuian.TryGetValue(ruian, out var currentForRuian))
            {
                if (currentForRuian != qid) Console.WriteLine("WARNING: Mismatch for street #{0}: {1} vs {2}", ruian, currentForRuian, qid);
            }
            else
            {
                itemForRuian.Add(ruian, qid);
            }

            itemForRuian[ruian] = qid;
            if (zipCode != null)
            {
                if (!itemsWithZips.TryGetValue(ruian, out var zipsOfItem))
                {
                    zipsOfItem = new();
                    itemsWithZips.Add(ruian, zipsOfItem);
                }

                if (zipCode.Length != 6) Console.WriteLine("WARNING: Zip '{0}' for street {1} ({2}) is badly formatted", zipCode, ruian, qid);
                zipsOfItem.Add(CleanZip(zipCode));
            }
        }

        // QID -> [currently known/remaining ZIP]
        var itemsWithZipsRemaining = new Dictionary<String, HashSet<string>>(itemsWithZips.Count);
        foreach (var entry in itemsWithZips)
        {
            if (!itemForRuian.TryGetValue(entry.Key, out var qid))
            {
                Console.WriteLine("WARNING: Unknown RUIAN {0}!?", entry.Key);
                continue;
            }
            if (!itemsWithZipsRemaining.TryGetValue(qid, out var set))
            {
                set = new HashSet<string>(entry.Value.Count + 1);
                itemsWithZipsRemaining.Add(qid, set);
            }
            set.UnionWith(entry.Value);
        }

        using (var zip = ZipFile.OpenRead(Path.Combine(BasePath, ImportCsvFile)))
        {
            foreach (var entry in zip.Entries)
            {
                using var csv = new StreamReader(entry.Open(), cp1250);

                // skip header
                await csv.ReadLineAsync();

                string? line;
                while ((line = await csv.ReadLineAsync()) != null)
                {
                    if (line.Contains('"')) throw new FormatException("Escaped CSV values not supported");
                    var fields = line.Split(';');
                    var streetIdStr = fields[9];
                    var zipCode = fields[15];
                    if (streetIdStr.Length == 0) continue;
                    if (zipCode.Length != 5 || streetIdStr.Length < 2 || streetIdStr.Length > 7) throw new FormatException($"Unexpected values: '{streetIdStr}' '{zipCode}' at '{entry.Name}': '{line}'");
                    Int32.Parse(zipCode, CultureInfo.InvariantCulture);
                    var streetId = Int32.Parse(streetIdStr, CultureInfo.InvariantCulture);
                    var cleanZip = CleanZip(zipCode);

                    if (!itemForRuian.TryGetValue(streetId, out var qid))
                    {
                        if (!unknownRuianIDs.Contains(streetId))
                        {
                            Console.WriteLine("WARNING: Street ID {0} not found", streetId);
                            unknownRuianIDs.Add(streetId);
                        }
                        continue;
                    }

                    if (itemsWithZips.TryGetValue(streetId, out var alreadyZipSet))
                    {
                        if (!alreadyZipSet.Contains(cleanZip))
                        {
                            alreadyZipSet.Add(cleanZip);
                            // Console.WriteLine("ZIP {0} is missing for street ID {1}", zipCode, streetId);
                        }
                        else
                        {
                            itemsWithZipsRemaining[qid].Remove(cleanZip);
                            continue;
                        }
                    }

                    if (!zipsForStreet.TryGetValue(qid, out var zipList))
                    {
                        zipList = new HashSet<string>(1);
                        zipsForStreet.Add(qid, zipList);
                    }
                    // Console.WriteLine("Adding ZIP {0} for street ID {1} ({2})", zipCode, streetId, qid);
                    zipList.Add(FormatZip(zipCode));
                }
            }
        }

        int counter = 0;
        var itemsWithZipToRemove = itemsWithZipsRemaining.Where(r => r.Value.Count > 0).ToList();
        var count = itemsWithZipToRemove.Count;
        foreach (var e in itemsWithZipToRemove)
        {
            Console.WriteLine($"Street {e.Key} has zip(s) {String.Join(", ", e.Value)} it should not have ({++counter}/{count})");

            var entityId = GetEntityIdFromUri(e.Key);
            var entity = new Entity(wikidataSite, entityId);
            await entity.RefreshAsync(EntityQueryOptions.FetchClaims, new[] { "cs" });

            if (entity.Claims == null)
            {
                await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                continue;
            }

            if (!entity.Claims.ContainsKey("P281"))
            {
                await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain a ZIP code");
                continue;
            }

            var edits = new List<EntityEditEntry>();
            foreach (var zip in e.Value)
            {
                var zipToRemove = FormatZip(zip);
                var claim = entity.Claims["P281"].Where(claim => claim.MainSnak.SnakType == SnakType.Value && (string)claim.MainSnak.DataValue == zipToRemove).ToList();
                if (claim.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} contains {claim.Count} claims with {zipToRemove}");
                    continue;
                }
                edits.Add(new EntityEditEntry(nameof(Entity.Claims), claim.Single(), EntityEditEntryState.Removed));
            }
            await Console.Error.WriteLineAsync($"Editing {entityId}...");
            await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
        }

        counter = 0;
        count = zipsForStreet.Count;
        foreach (var street in zipsForStreet.OrderBy(p => p.Key))
        {
            Console.WriteLine($"Street {street.Key} needs {street.Value.Count} new zip(s) ({++counter}/{count})");

            var entityId = GetEntityIdFromUri(street.Key);
            var entity = new Entity(wikidataSite, entityId);
            await entity.RefreshAsync(EntityQueryOptions.FetchClaims, new[] { "cs" });

            if (entity.Claims == null)
            {
                await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                continue;
            }

            var edits = new List<EntityEditEntry>();
            foreach (var zip in street.Value.OrderBy(v => v))
            {
                var claim = new Claim(new Snak("P281", zip, BuiltInDataTypes.String));
                claim.References.Add(new ClaimReference(
                    new Snak("P248", "Q12049125", BuiltInDataTypes.WikibaseItem),
                    new Snak("P577", importCsvWbTime, BuiltInDataTypes.Time),
                    new Snak("P854", "https://vdp.cuzk.cz/vymenny_format/csv/" + ImportCsvFile, BuiltInDataTypes.Url),
                    new Snak("P813", importWbTime, BuiltInDataTypes.Time)
                ));

                edits.Add(new(nameof(Entity.Claims), claim));
            }
            await Console.Error.WriteLineAsync($"Editing {entityId}...");
            await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
        }
    }

    private static string CleanZip(string zipCode) => zipCode.Replace(" ", "").Replace("\u00A0", "");

    private static string FormatZip(string zipCode)
    {
        zipCode = CleanZip(zipCode);
        if (zipCode.Length != 5) throw new FormatException();
        return zipCode.Insert(3, "\u00A0");
    }
}