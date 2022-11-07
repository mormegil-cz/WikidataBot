using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public class CovIdFixImport
    {
        private static readonly string EditGroupId = GenerateRandomEditGroupId();
        private static readonly string EditSummary = MakeEditSummary("Reimport of changed ČOV IDs", EditGroupId);

        private static readonly WbTime CovTimestamp = new(2021, 9, 30, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar);

        public static async Task RunImport(WikiSite wikidataSite)
        {
            var problems = new List<string>();

            try
            {
                var importMapping = await File.ReadAllLinesAsync(@"y:\_mine\wikidata-imports\cov\wikidata-covid-mapping-full.csv");

                int counter = 2608;
                foreach (var line in importMapping.Skip(counter))
                {
                    var pieces = line.Split(';');
                    if (pieces.Length != 3) throw new FormatException();
                    var entityUri = pieces[0];
                    var oldId = pieces[1];
                    var newId = pieces[2];

                    var entityId = GetEntityIdFromUri(entityUri);
                    await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{importMapping.Length})");
                    var entity = new Entity(wikidataSite, entityId);
                    await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new[] { "cs" });

                    var covidClaim = entity.Claims["P4062"].Single(claim => claim.Rank != "deprecated");

                    var idFromClaim = (string)covidClaim.MainSnak.DataValue;

                    if (idFromClaim != oldId)
                    {
                        problems.Add(entityUri);
                        await Console.Error.WriteLineAsync($"Unexpected ID value in {entityId}: '{oldId}' expected '{idFromClaim}' found when wanted to set '{newId}'");
                        continue;
                    }

                    EntityEditEntry[] edits;
                    if (covidClaim.Qualifiers.Any(qual => qual.PropertyId == "P2699" || qual.PropertyId == "P1065"))
                    {
                        // claim with URL/archive URL qualifier → deprecate, add the current
                        covidClaim.Rank = "deprecated";
                        covidClaim.Qualifiers.Add(new Snak("P2241", "Q107356532", BuiltInDataTypes.WikibaseItem));

                        var newClaim = new Claim(new Snak("P4062", newId, BuiltInDataTypes.ExternalId));
                        newClaim.Qualifiers.Add(
                            new Snak("P585", CovTimestamp, BuiltInDataTypes.Time)
                        );
                        edits = new[]
                        {
                            new EntityEditEntry(nameof(Entity.Claims), covidClaim),
                            new EntityEditEntry(nameof(Entity.Claims), newClaim),
                        };
                    }
                    else
                    {
                        // just replace value and add qualifier
                        covidClaim.MainSnak.DataValue = newId;
                        covidClaim.Qualifiers.Add(
                            new Snak("P585", CovTimestamp, BuiltInDataTypes.Time)
                        );
                        edits = new[]
                        {
                            new EntityEditEntry(nameof(Entity.Claims), covidClaim),
                        };
                    }

                    await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                }
            }
            catch (Exception)
            {
                await Console.Error.WriteLineAsync("Unexpected error!");
                if (problems.Count > 0)
                {
                    await Console.Error.WriteLineAsync("Problems detected:");
                    foreach (var problem in problems) await Console.Error.WriteLineAsync(problem);
                }
                throw;
            }
        }

        public static async Task RunDeprecationImport(WikiSite wikidataSite)
        {
            var problems = new List<string>();

            try
            {
                var importMapping = await File.ReadAllLinesAsync(@"y:\_mine\wikidata-imports\cov\wikidata-covid-mapping-deprecated.csv");

                int counter = 0;
                foreach (var line in importMapping.Skip(counter))
                {
                    var pieces = line.Split(';');
                    if (pieces.Length < 3 || pieces.Length > 4) throw new FormatException();
                    var entityUri = pieces[0];
                    var oldId = pieces[1];
                    var archiveUrl = pieces.Length > 3 ? pieces[3] : null;

                    var entityId = GetEntityIdFromUri(entityUri);
                    await Console.Error.WriteLineAsync($"Reading {entityId} ({++counter}/{importMapping.Length})");
                    var entity = new Entity(wikidataSite, entityId);
                    await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new[] { "cs" });

                    var covidClaim = entity.Claims["P4062"].Single(claim => claim.Rank != "deprecated");

                    var idFromClaim = (string)covidClaim.MainSnak.DataValue;

                    if (idFromClaim != oldId)
                    {
                        problems.Add(entityUri);
                        await Console.Error.WriteLineAsync($"Unexpected ID value in {entityId}: '{oldId}' expected '{idFromClaim}' found");
                        continue;
                    }

                    // claim with URL/archive URL qualifier → deprecate, add the current
                    covidClaim.Rank = "deprecated";
                    covidClaim.Qualifiers.Add(new Snak("P2241", "Q21441764", BuiltInDataTypes.WikibaseItem));
                    if (archiveUrl != null)
                    {
                        covidClaim.References.Add(new ClaimReference(new Snak("P1065", archiveUrl, BuiltInDataTypes.Url)));
                    }
                    var edits = new[]
                    {
                        new EntityEditEntry(nameof(Entity.Claims), covidClaim),
                    };

                    await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                }
            }
            catch (Exception)
            {
                await Console.Error.WriteLineAsync("Unexpected error!");
                if (problems.Count > 0)
                {
                    await Console.Error.WriteLineAsync("Problems detected:");
                    foreach (var problem in problems) await Console.Error.WriteLineAsync(problem);
                }
                throw;
            }
        }
    }
}