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
            foreach (var row in GetEntities(await GetSparqlResults(@"SELECT ?item WHERE {
  ?item (p:P691/prov:wasDerivedFrom/pr:P585) ?datum.
  FILTER((DAY(?datum)=15) && (MONTH(?datum)=4) && (YEAR(?datum)=2020))
}"), new Dictionary<string, string> {{"item", "uri"}}))
            {
                var entityId = GetEntityIdFromUri(row[0]);
                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, new string[] {"cs", "en"});

                var p691 = entity.Claims["P691"].SingleOrDefault();
                if (p691 == null)
                {
                    Console.Error.WriteLine("No single P691 for {0}", entityId);
                    continue;
                }
                foreach (var reference in p691.References.Where(r => r.Snaks.Any(snak => snak.PropertyId == "P585")))
                {
                    var snak = reference.Snaks.SingleOrDefault(s => s.PropertyId == "P585");
                    if (snak == null)
                    {
                        Console.Error.WriteLine("No single reference snak for {0}", entityId);
                        continue;
                    }
                    var snakIdx = reference.Snaks.IndexOf(snak);
                    var fixedSnak = new Snak("P813", snak.RawDataValue, snak.DataType);
                    reference.Snaks[snakIdx] = fixedSnak;
                    Console.Error.WriteLine("Fixing ref {1} at {0}", entityId, reference.Hash);
                }

                var edits = new List<EntityEditEntry>
                {
                    new EntityEditEntry(nameof(Entity.Claims), p691),
                };
                await entity.EditAsync(edits, "Fixing reference per [[Wikidata:Mezi bajty#Prosba o opravu referencí botem]]", EntityEditOptions.Bot);
            }
        }
    }
}