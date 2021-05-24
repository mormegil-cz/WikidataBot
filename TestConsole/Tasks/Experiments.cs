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
    public static class Experiments
    {
        private static readonly Uri GregorianCalendarUri = new Uri("http://www.wikidata.org/entity/Q1985727");

        public static async Task Run(WikiSite wikidataSite)
        {
            var entity = new Entity(wikidataSite, "Q4115189");
            var claimDsid = new Claim(new Snak("P569", new WbTime(1850, 0, 0, 0, 0, 0, 4, 5, 0, WikibaseTimePrecision.Year, GregorianCalendarUri), BuiltInDataTypes.Time));
            var edits = new[] {new EntityEditEntry(nameof(Entity.Claims), claimDsid)};
            await entity.EditAsync(edits, "Time data model test", EntityEditOptions.Bot);
        }
    }
}