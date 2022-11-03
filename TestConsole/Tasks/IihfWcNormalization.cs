using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public class IihfWcNormalization
    {
        public static async Task Run(WikiSite wikidataSite)
        {
            var queryBatch = 0;
            try
            {
                Console.WriteLine("Executing query...");
                var interestingItems = GetEntities(await GetSparqlResults(@"
SELECT ?item WHERE {
  SERVICE wikibase:mwapi {
      bd:serviceParam wikibase:endpoint 'www.wikidata.org';
                      wikibase:api 'Search';
                      mwapi:srsearch '""IIHF World Championship"" OR ""Men\'s World Ice Hockey Championships""'.
      ?title wikibase:apiOutput mwapi:title.
  }
  BIND (IRI(CONCAT('http://www.wikidata.org/entity/', ?title)) AS ?item)
  FILTER (?item != wd:Q190163).
}"), new Dictionary<string, string> { { "item", "uri" } }).Select(row => row[0]).ToList();

                Console.WriteLine($"{interestingItems.Count} interesting items");
                foreach (var itemUri in interestingItems)
                {
                    var qid = GetEntityIdFromUri(itemUri);
                    var entity = new Entity(wikidataSite, qid);
                    await entity.RefreshAsync(EntityQueryOptions.FetchClaims | EntityQueryOptions.FetchLabels, new[] { "cs", "en" });

                    if (entity.Claims == null)
                    {
                        await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {qid}!");
                        continue;
                    }

                    var classes = entity.Claims.ContainsKey("P31")
                        ? entity.Claims["P31"].Select(claim => (string)claim.MainSnak.DataValue)
                            // ignore “cancelled sports event due to the 2019–21 coronavirus pandemic”
                            .Where(c => c != "http://www.wikidata.org/entity/Q88903067")
                            .ToHashSet()
                        : new HashSet<string>();
                    var superclasses = entity.Claims.ContainsKey("P279") ? entity.Claims["P279"].Select(claim => (string)claim.MainSnak.DataValue).ToHashSet() : new HashSet<string>();
                    var partOfs = entity.Claims.ContainsKey("P361") ? entity.Claims["P361"].Select(claim => (string)claim.MainSnak.DataValue).ToHashSet() : new HashSet<string>();
                    var seasonOfs = entity.Claims.ContainsKey("P3450") ? entity.Claims["P3450"].Select(claim => (string)claim.MainSnak.DataValue).ToHashSet() : new HashSet<string>();

                    if (classes.Count != 1)
                    {
                        await Console.Error.WriteLineAsync($"PROBLEM: {qid} has {classes.Count} classes");
                        continue;
                    }
                    var entityClass = classes.Single();
                    if (superclasses.Count > 1)
                    {
                        await Console.Error.WriteLineAsync($"PROBLEM: {qid} has {superclasses.Count} superclasses");
                        continue;
                    }
                    var superclass = classes.SingleOrDefault();
                    if (partOfs.Count > 1)
                    {
                        await Console.Error.WriteLineAsync($"PROBLEM: {qid} has {partOfs.Count} part-ofs");
                        continue;
                    }
                    var partOf = partOfs.SingleOrDefault();
                    if (seasonOfs.Count > 1)
                    {
                        await Console.Error.WriteLineAsync($"PROBLEM: {qid} has {seasonOfs.Count} seasons");
                        continue;
                    }
                    var seasonOf = seasonOfs.SingleOrDefault();

                    
                }
            }
            catch (Exception)
            {
//                Console.WriteLine("Aborted with error at batch {4}; {0} entries imported, {1} missing, {2} already up-to-date, {3} different", ImportedEntries, MissingEntries, UpToDateEntries, DifferentEntries, queryBatch);
                throw;
            }

            //Console.WriteLine("{0} entries imported, {1} missing, {2} already up-to-date, {3} different", ImportedEntries, MissingEntries, UpToDateEntries, DifferentEntries);
        }
    }
}