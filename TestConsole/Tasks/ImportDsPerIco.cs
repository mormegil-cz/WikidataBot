using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks
{
    public static class ImportDsPerIco
    {
        private const int QueryBatchSize = 100;

        private const int QuerySkip = 0;

        internal const bool ImportingPo = true;
        internal static readonly DateOnly ImportDate = new(2024, 9, 23);

        private static readonly string BasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        private static readonly string ImportFileName = Path.Combine(BasePath, ImportingPo
            ? $"seznam_ds_po-{ImportDate.ToString("yyyy-MM-dd")}.xml.gz"
            : $"seznam_ds_ovm-{ImportDate.ToString("yyyy-MM-dd")}.xml.gz"
        );

        private static int MissingEntries = 0;
        private static int UpToDateEntries = 0;
        private static int DifferentEntries = 0;
        private static int ImportedEntries = 0;
        private static HashSet<string> RemainingMissingIcos = [];

        public static async Task Run(WikiSite wikidataSite)
        {
            var queryBatch = 0;
            try
            {
                Console.WriteLine("Executing query...");
                var missingIcos = GetEntities(await GetSparqlResults(@"
SELECT ?ico WHERE {
  ?item wdt:P4156 ?ico.
  MINUS { ?item wdt:P8987 ?ds }
}
"), new Dictionary<string, string> { { "ico", "literal" } }).Select(row => row[0]!).ToHashSet();

                RemainingMissingIcos = [..missingIcos];
                Console.WriteLine($"{missingIcos.Count} IČOs without DS ID");
                // using (var importer = new QuickStatementExport())
                using (var importer = new BotEditingImport(wikidataSite))
                {
                    await foreach (var batch in LoadDsData(ImportFileName).Where(row => missingIcos.Contains(row.Key)).Batch(QueryBatchSize))
                    {
                        while (Console.KeyAvailable)
                        {
                            if (Console.ReadKey().Key == ConsoleKey.Escape)
                            {
                                Console.WriteLine($"Aborting at {queryBatch}");
                                break;
                            }
                        }

                        ++queryBatch;
                        if (queryBatch <= QuerySkip)
                        {
                            if (queryBatch == 1)
                            {
                                Console.Write($"Skipping {QuerySkip} batches...");
                            }
                            if (queryBatch % 100 == 0)
                            {
                                Console.Write($"{queryBatch}...");
                            }
                            if (queryBatch == QuerySkip)
                            {
                                Console.WriteLine();
                            }
                            continue;
                        }

                        Console.WriteLine($"*** Processing batch #{queryBatch}...");
                        await importer.StartQueryBatchProcessing(queryBatch);
                        await ImportBatch(batch, importer);
                    }
                }

                foreach (var remainingIco in RemainingMissingIcos)
                {
                    Console.WriteLine($"IČO {remainingIco} not found in DS data");
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Aborted with error at batch {4}; {0} entries imported, {1} missing, {2} already up-to-date, {3} different, {5} remaining unprocessed", ImportedEntries, MissingEntries, UpToDateEntries, DifferentEntries, queryBatch, RemainingMissingIcos.Count);
                throw;
            }

            Console.WriteLine("{0} entries imported, {1} missing, {2} already up-to-date, {3} different, {4} unknown", ImportedEntries, MissingEntries, UpToDateEntries, DifferentEntries, RemainingMissingIcos.Count);
        }

        private static async Task ImportBatch(KeyValuePair<string, string>[] entriesToImport, IImporter importer)
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

            var queryResults = GetEntities(await GetSparqlResults(sparql.ToString()), new Dictionary<string, string> { { "item", "uri" }, { "dsid", "literal" }, { "ico", "literal" } }).ToList();
            var dataAtWd = queryResults.ToDictionaryLax(queryItem => queryItem[2]!, queryItem => new { item = queryItem[0]!, dsid = queryItem[1]! });
            foreach (var entryToImport in entriesToImport)
            {
                RemainingMissingIcos.Remove(entryToImport.Key);

                if (dataAtWd.TryGetValue(entryToImport.Key, out var entryAtWd))
                {
                    if (String.IsNullOrEmpty(entryAtWd.dsid))
                    {
                        await Console.Error.WriteLineAsync($"Importing {entryToImport.Value} for {entryToImport.Key} at {entryAtWd.item}");
                        await importer.ImportEntry(entryToImport.Value, entryAtWd.item);
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
                    await Console.Error.WriteLineAsync($"ID {entryToImport.Key} not found at WD");
                    ++MissingEntries;
                }
            }
        }

        private static async IAsyncEnumerable<KeyValuePair<string, string>> LoadDsData(string filename)
        {
            await using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            var firstByte = stream.ReadByte();
            stream.Seek(0, SeekOrigin.Begin);
            XmlReader reader = null;
            try
            {
                switch (firstByte)
                {
                    case 0x3C:
                        // plain XML
                        reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
                        break;
                    case 0x1F:
                        // GZ-compressed XML
                        reader = XmlReader.Create(new GZipStream(stream, CompressionMode.Decompress), new XmlReaderSettings { Async = true });
                        break;
                    default:
                        throw new FormatException("Unsupported XML format");
                }
                await reader.MoveToContentAsync();
                reader.ReadStartElement("list");
                while (await reader.MoveToContentAsync() == XmlNodeType.Element)
                {
                    reader.ReadStartElement("box");
                    string? ico = null;
                    string? dsid = null;
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
                        yield return new KeyValuePair<string, string>(ico, dsid);
                    }
                }
                reader.ReadEndElement();
            }
            finally
            {
                reader?.Dispose();
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

        private static async IAsyncEnumerable<TSource[]> Batch<TSource>(
            this IAsyncEnumerable<TSource> source, int size)
        {
            var bucket = new TSource[size];
            var count = 0;

            await foreach (var item in source)
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
        ) where TKey : notnull
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

    internal interface IImporter : IDisposable
    {
        Task StartQueryBatchProcessing(int queryBatchNumber);
        Task ImportEntry(string dsid, string item);
    }

    internal class QuickStatementExport : IImporter
    {
        private const int ImportBatchSize = 10; // number of query batches in a single import file

        private int importBatch;
        private int batchStart;
        TextWriter? outputWriter;

        public Task StartQueryBatchProcessing(int queryBatchNumber)
        {
            if (outputWriter == null || batchStart < queryBatchNumber - ImportBatchSize)
            {
                outputWriter?.Close();
                outputWriter = new StreamWriter($"qs-import-datafile-batch-{importBatch:000}.tsv", false, Encoding.UTF8);
                batchStart = queryBatchNumber;
                ++importBatch;
            }
            return Task.CompletedTask;
        }

        public async Task ImportEntry(string dsid, string item)
        {
            var qid = GetEntityIdFromUri(item);
            await (outputWriter ?? throw new InvalidOperationException("Not started!")).WriteLineAsync($"{qid}\tP8987\t\"{dsid}\"\tS1476\tcs:\"Seznam datových schránek : Orgány veřejné moci\"\tS123\tQ11781499\tS2701\tQ2115\tS854\t\"https://www.mojedatovaschranka.cz/sds/datafile?format=xml&service=seznam_ds_ovm\"\tS813\t+2022-08-22T00:00:00Z/11");
        }

        public void Dispose()
        {
            outputWriter?.Dispose();
        }
    }

    internal class BotEditingImport : IImporter
    {
        private static readonly string EditGroupId = GenerateRandomEditGroupId();
        private static readonly string EditSummary = MakeEditSummary($"DS ID import for {(ImportDsPerIco.ImportingPo ? "companies" : "government entities")} based on IČO", EditGroupId);

        private readonly WikiSite wikidataSite;

        public BotEditingImport(WikiSite wikidataSite)
        {
            this.wikidataSite = wikidataSite;
        }

        public Task StartQueryBatchProcessing(int queryBatchNumber)
        {
            return Task.CompletedTask;
        }

        public async Task ImportEntry(string dsid, string item)
        {
            var qid = GetEntityIdFromUri(item);
            var entity = new Entity(wikidataSite, qid);
            var claimDsid = new Claim(new Snak("P8987", dsid, BuiltInDataTypes.ExternalId));
            claimDsid.References.Add(new ClaimReference(
                ImportDsPerIco.ImportingPo
                    ? new Snak("P1476", new WbMonolingualText("cs", "Seznam datových schránek : Právnické osoby"), BuiltInDataTypes.MonolingualText)
                    : new Snak("P1476", new WbMonolingualText("cs", "Seznam datových schránek : Orgány veřejné moci"), BuiltInDataTypes.MonolingualText),
                new Snak("P123", "Q11781499", BuiltInDataTypes.WikibaseItem),
                new Snak("P2701", "Q2115", BuiltInDataTypes.WikibaseItem),
                ImportDsPerIco.ImportingPo
                    ? new Snak("P854", "https://www.mojedatovaschranka.cz/sds/datafile?format=xml&service=seznam_ds_po", BuiltInDataTypes.Url)
                    : new Snak("P854", "https://www.mojedatovaschranka.cz/sds/datafile?format=xml&service=seznam_ds_ovm", BuiltInDataTypes.Url),
                new Snak("P813", new WbTime(ImportDsPerIco.ImportDate.Year, ImportDsPerIco.ImportDate.Month, ImportDsPerIco.ImportDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time)
            ));
            var edits = new[] { new EntityEditEntry(nameof(Entity.Claims), claimDsid) };
            await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
        }

        public void Dispose()
        {
        }
    }
}