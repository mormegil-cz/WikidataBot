using System;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Pages;
using WikiClientLibrary.Sites;

namespace TestConsole.Tasks;

public static class WikidataItemCreationStatistics
{
    public static async Task Run(WikiSite wikidataSite)
    {
        for (var qid = 115600000L; qid <= 131341016L; qid += 100000)
        {
            var page = new WikiPage(wikidataSite, "Q" + qid);
            var revGenerator = page.CreateRevisionsGenerator();
            revGenerator.TimeAscending = true;
            revGenerator.PaginationSize = 1;
            var rev = await revGenerator.EnumItemsAsync().FirstOrDefaultAsync();
            if (rev == null)
            {
                await Console.Error.WriteLineAsync($"Warning: Unable to retrieve revision for Q{qid}");
                continue;
            }
            Console.WriteLine($"{rev.TimeStamp:s}Z\t{qid}");
        }
    }
}