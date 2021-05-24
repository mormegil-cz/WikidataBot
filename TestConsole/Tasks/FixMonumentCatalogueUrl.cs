using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestConsole.MWApi;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public class FixMonumentCatalogueUrl
    {
        private static readonly Regex reMatcher = new(@"^https://pamatkovykatalog.cz/\?mode=parametric&catalogNumber=([0-9]+)&presenter=ElementsResults$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const int BatchSize = 500;

        public static async Task Run(WikiSite wikidataSite)
        {
            var editGroupId = GenerateRandomEditGroupId();
            var editSummary = MakeEditSummary("Fixing reference URLs to Památkový katalog", editGroupId);

            int entityCount;
            do
            {
                await Console.Error.WriteLineAsync("Retrieving data from MW API");
                var entities = GetResultsFromApi(
                        await wikidataSite.InvokeMediaWikiApiAsync(new ExtUrlUsageRequestMessage("https", "pamatkovykatalog.cz/?mode=parametric&catalogNumber=", BatchSize), true, CancellationToken.None),
                        new List<string> { "query", "exturlusage" },
                        new List<string> { "title" }
                    )
                    .ToList();
                var counter = 0;
                entityCount = entities.Count;
                await Console.Error.WriteLineAsync($"Retrieved {entityCount} entities, processing...");
                foreach (var row in entities)
                {
                    var entityId = row[0];
                    await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{entityCount})");
                    var entity = new Entity(wikidataSite, entityId);
                    await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new[] { "cs" });

                    var refClaimsAndSnaks = entity.Claims
                        .SelectMany(c => c.References.Select(r => (c, r)))
                        .SelectMany(cr => cr.r.Snaks.Select(s => (cr.c, cr.r, s)))
                        .Where(crs => crs.s.PropertyId == "P854" && ((string) crs.s.DataValue).StartsWith("https://pamatkovykatalog.cz/?mode=parametric&catalogNumber="))
                        .ToList();

                    if (refClaimsAndSnaks.Count == 0)
                    {
                        await Console.Error.WriteLineAsync($"No more refs to fix at {entityId}");
                        continue;
                    }

                    foreach (var refClaimAndSnak in refClaimsAndSnaks)
                    {
                        var r = refClaimAndSnak.r;
                        var refSnak = refClaimAndSnak.s;
                        var url = (string) refSnak.DataValue;
                        var match = reMatcher.Match(url);
                        if (!match.Success)
                        {
                            await Console.Error.WriteLineAsync($"Error matching '{url}'");
                            return;
                        }
                        var identifier = match.Groups[1].Value;
                        refSnak.DataValue = "https://pamatkovykatalog.cz/soupis/podle-relevance/1/seznam?katCislo=" + identifier;
                        r.Snaks.Add(
                            new Snak("P4075", identifier, BuiltInDataTypes.String)
                        );
                    }

                    var edits = refClaimsAndSnaks.Select(cs => new EntityEditEntry(nameof(Entity.Claims), cs.c)).ToList();
                    await Console.Error.WriteLineAsync($"Editing {entityId} ({edits.Count} claims)");
                    var options = EntityEditOptions.Bot;
                    if (edits.Count > 1) options |= EntityEditOptions.Bulk;
                    await entity.EditAsync(edits, editSummary, options);
                }

                if (entityCount == BatchSize)
                {
                    await Console.Error.WriteLineAsync($"...");
                    Thread.Sleep(30000);
                }
            } while (entityCount == BatchSize);

            await Console.Error.WriteLineAsync("Done!");
        }
    }
}