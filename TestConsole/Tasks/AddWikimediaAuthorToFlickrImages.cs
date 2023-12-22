using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Tasks;

using static WikidataTools;

public class AddWikimediaAuthorToFlickrImages
{
    private const string queryUserName = "www.flickr.com/photos/...";
    private const string queryUserNumber = "www.flickr.com/people/...";
    private const string authorName = "...";
    private const string authorWikimediaUsername = "...";
    private const string authorFlickrId = "...@N00";
    private const string authorUrl = "https://www.flickr.com/people/...@N00";

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Add author structured data", EditGroupId);
    private static readonly string[] Languages = { };

    public static async Task Run(WikiSite wikidataSite, string wcqsOAuth2Cookie)
    {
        await Console.Out.WriteLineAsync("Finding files...");

        var entities = GetEntities(await GetSparqlResults(CommonsQueryServiceEndpoint, CommonsQueryServiceOAuthCookieName, wcqsOAuth2Cookie, @"
SELECT DISTINCT ?item
WITH
 {
   SELECT ?item WHERE {
    SERVICE wikibase:mwapi {
      bd:serviceParam wikibase:endpoint ""commons.wikimedia.org"";
                      wikibase:api ""Generator"";
                      mwapi:generator ""exturlusage"";
                      mwapi:geuprotocol ""https"";
                      mwapi:geuquery """ + queryUserName + @""";
                      mwapi:geulimit ""max"".
      ?pageid wikibase:apiOutput mwapi:pageid .
    }
    BIND (IRI(CONCAT('https://commons.wikimedia.org/entity/M', STR(?pageid))) AS ?item)
  } LIMIT 100
 } AS %filesNamed
WITH
 {
   SELECT ?item WHERE {
    SERVICE wikibase:mwapi {
      bd:serviceParam wikibase:endpoint ""commons.wikimedia.org"";
                      wikibase:api ""Generator"";
                      mwapi:generator ""exturlusage"";
                      mwapi:geuprotocol ""https"";
                      mwapi:geuquery """ + queryUserNumber + @""";
                      mwapi:geulimit ""max"".
      ?pageid wikibase:apiOutput mwapi:pageid .
    }
    BIND (IRI(CONCAT('https://commons.wikimedia.org/entity/M', STR(?pageid))) AS ?item)
  } LIMIT 100
 } AS %filesNumbered

WHERE {
  {
    { INCLUDE %filesNamed }
    UNION
    { INCLUDE %filesNumbered }
  }
}
"), new Dictionary<string, string> { { "item", "uri" } }).ToList();
        var count = entities.Count;
        var counter = 0;
        foreach (var row in entities)
        {
            var entityId = GetEntityIdFromUri(row[0]!);
            await Console.Out.WriteLineAsync($"Reading {entityId} ({++counter}/{count})");
            var entity = new Entity(wikidataSite, entityId);
            await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

            Claim claim;
            bool edited;
            if (entity.Claims.ContainsKey(WikidataProperties.Creator))
            {
                var claims = entity.Claims[WikidataProperties.Creator];
                if (claims.Count != 1)
                {
                    // ?!?
                    await Console.Error.WriteLineAsync($"Error: {claims.Count} author claims on {entityId}");
                    continue;
                }
                claim = claims.Single();
                if (claim.MainSnak.SnakType != SnakType.SomeValue)
                {
                    // ?
                    await Console.Error.WriteLineAsync($"Error: {claim.MainSnak.SnakType} author claim on {entityId}");
                    continue;
                }
                edited = false;
            }
            else
            {
                edited = true;
                var mainSnak = new Snak(WikidataProperties.Creator, SnakType.SomeValue)
                {
                    DataType = BuiltInDataTypes.WikibaseItem
                };
                claim = new Claim(mainSnak);
            }
            edited |= CheckOrAddQualifier(entityId, claim.Qualifiers, WikidataProperties.AuthorText, authorName, BuiltInDataTypes.String);
            edited |= CheckOrAddQualifier(entityId, claim.Qualifiers, WikidataProperties.WikimediaUsername, authorWikimediaUsername, BuiltInDataTypes.ExternalId);
            edited |= CheckOrAddQualifier(entityId, claim.Qualifiers, WikidataProperties.FlickrId, authorFlickrId, BuiltInDataTypes.ExternalId);
            edited |= CheckOrAddQualifier(entityId, claim.Qualifiers, WikidataProperties.Url, authorUrl, BuiltInDataTypes.Url);
            if (edited)
            {
                var edits = new List<EntityEditEntry>(1) { new(nameof(entity.Claims), claim) };
                await Console.Out.WriteAsync($"Editing {entityId}...");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                await Console.Out.WriteLineAsync();
            }
        }
    }

    private static bool CheckOrAddQualifier(string entityId, IList<Snak> qualifiers, string propertyId, object dataValue, WikibaseDataType dataType)
    {
        foreach (var existingQualifier in qualifiers.Where(q => q.PropertyId == propertyId))
        {
            if (dataValue.Equals(existingQualifier.DataValue))
            {
                return false;
            }
            Console.Error.WriteLine($"Warning: Additional different {propertyId} qualifier at {entityId}");
        }

        qualifiers.Add(new Snak(propertyId, dataValue, dataType));
        return true;
    }
}