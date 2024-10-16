using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class FixReferenceAccessDate
    {
        public static async Task Run(WikiSite wikidataSite)
        {
            var entitiesToFix = GetEntities(await GetSparqlResults(@"SELECT ?item WHERE {
  ?item (p:P691/prov:wasDerivedFrom/pr:P585) ?datum.
  FILTER((DAY(?datum)=15) && (MONTH(?datum)=4) && (YEAR(?datum)=2020))
}"), new Dictionary<string, string> {{"item", "uri"}}).ToList();
            var count = entitiesToFix.Count;
            var curr = 0;
            foreach (var row in entitiesToFix)
            {
                ++curr;
                var entityId = GetEntityIdFromUri(row[0]);
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});

                var edits = new List<EntityEditEntry>(entity.Claims["P691"].Count);
                foreach (var p691 in entity.Claims["P691"])
                {
                    foreach (var reference in p691.References.Where(r => r.Snaks.Any(snak => snak.PropertyId == "P585")))
                    {
                        var snak = GetOnly(reference.Snaks.Where(s => s.PropertyId == "P585").ToList());
                        if (snak == null)
                        {
                            Console.Error.WriteLine("No single reference snak for {0}", entityId);
                            continue;
                        }
                        var snakIdx = reference.Snaks.IndexOf(snak);
                        var fixedSnak = new Snak("P813", snak.RawDataValue, snak.DataType);
                        reference.Snaks[snakIdx] = fixedSnak;
                        Console.Error.WriteLine("- Fixing ref {1} at {0}", entityId, reference.Hash, curr, count);
                    }
                    edits.Add(new EntityEditEntry(nameof(Entity.Claims), p691));
                }

                Console.Error.WriteLine("{1}/{2}: Editing {0}", entityId, curr, count);
                await entity.EditAsync(edits, "Fixing reference per [[Wikidata:Mezi bajty#Prosba o opravu referencí botem]]", EntityEditOptions.Bot);
            }
        }

        private static T? GetOnly<T>(ICollection<T> sequence)
            where T : class
        {
            return sequence.Count == 1 ? sequence.Single() : null;
        }
    }
}