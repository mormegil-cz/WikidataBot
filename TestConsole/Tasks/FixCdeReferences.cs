using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public static class FixCdeReferences
{
    private static readonly string EditSummary = MakeEditSummary("Update of P9391 identifier format", GenerateRandomEditGroupId());

    private static Dictionary<string, string> conversionTable;

    public static async Task Run(WikiSite wikidataSite)
    {
        conversionTable = await LoadConversionTable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "divadelni encyklopedie - Sheet1.tsv"));

        var entitiesToFix = GetEntities(await GetSparqlResults(@"
SELECT DISTINCT ?item WHERE {
  ?item ?p ?prop.
  ?prop prov:wasDerivedFrom ?ref.
  ?ref pr:P9391 ?encId.
  FILTER (REGEX(?encId, '[a-zA-Z_]'))
}
"), new Dictionary<string, string> { { "item", "uri" } }).ToList();
        var count = entitiesToFix.Count;
        var curr = 0;
        foreach (var row in entitiesToFix)
        {
            ++curr;
            var entityId = GetEntityIdFromUri(row[0]);
            var entity = new Entity(wikidataSite, entityId);
            await entity.RefreshAsync(EntityQueryOptions.FetchAllProperties, ["cs", "en"]);

            // var editedClaims = await ProcessIdentifiersInEntityCareful(entity, entityId);
            var editedClaims = await ProcessIdentifiersInEntityByMapping(entity, entityId);
            if (editedClaims == null) continue;

            var edits = editedClaims.Select(claim => new EntityEditEntry(nameof(Entity.Claims), claim)).ToList();

            await Console.Error.WriteLineAsync($"{curr}/{count}: Editing {entityId} ({edits.Count} changes)");
            await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
        }
    }

    private static async Task<HashSet<Claim>?> ProcessIdentifiersInEntityByMapping(Entity entity, string entityId)
    {
        var refIdentifierSnaks = GetRefIdentifierSnaks(entity);
        var editedClaims = new HashSet<Claim>(refIdentifierSnaks.Count);
        foreach (var (claim, reference, snak) in refIdentifierSnaks.Where(s => !IsNumericIdent((string?) s.snak.DataValue)))
        {
            var oldIdentifier = (string) snak.DataValue!;
            if (!conversionTable.TryGetValue(oldIdentifier, out var newIdentifier))
            {
                await Console.Error.WriteLineAsync($"Warning: {entityId} uses unknown '{oldIdentifier}'");
                return null;
            }
            var fixedSnak = new Snak(snak.PropertyId, newIdentifier, snak.DataType!);
            var snakIdx = reference.Snaks.IndexOf(snak);
            reference.Snaks[snakIdx] = fixedSnak;
            await Console.Error.WriteLineAsync($"- Fixing ref {reference.Hash} at {entityId}");
            editedClaims.Add(claim);
        }
        return editedClaims;
    }

    private static async Task<HashSet<Claim>?> ProcessIdentifiersInEntityCareful(Entity entity, string entityId)
    {
        var identifiers = entity.Claims[WikidataProperties.CzechTheatreEncyclopedia];
        if (identifiers.Count != 1)
        {
            await Console.Error.WriteLineAsync($"Warning: {entityId} has {identifiers.Count} identifiers, skipping");
            return null;
        }
        var newIdentifier = (string?) identifiers.Single().MainSnak.DataValue;
        if (newIdentifier == null)
        {
            await Console.Error.WriteLineAsync($"Warning: {entityId} has non-value identifier, skipping");
            return null;
        }

        var refIdentifierSnaks = GetRefIdentifierSnaks(entity);

        var refIdentifiers = refIdentifierSnaks.Select(s => (string?) s.snak.DataValue).ToHashSet();
        if (refIdentifiers.Contains(null))
        {
            // ?!?
            await Console.Error.WriteLineAsync($"Warning: {entityId} uses non-value identifier, skipping");
            return null;
        }

        var identifiersByType = refIdentifiers.GroupBy(IsNumericIdent).ToDictionary(g => g.Key, g => g.ToList());
        if (identifiersByType.Any(p => p.Value.Count > 1))
        {
            await Console.Error.WriteLineAsync($"Warning: {entityId} uses multiple identifiers, skipping");
            return null;
        }

        if (identifiersByType.TryGetValue(true, out var usedNewIdentifier) && (usedNewIdentifier.Count != 1 || usedNewIdentifier.Single() != newIdentifier))
        {
            await Console.Error.WriteLineAsync($"Warning: {entityId} uses different new identifier, skipping");
            return null;
        }
        var oldIdentifier = identifiersByType[false].Single()!;

        if (!conversionTable.TryGetValue(oldIdentifier, out var convertedIdentifier) || convertedIdentifier != newIdentifier)
        {
            await Console.Error.WriteLineAsync($"Warning: {entityId} conversion mismatch: {oldIdentifier} → {convertedIdentifier}/{newIdentifier}, skipping");
            return null;
        }

        var editedClaims = new HashSet<Claim>(refIdentifierSnaks.Count);
        foreach (var (claim, reference, snak) in refIdentifierSnaks.Where(s => oldIdentifier == (string?) s.snak.DataValue))
        {
            var fixedSnak = new Snak(snak.PropertyId, newIdentifier, snak.DataType!);
            var snakIdx = reference.Snaks.IndexOf(snak);
            reference.Snaks[snakIdx] = fixedSnak;
            await Console.Error.WriteLineAsync($"- Fixing ref {reference.Hash} at {entityId}");
            editedClaims.Add(claim);
        }
        return editedClaims;
    }

    private static List<(Claim claim, ClaimReference reference, Snak snak)> GetRefIdentifierSnaks(Entity entity) =>
        (from claim in entity.Claims
            from reference in claim.References
            from snak in reference.Snaks
            where snak is { PropertyId: WikidataProperties.CzechTheatreEncyclopedia, SnakType: SnakType.Value }
            select (claim, reference, snak))
        .ToList();

    private static ValueTask<Dictionary<string, string>> LoadConversionTable(string path) =>
        File.ReadLinesAsync(path)
            .Skip(1)
            .Select(line => line.Split('\t'))
            .ToDictionaryAsync(parts => parts[1], parts => parts[0]);

    private static bool IsNumericIdent(string? ident) => ident!.All(c => c is >= '0' and <= '9');
}