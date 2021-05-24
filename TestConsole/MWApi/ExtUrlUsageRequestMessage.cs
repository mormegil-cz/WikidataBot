using System.Collections.Generic;
using WikiClientLibrary.Client;

namespace TestConsole.MWApi
{
    public class ExtUrlUsageRequestMessage : MediaWikiFormRequestMessage
    {
        public ExtUrlUsageRequestMessage(string protocol, string query, int limit) : base(
            new Dictionary<string, object>
            {
                // https://www.wikidata.org/w/api.php?action=query&format=json&list=exturlusage&euprop=title&euprotocol=https&euquery=pamatkovykatalog.cz%2F%3Fmode%3Dparametric%26catalogNumber%3D&eunamespace=0&eulimit=10
                { "format", "json" },
                { "action", "query" },
                { "list", "exturlusage" },
                { "euprop", "title" },
                { "euprotocol", protocol },
                { "euquery", query },
                { "eunamespace", 0 },
                { "eulimit", limit },
            }
        )
        {
        }
    }
}