using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class ImportDsPerIco
    {
        private const int QueryBatchSize = 100;
        private const int ImportBatchSize = 10; // number of query batches in a single import file

        private static int MissingEntries = 0;
        private static int UpToDateEntries = 0;
        private static int DifferentEntries = 0;
        private static int ImportedEntries = 0;

        public static async Task Run()
        {
            var dsData = await LoadDsData(@"c:\Users\petrk\Downloads\datafile-seznam_ds_ovm-20210111092054.xml");
            var count = (dsData.Count + QueryBatchSize - 1) / QueryBatchSize;

            var queryBatch = 0;
            var importBatch = 1;
            var batchStart = 0;
            TextWriter outputWriter = null;
            foreach (var batch in dsData.Batch(QueryBatchSize))
            {
                ++queryBatch;
                if (outputWriter == null || batchStart < queryBatch - ImportBatchSize)
                {
                    outputWriter?.Close();
                    outputWriter = new StreamWriter($"qs-import-datafile-seznam_ds_ovm-20210111092054-batch-{importBatch:000}.tsv", false, Encoding.UTF8);
                    batchStart = queryBatch;
                    ++importBatch;
                }
                Console.WriteLine($"*** Processing batch {queryBatch}/{count}...");
                await ImportBatch(batch, outputWriter);
            }
            outputWriter?.Close();

            Console.WriteLine("{0} entries imported, {1} missing, {2} already up-to-date, {3} different", ImportedEntries, MissingEntries, UpToDateEntries, DifferentEntries);
        }

        private static async Task ImportBatch(KeyValuePair<string, string>[] entriesToImport, TextWriter writer)
        {
            var sparql = new StringBuilder();
            sparql.Append("SELECT ?item ?ico ?dsid WHERE { ?item wdt:P4156 ?ico. FILTER (?ico IN (");
            var first = true;
            foreach (var entry in entriesToImport)
            {
                if (!first) sparql.Append(',');
                sparql.Append('\'');
                sparql.Append(entry.Key);
                sparql.Append('\'');
                first = false;
            }
            sparql.Append(")) OPTIONAL { ?item wdt:P8987 ?dsid } }");

            var queryResults = GetEntities(await GetSparqlResults(sparql.ToString()), new Dictionary<string, string> {{"item", "uri"}, {"dsid", "literal"}, {"ico", "literal"}}).ToList();
            var dataAtWd = queryResults.ToDictionaryLax(queryItem => queryItem[2], queryItem => new {item = queryItem[0], dsid = queryItem[1]});
            foreach (var entryToImport in entriesToImport)
            {
                if (dataAtWd.TryGetValue(entryToImport.Key, out var entryAtWd))
                {
                    if (String.IsNullOrEmpty(entryAtWd.dsid))
                    {
                        await Console.Error.WriteLineAsync($"Importing {entryToImport.Value} for {entryToImport.Key} at {entryAtWd.item}");
                        await ImportEntry(entryToImport.Value, entryAtWd.item, writer);
                        ++ImportedEntries;
                    }
                    else
                    {
                        if (entryAtWd.dsid == entryToImport.Value)
                        {
                            await Console.Error.WriteLineAsync($"Correct DSID already for {entryToImport.Key} at {entryAtWd.item}");
                            ++UpToDateEntries;
                        }
                        else
                        {
                            await Console.Error.WriteLineAsync($"Mismatch of {entryToImport.Value}/{entryAtWd.dsid} for {entryToImport.Key} at {entryAtWd.item}");
                            ++DifferentEntries;
                        }
                    }
                }
                else
                {
                    await Console.Error.WriteLineAsync($"IČO {entryToImport.Key} not found at WD");
                    ++MissingEntries;
                }
            }
        }

        private static async Task ImportEntry(string dsid, string item, TextWriter writer)
        {
            var qid = GetEntityIdFromUri(item);

            await writer.WriteLineAsync($"{qid}\tP8987\t\"{dsid}\"\tS1476\tcs:\"Seznam datových schránek : Orgány veřejné moci\"\tS123\tQ11781499\tS2701\tQ2115\tS854\t\"https://www.mojedatovaschranka.cz/sds/datafile?format=xml&service=seznam_ds_ovm\"\tS813\t+2021-01-11T00:00:00Z/11");
        }

        private static async Task<Dictionary<string, string>> LoadDsData(string filename)
        {
            var result = new Dictionary<string, string>();

            using var reader = XmlReader.Create(filename, new XmlReaderSettings {Async = true});
            await reader.MoveToContentAsync();
            reader.ReadStartElement("list");
            while (await reader.MoveToContentAsync() == XmlNodeType.Element)
            {
                reader.ReadStartElement("box");
                string ico = null;
                string dsid = null;
                var isMaster = true;
                while (await reader.MoveToContentAsync() == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "id":
                            dsid = await reader.ReadElementContentAsStringAsync();
                            break;
                        case "ico":
                            ico = await reader.ReadElementContentAsStringAsync();
                            break;
                        case "hierarchy":
                            reader.ReadStartElement();
                            while (await reader.MoveToContentAsync() == XmlNodeType.Element)
                            {
                                switch (reader.Name)
                                {
                                    case "isMaster":
                                        isMaster = reader.ReadElementContentAsBoolean();
                                        break;
                                    default:
                                        await reader.SkipAsync();
                                        break;
                                }
                            }
                            reader.ReadEndElement();
                            break;
                        default:
                            await reader.SkipAsync();
                            break;
                    }
                }
                reader.ReadEndElement();
                if (ico == null || dsid == null) throw new FormatException("Missing ico or id");
                if (isMaster && ico != "")
                {
                    if (result.ContainsKey(ico)) throw new FormatException($"Duplicate box for {ico}");
                    result.Add(ico, dsid);
                }
            }
            reader.ReadEndElement();
            return result;
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

        private static IDictionary<TKey, TValue> ToDictionaryLax<TSource, TKey, TValue>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TValue> valueSelector
        )
        {
            var result = new Dictionary<TKey, TValue>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (result.ContainsKey(key))
                {
                    Console.Error.WriteLine($"ERROR: Duplicate entry {key}");
                    continue;
                }
                result.Add(key, valueSelector(item));
            }
            return result;
        }
    }
}