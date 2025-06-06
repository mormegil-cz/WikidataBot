using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;
using static TestConsole.WikidataTools;

namespace TestConsole.Tasks;

public partial class ImportCadastralCoords
{
    [GeneratedRegex(@"^(-[1-9][0-9]*\.[0-9]+)\s+(-[1-9][0-9]*\.[0-9]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex RuianCoordsParser();

    [GeneratedRegex(@"^Point\((-?[0-9]+\.[0-9]+)\s+(-?[0-9]+\.[0-9]+)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex WikidataCoordsParser();

    private const int ImportBatchSize = 200;
    private const int CheckBatchSize = 200;

    private static readonly string[] Languages = Array.Empty<string>();

    private const string XMLNS_VF = "urn:cz:isvs:ruian:schemas:VymennyFormatTypy:v1";
    private const string XMLNS_KUI = "urn:cz:isvs:ruian:schemas:KatUzIntTypy:v1";
    private const string XMLNS_GML = "http://www.opengis.net/gml/3.2";

    private static readonly DateOnly RuianDumpDate = new(2025, 05, 31);
    private static readonly DateOnly AccessDate = new(2025, 06, 06);
    private static readonly string RuianDumpFilename = $"{RuianDumpDate:yyyyMMdd}_ST_UZSZ.xml.zip";
    private static readonly string RuianDumpUrl = $"https://vdp.cuzk.cz/vymenny_format/soucasna/{RuianDumpFilename}";

    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Import of coordinates for cadastral areas in the Czech Republic", EditGroupId);

    public static async Task Run(WikiSite wikidataSite)
    {
        // https://vdp.cuzk.cz/vdp/ruian/vymennyformat?crKopie=on&casovyRozsah=U&upStatAzZsj=on&uzemniPrvky=ST&dsZakladni=on&datovaSada=Z&vyZakladni=on&vyber=vyZakladni&search=
        // https://vdp.cuzk.cz/vymenny_format/soucasna/20250531_ST_UZSZ.xml.zip
        await Console.Out.WriteLineAsync("Loading KU data...");
        var ruianData = await LoadXmlData(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", RuianDumpFilename));
        await Console.Out.WriteLineAsync($"Done, {ruianData.Count} entries loaded");

        await ImportCoordsToWikidata(wikidataSite, ruianData);
        //await CheckCoordsInWikidata(wikidataSite, ruianData);
    }

    private static async Task ImportCoordsToWikidata(WikiSite wikidataSite, Dictionary<string, (float, float)> ruianData)
    {
        var batchCount = (ruianData.Count + ImportBatchSize - 1) / ImportBatchSize;
        var batchNumber = 0;
        foreach (var batch in ruianData.Keys.Batch(ImportBatchSize).Skip(batchNumber))
        {
            ++batchNumber;
            await Console.Out.WriteLineAsync($"Fetching batch {batchNumber}/{batchCount} from WQS...");

            var kuIds = "'" + String.Join("' '", batch) + "'";
            foreach (var row in GetEntities(await GetSparqlResults(@"
SELECT ?item ?kuid WHERE {
  VALUES ?kuid { " + kuIds + @" }.
  ?item wdt:P7526 ?kuid.
  MINUS { ?item wdt:P625 [] }
}"), new Dictionary<string, string> { { "item", "uri" }, { "kuid", "literal" } }))
            {
                var entityId = GetEntityIdFromUri(row[0]!);
                var kuId = row[1]!;

                if (!ruianData.TryGetValue(kuId, out var coords))
                {
                    await Console.Error.WriteLineAsync($"WARNING: No KU with '{kuId}'");
                    continue;
                }

                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    continue;
                }

                if (entity.Claims.ContainsKey(WikidataProperties.Coordinates))
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} already contains coordinates");
                    continue;
                }

                var claimCoordinates = new Claim(new Snak(WikidataProperties.Coordinates, new WbGlobeCoordinate(coords.Item1, coords.Item2, 0.001, WikidataProperties.GlobeEarth), BuiltInDataTypes.GlobeCoordinate));
                claimCoordinates.References.Add(new ClaimReference(
                    new Snak(WikidataProperties.StatedIn, "Q105335225", BuiltInDataTypes.WikibaseItem),
                    new Snak(WikidataProperties.PublishDate, new WbTime(RuianDumpDate.Year, RuianDumpDate.Month, RuianDumpDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time),
                    new Snak(WikidataProperties.ReferenceUrl, RuianDumpUrl, BuiltInDataTypes.Url),
                    new Snak(WikidataProperties.AccessDate, new WbTime(AccessDate.Year, AccessDate.Month, AccessDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time)
                ));
                var edits = new[] { new EntityEditEntry(nameof(Entity.Claims), claimCoordinates) };
                await Console.Out.WriteAsync($"Editing {entityId}...");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
                await Console.Out.WriteLineAsync();
            }
        }
    }

    private static async Task CheckCoordsInWikidata(WikiSite wikidataSite, Dictionary<string, (float, float)> ruianData)
    {
        var batchCount = (ruianData.Count + CheckBatchSize - 1) / CheckBatchSize;
        var batchNumber = 0;
        foreach (var batch in ruianData.Keys.Batch(CheckBatchSize).Skip(batchNumber))
        {
            ++batchNumber;
            await Console.Out.WriteAsync($"{batchNumber}...");

            var kuIds = "'" + String.Join("' '", batch) + "'";
            foreach (var row in GetEntities(await GetSparqlResults(@"
SELECT ?item ?kuid ?coords WHERE {
  VALUES ?kuid { " + kuIds + @" }.
  ?item wdt:P7526 ?kuid.
  OPTIONAL { ?item wdt:P625 ?coords }
}"), new Dictionary<string, string> { { "item", "uri" }, { "kuid", "literal" }, { "coords", "literal" } }))
            {
                var entityId = GetEntityIdFromUri(row[0]!);
                var kuId = row[1]!;
                var coordsStr = row[2];

                if (coordsStr == null)
                {
                    await Console.Error.WriteLineAsync($"\nEntity {entityId} does not have coordinates");
                    continue;
                }

                var parsedCoords = WikidataCoordsParser().Match(coordsStr); 
                if (!parsedCoords.Success) throw new FormatException("Coordinate syntax error");
                var wdLon = Single.Parse(parsedCoords.Groups[1].Value, CultureInfo.InvariantCulture);
                var wdLat = Single.Parse(parsedCoords.Groups[2].Value, CultureInfo.InvariantCulture);
                
                if (!ruianData.TryGetValue(kuId, out var coordsInRuian))
                {
                    await Console.Error.WriteLineAsync($"\nWARNING: No KU with '{kuId}' for entity {entityId}");
                    continue;
                }

                // heh! :-)
                var dist = Math.Abs(wdLat - coordsInRuian.Item1) + Math.Abs(wdLon - coordsInRuian.Item2);
                if (dist > 0.06)
                {
                    await Console.Error.WriteLineAsync($"\nEntity {entityId} has suspicious coordinates ({coordsInRuian.Item1}, {coordsInRuian.Item2} expected; distance {dist * 40000 / 360.0f})");
                }
                
                /*

                // alternate, more thorough (and much slower) tests, fetching the whole entity data

                var entity = new Entity(wikidataSite, entityId);
                await entity.RefreshAsync(EntityQueryOptions.FetchClaims, Languages);

                if (entity.Claims == null)
                {
                    await Console.Error.WriteLineAsync($"WARNING! Unable to read entity {entityId}!");
                    continue;
                }

                var coordsInEntity = entity.Claims[WikidataProperties.Coordinates];
                
                if (coordsInEntity == null)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} does not have coordinates");
                    continue;
                }
                if (coordsInEntity.Count != 1)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} has {coordsInEntity.Count} coordinate claims");
                    continue;
                }

                var coordSnak = coordsInEntity.Single().MainSnak;
                if (coordSnak.SnakType != SnakType.Value)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} has {coordSnak.SnakType} coordinate claim");
                    continue;
                }

                WbGlobeCoordinate wbGlobeCoordinate;
                try
                {
                    wbGlobeCoordinate = (WbGlobeCoordinate) coordSnak.DataValue;
                }
                catch (ArgumentException e)
                {
                    await Console.Error.WriteLineAsync($"Error parsing coordinates in entity {entityId}!");
                    await Console.Error.WriteLineAsync(e.ToString());
                    continue;
                }
                if (wbGlobeCoordinate.Globe != WikidataProperties.GlobeEarth)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} is on {wbGlobeCoordinate.Globe}!");
                    continue;
                }

                // heh! :-)
                var dist = Math.Abs(wbGlobeCoordinate.Latitude - coordsInRuian.Item1) + Math.Abs(wbGlobeCoordinate.Longitude - coordsInRuian.Item2);
                if (dist > 0.06)
                {
                    await Console.Error.WriteLineAsync($"Entity {entityId} has suspicious coordinates ({coordsInRuian.Item1}, {coordsInRuian.Item2} expected; distance {dist * 40000 / 360.0f})");
                }
                // if (wbGlobeCoordinate.Precision < 0.00001)
                // {
                //     await Console.Error.WriteLineAsync($"Entity {entityId} uses coordinate precision of {wbGlobeCoordinate.Precision}");
                // }
                */
            }
        }
    }

    private static async Task<Dictionary<string, (float, float)>> LoadXmlData(string filename)
    {
        string xmlDate;
        await using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        var zipArchiveEntry = zipArchive.Entries.Single();
        await using var xmlStream = zipArchiveEntry.Open();
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings { Async = true });

        await reader.MoveToContentAsync();
        reader.ReadStartElement("VymennyFormat", XMLNS_VF);
        reader.ReadStartElement("Hlavicka", XMLNS_VF);
        await XmlSkipTo(reader, XmlNodeType.Element, XMLNS_VF, "Datum", XmlNodeType.EndElement, XMLNS_VF, "Hlavicka");
        if (reader.LocalName == "Datum")
        {
            xmlDate = await reader.ReadElementContentAsStringAsync();
        }
        await XmlSkipTo(reader, XmlNodeType.Element, XMLNS_VF, "KatastralniUzemi", null, null, null);
        if (reader.LocalName != "KatastralniUzemi") throw new FormatException("KatastralniUzemi not found in XML data");

        var result = new Dictionary<string, (float, float)>();
        reader.ReadStartElement("KatastralniUzemi", XMLNS_VF);
        while (await reader.MoveToContentAsync() != XmlNodeType.EndElement)
        {
            reader.ReadStartElement("KatastralniUzemi", XMLNS_VF);
            string? kod = null;
            string? coordsStr = null;
            while (await reader.MoveToContentAsync() != XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "Kod":
                        if (reader.NamespaceURI != XMLNS_KUI) throw new FormatException("Unexpected namespace");
                        if (kod != null) throw new FormatException("Duplicate kod");
                        kod = await reader.ReadElementContentAsStringAsync();
                        break;

                    case "Geometrie":
                        if (reader.NamespaceURI != XMLNS_KUI) throw new FormatException("Unexpected namespace");
                        if (coordsStr != null) throw new FormatException("Duplicate coords");
                        reader.ReadStartElement("Geometrie", XMLNS_KUI);
                        reader.ReadStartElement("DefinicniBod", XMLNS_KUI);
                        reader.ReadStartElement("MultiPoint", XMLNS_GML);
                        reader.ReadStartElement("pointMembers", XMLNS_GML);
                        reader.ReadStartElement("Point", XMLNS_GML);
                        reader.ReadStartElement("pos", XMLNS_GML);
                        coordsStr = await reader.ReadContentAsStringAsync();
                        XmlReadEnd(reader); // /pos
                        XmlReadEnd(reader); // /Point
                        if (reader is { NodeType: XmlNodeType.Element, LocalName: "Point" })
                        {
                            // multipoint!?
                            await Console.Error.WriteLineAsync("Warning: Skipping additional coordinates for " + kod);
                            await XmlSkipTo(reader, XmlNodeType.EndElement, XMLNS_GML, "pointMembers", null, null, null);
                        }
                        XmlReadEnd(reader); // /pointMembers
                        XmlReadEnd(reader); // /MultiPoint
                        XmlReadEnd(reader); // /DefinicniBod
                        XmlReadEnd(reader); // /Geometrie
                        /*
                        reader.ReadEndElement();
                        reader.ReadEndElement();
                        reader.ReadEndElement();
                        reader.ReadEndElement();
                        reader.ReadEndElement();
                        reader.ReadEndElement();
                        */
                        break;

                    default:
                        await reader.SkipAsync();
                        break;
                }
            }
            if (kod == null || coordsStr == null) throw new FormatException("Missing kod or coords");
            var parsedCoords = RuianCoordsParser().Match(coordsStr);
            if (!parsedCoords.Success) throw new FormatException("Coordinate syntax error");
            var y = Single.Parse(parsedCoords.Groups[1].Value, CultureInfo.InvariantCulture);
            var x = Single.Parse(parsedCoords.Groups[2].Value, CultureInfo.InvariantCulture);
            var wgsCoords = Epsg5514.ConvertToWgs84(y, x, 200);
            result.Add(kod, (wgsCoords.Item1, wgsCoords.Item2));
            reader.ReadEndElement();
        }
        return result;
    }

    private static void XmlReadEnd(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.EndElement) throw new FormatException($"End of element expected, but {reader.NodeType} '{reader.Name}' found");
        reader.ReadEndElement();
    }

    private static async Task XmlSkipTo(XmlReader xmlReader, XmlNodeType expectedNodeType1, string expectedNs1, string expectedName1, XmlNodeType? expectedNodeType2, string? expectedNs2, string? expectedName2)
    {
        while (!xmlReader.EOF &&
               !(await xmlReader.MoveToContentAsync() == expectedNodeType1 && xmlReader.LocalName == expectedName1 && xmlReader.NamespaceURI == expectedNs1) &&
               !(xmlReader.NodeType == expectedNodeType2 && xmlReader.LocalName == expectedName2 && xmlReader.NamespaceURI == expectedNs2)
              )
        {
            await xmlReader.ReadAsync();
        }
    }
}