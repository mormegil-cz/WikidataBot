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
        public static async Task Run(WikiSite wikidataSite)
        {
            var entity = new Entity(wikidataSite, "Q4115189");
            var claim = new Claim(new Snak("P1106", new WbQuantity(10.0, 9.0, 12.0, WikidataProperties.UnitCentimetre), BuiltInDataTypes.Quantity));
            var edits = new[] {new EntityEditEntry(nameof(Entity.Claims), claim)};
            await entity.EditAsync(edits, "Quantity data model test", EntityEditOptions.Bot);
            Console.WriteLine("Done");
        }
    }
}