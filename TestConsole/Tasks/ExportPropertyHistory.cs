using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Pages;
using WikiClientLibrary.Sites;

namespace TestConsole.Tasks
{
    public static class ExportPropertyHistory
    {
        private static readonly Regex reParser = new Regex(@"\|(?<prop>[1-9][0-9]*)=(?<count>[1-9][0-9]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        private const string BasePath = @"y:\property-history";

        public static async Task Run(WikiSite wikidataSite)
        {
            var wikiPage = new WikiPage(wikidataSite, "Template:Property_uses");
            var revisions = wikiPage.CreateRevisionsGenerator();
            revisions.PaginationSize = 50;
            revisions.StartTime = new DateTime(2020, 5, 14, 8, 0, 0, DateTimeKind.Utc);
            revisions.PropertyProvider.FetchContent = true;

            var lastYear = 9999;
            var lastMonth = 99;
            var lastDay = 99;
            var yearIndex = new Dictionary<int, SortedSet<int>>(20);
            var counter = 0;
            await foreach (var revision in revisions.EnumItemsAsync())
            {
                ++counter;
                var timestamp = revision.TimeStamp;
                if (timestamp.Year != lastYear)
                {
                    if (yearIndex.Count != 0) CreateIndexFile(lastYear, yearIndex);
                    yearIndex.Clear();
                }
                else if (timestamp.Month == lastMonth && timestamp.Day == lastDay)
                {
                    // multiple revisions per day, ignore
                    continue;
                }

                lastYear = timestamp.Year;
                lastMonth = timestamp.Month;
                lastDay = timestamp.Day;

                if (!yearIndex.TryGetValue(timestamp.Month, out var monthIndex))
                {
                    monthIndex = new SortedSet<int>();
                    yearIndex.Add(timestamp.Month, monthIndex);
                }
                monthIndex.Add(lastDay);

                Console.WriteLine($"Processing revision #{counter} from {lastYear}-{lastMonth}-{lastDay}");
                
                var path = Path.Combine(BasePath, lastYear.ToString(), lastMonth.ToString(), lastDay.ToString());
                Directory.CreateDirectory(path);

                // await File.WriteAllTextAsync(Path.Combine(path, "_fulldata.wiki"), revision.Content, Encoding.UTF8);

                var matches = reParser.Matches(revision.Content);
                foreach (Match match in matches)
                {
                    var prop = Int32.Parse(match.Groups["prop"].Value, CultureInfo.InvariantCulture);
                    var count = Int32.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);

                    await File.WriteAllTextAsync(Path.Combine(path, prop + ".json"), count.ToString(CultureInfo.InvariantCulture), Encoding.ASCII);
                }
            }

            if (yearIndex.Count != 0) CreateIndexFile(lastYear, yearIndex);
        }

        private static void CreateIndexFile(int year, Dictionary<int, SortedSet<int>> yearIndex)
        {
            var path = Path.Combine(BasePath, year.ToString());
            Directory.CreateDirectory(path);
            using var writer = new StreamWriter(Path.Combine(path, "_index.json"), false, Encoding.UTF8);
            JsonSerializer.Create().Serialize(writer, yearIndex);
        }
    }
}