using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class MapCsfdToImdb
    {
        private const int QueryBatchSize = 100;

        public static async Task Run(string inputFilename, string outputFilename)
        {
            Console.SetOut(new StreamWriter(outputFilename, false, Encoding.ASCII));

            foreach (var csfdGroup in (await File.ReadAllLinesAsync(inputFilename, Encoding.ASCII)).Batch(QueryBatchSize))
            {
                var csfdSet = "'" + String.Join("' '", csfdGroup) + "'";
                foreach (var row in GetEntities(await GetSparqlResults(@"
SELECT ?csfd ?imdb WHERE {
  VALUES ?csfd { " + csfdSet  + @" }.
  ?item wdt:P2529 ?csfd.
  ?item wdt:P345 ?imdb.
}"), new Dictionary<string, string> {{"csfd", "literal"}, {"imdb", "literal"}}))
                {
                    Console.WriteLine($"{row[0]};{row[1]}");
                }
            }
        }

        private static IEnumerable<TSource[]> Batch<TSource>(
            this IEnumerable<TSource> source, int size)
        {
            var bucket = new TSource[size];
            var count = 0;

            foreach (var item in source)
            {
                bucket[count++] = item;
                if (count == size)
                {
                    yield return bucket;
                    count = 0;
                }
            }

            if (count > 0) yield return bucket.Take(count).ToArray();
        }
    }
}