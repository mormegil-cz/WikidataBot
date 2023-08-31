using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole.Tasks;

using static WikidataTools;

public static class WikidataTreeBuilder
{
    private static readonly HashSet<string> EmptySet = new();

    public static async Task Run()
    {
        // var queue = await FilterItemsToClasses();
        var queue = new HashSet<string>(await File.ReadAllLinesAsync(@"/home/petr/Downloads/5000-most-used-wikidata-classes-list.txt"));

        var batch = 0;
        var processedEntities = new HashSet<string>();
        var entityLabels = new Dictionary<string, string>();
        var superclasses = new Dictionary<string, HashSet<string>>();
        while (queue.Count > 0)
        {
            ++batch;

            var itemsToProcess = queue.Take(100).ToList();
            queue.ExceptWith(itemsToProcess);
            processedEntities.UnionWith(itemsToProcess);

            await Console.Error.WriteAsync($"Batch #{batch} Retrieving {itemsToProcess.Count} classes from WQS ({queue.Count} remaining)...");

            var entities = GetEntities(await GetSparqlResults(@"
SELECT ?item ?itemLabel ?parents WHERE {
  {
    SELECT ?item (GROUP_CONCAT(?parent; SEPARATOR = "" "") AS ?parents) WHERE {
      hint:Query hint:optimizer ""None"".
      VALUES ?item { " + String.Join(' ', itemsToProcess.Select(item => "wd:" + item)) + @" }
      {
          { ?item wdt:P279 ?parent }
          UNION
          { }
      }
    }
    GROUP BY ?item
  }
  SERVICE wikibase:label { bd:serviceParam wikibase:language ""en"". }
}"), new Dictionary<string, string> { { "item", "uri" }, { "itemLabel", "literal" }, { "parents", "literal" } }).ToList();

            Console.WriteLine("OK");
            foreach (var row in entities)
            {
                var entityId = GetEntityIdFromUri(row[0]!);
                var itemLabel = row[1]!;
                var superclassIds = String.IsNullOrEmpty(row[2]) ? EmptySet : row[2]!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(GetEntityIdFromUri).ToHashSet();
                superclasses.Add(entityId, new HashSet<string>(superclassIds));
                entityLabels.Add(entityId, itemLabel);

                superclassIds.ExceptWith(processedEntities);
                queue.UnionWith(superclassIds);
            }
        }
        await Console.Error.WriteLineAsync("Done!");
        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync();

        var fileStreamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous,
            Share = FileShare.None
        };
        await using var outputFile = new StreamWriter(@"/home/petr/Downloads/5000-most-used-wikidata-classes.json", Encoding.UTF8, fileStreamOptions);
        await outputFile.WriteLineAsync("{");
        var first = true;
        foreach (var entry in superclasses)
        {
            if (!first) await outputFile.WriteLineAsync(",");
            first = false;
            if (!entityLabels.TryGetValue(entry.Key, out var label))
            {
                Console.WriteLine($"Warning: Item {entry.Key} does not have a label");
                label = entry.Key;
            }
            await outputFile.WriteLineAsync($"\t\"{entry.Key}\": {{");
            await outputFile.WriteLineAsync($"\t\t\"l\": \"{JsonEscape(label)}\",");
            await outputFile.WriteLineAsync($"\t\t\"s\": [\"{String.Join("\", \"", entry.Value)}\"]");
            await outputFile.WriteAsync("\t}");
        }
        await outputFile.WriteLineAsync();
        await outputFile.WriteLineAsync("}");
    }

    private static async Task<HashSet<string>> FilterItemsToClasses()
    {
        var batch = 0;
        var mostLinkedItems = new HashSet<string>(await File.ReadAllLinesAsync(@"/home/petr/Downloads/5000-most-used-wikidata-items-list.txt"));

        var queue = new HashSet<string>(mostLinkedItems.Count);
        while (mostLinkedItems.Count > 0)
        {
            ++batch;

            var itemsToProcess = mostLinkedItems.Take(17).ToList();
            mostLinkedItems.ExceptWith(itemsToProcess);

            await Console.Error.WriteAsync($"Batch #{batch} Checking {itemsToProcess.Count} items using WQS ({mostLinkedItems.Count} remaining)...");

            var sparql = new StringBuilder();
            sparql.Append(@"
SELECT ?item WHERE {
  {
");
            var sparqlFirst = true;
            foreach (var item in itemsToProcess)
            {
                if (!sparqlFirst) sparql.AppendLine("    UNION");
                sparqlFirst = false;
                sparql.Append(@"
    {
      SELECT ?item WHERE {
        VALUES ?item {
          wd:" + item + @"
        }
        ?inst wdt:P31 ?item.
      }
      LIMIT 1
    }
");
            }
            sparql.Append(@"
  }
}
");

            List<IList<string?>> entities;
            try
            {
                entities = GetEntities(await GetSparqlResults(sparql.ToString()), new Dictionary<string, string> { { "item", "uri" } }).ToList();
            }
            catch (HttpRequestException)
            {
                await Console.Error.WriteLineAsync($"Error in SPARQL query ({sparql})");
                throw;
            }

            Console.WriteLine("OK");
            foreach (var row in entities)
            {
                var entityId = GetEntityIdFromUri(row[0]!);
                queue.Add(entityId);
            }
        }

        await File.WriteAllLinesAsync(@"/home/petr/Downloads/5000-most-used-wikidata-classes-list.txt", queue);
        return queue;
    }

    // TODO: Proper JSON escape
    private static string JsonEscape(string entityLabel) => entityLabel.Replace("\"", "\\\"");
}