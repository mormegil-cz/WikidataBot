using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Tasks;

using static WikidataTools;

public class LinkMunicipalityNameToLexeme
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Linking Czech municipalities to the corresponding lexeme senses", EditGroupId);
    private static readonly string[] Languages = { };

    public static async Task Run(WikiSite wikidataSite)
    {
        await Console.Error.WriteAsync($"Retrieving data from WQS...");
        var entities = GetEntities(await GetSparqlResults(@"
select distinct ?obec ?stmt ?jmeno ?sense

with {
  select distinct ?obec ?jmeno ?sense where {
    ?lexeme dct:language wd:Q9056;
            ontolex:sense ?sense.
    ?sense wdt:P5137 ?obec.
    {
      {?obec wdt:P31 wd:Q5153359}
      union
      {?obec wdt:P31 wd:Q61089180}
    }
    ?lexeme wikibase:lemma ?jmeno.
  }
} as %obec

where {
  include %obec
  ?obec p:P1448 ?stmt.
  ?stmt ps:P1448 ?of_nazev.
  filter(str(?of_nazev)=str(?jmeno))
}
"), new Dictionary<string, string> { { "obec", "uri" }, { "stmt", "uri" }, { "jmeno", "literal" }, { "sense", "uri" } }).ToList();

        var counter = 0;
        var count = entities.Count;
        await Console.Error.WriteLineAsync($" processing {count} entities...");
        foreach (var row in entities)
        {
            ++counter;
            var entityId = GetEntityIdFromUri(row[0]);
            var stmtId = GetStatementIdFromUri(row[1]);
            var name = row[2];
            var senseUri = GetEntityIdFromUri(row[3]);
            // await Console.Error.WriteLineAsync($"Reading {entityId} ({counter}/{count})");
            var entity = new Entity(wikidataSite, entityId);
            await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

            if (entity.Claims == null)
            {
                await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                continue;
            }

            if (!entity.Claims.ContainsKey(WikidataProperties.OfficialName))
            {
                // ??!?
                await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain official name?!");
                continue;
            }

            var claims = entity.Claims[WikidataProperties.OfficialName];
            var editedClaim = claims.SingleOrDefault(c => c.Id == stmtId);
            if (editedClaim == null)
            {
                // ??!?
                await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} does not contain claim {stmtId}");
                continue;
            }

            if (((WbMonolingualText)editedClaim.MainSnak.DataValue).Text != name)
            {
                // ??!?
                await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} has a different name");
                continue;
            }
            if (editedClaim.Qualifiers.Any(q => q.PropertyId == WikidataProperties.LexemeSense))
            {
                await Console.Error.WriteLineAsync($"WARNING! Entity {entityId} already contains lexeme qualifier at {stmtId}");
                continue;
            }

            editedClaim.Qualifiers.Add(new Snak(WikidataProperties.LexemeSense, senseUri, LexemeBuiltInDataTypes.WikibaseSense));
            var edits = new List<EntityEditEntry>
            {
                new(nameof(Entity.Claims), editedClaim)
            };

            await Console.Error.WriteLineAsync($"Editing {entityId} ({counter}/{count})");
            await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}