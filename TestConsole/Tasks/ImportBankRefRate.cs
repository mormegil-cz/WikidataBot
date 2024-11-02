using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public class ImportBankRefRate
{
    private static readonly string[] Languages = [];

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Import reference loan rates for ČNB", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        var csCulture = CultureInfo.GetCultureInfo("cs-CZ");
        var rateData = (await File.ReadAllLinesAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "vyvoj_repo_historie.txt")))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('|'))
            .Select(columns => new { validFrom = DateTime.ParseExact(columns[0], "yyyyMMdd", CultureInfo.InvariantCulture), rate = Decimal.Parse(columns[1], NumberStyles.AllowDecimalPoint, csCulture) })
            .ToList();

        var entity = new Entity(wikidataSite, "Q251062");
        await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

        if (entity.Claims == null)
        {
            await Console.Error.WriteLineAsync("Unable to read entity!");
            return;
        }

        if (entity.Claims.ContainsKey(WikidataProperties.ReferenceRate))
        {
            await Console.Error.WriteLineAsync("Reference rate already present");
            return;
        }

        var claimReference = new ClaimReference(
            new Snak(WikidataProperties.Title, new WbMonolingualText("cs", "Jak se vyvíjela dvoutýdenní repo sazba ČNB?"), BuiltInDataTypes.MonolingualText),
            new Snak(WikidataProperties.Publisher, "Q251062", BuiltInDataTypes.WikibaseItem),
            new Snak(WikidataProperties.ReferenceUrl, "https://www.cnb.cz/cs/casto-kladene-dotazy/Jak-se-vyvijela-dvoutydenni-repo-sazba-CNB/", BuiltInDataTypes.Url),
            new Snak(WikidataProperties.AccessDate, new WbTime(2024, 11, 2, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time)
        );
        
        var edits = new List<EntityEditEntry>(rateData.Count);
        for (var rowIndex = 0; rowIndex < rateData.Count; rowIndex++)
        {
            var line = rateData[rowIndex];
            var nextLine = rowIndex < rateData.Count - 1 ? rateData[rowIndex + 1] : null;

            var claim = new Claim(new Snak(WikidataProperties.ReferenceRate, new WbQuantity((double) line.rate, WikidataProperties.UnitPercent), BuiltInDataTypes.Quantity));
            claim.Qualifiers.Add(new Snak(WikidataProperties.StartTime, new WbTime(line.validFrom.Year, line.validFrom.Month, line.validFrom.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time));
            if (nextLine != null)
            {
                var validTo = nextLine.validFrom.AddDays(-1);
                claim.Qualifiers.Add(new Snak(WikidataProperties.EndTime, new WbTime(validTo.Year, validTo.Month, validTo.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time));
            }
            else
            {
                claim.Rank = "preferred";
            }
            claim.References.Add(claimReference);
            edits.Add(new EntityEditEntry(nameof(Entity.Claims), claim));
        }

        await Console.Out.WriteAsync("Editing...");
        await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot | EntityEditOptions.Bulk);
        await Console.Out.WriteLineAsync();
    }
}