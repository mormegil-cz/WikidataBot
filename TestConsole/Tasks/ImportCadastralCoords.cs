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

namespace TestConsole.Tasks;

public partial class ImportCadastralCoords
{
    [GeneratedRegex(@"^(-[1-9][0-9]*\.[0-9]+)\s+(-[1-9][0-9]*\.[0-9]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex CoordsParser();

    private const string XMLNS_VF = "urn:cz:isvs:ruian:schemas:VymennyFormatTypy:v1";
    private const string XMLNS_KUI = "urn:cz:isvs:ruian:schemas:KatUzIntTypy:v1";
    private const string XMLNS_GML = "http://www.opengis.net/gml/3.2";

    private static readonly DateOnly RuianDumpDate = new(2023, 10, 31);
    private static readonly string RuianDumpFilename = $"{RuianDumpDate:yyyyMMdd}_ST_UZSZ.xml.zip";

    public static async Task Run(WikiSite wikidataSite)
    {
        // https://vdp.cuzk.cz/vdp/ruian/vymennyformat?crKopie=on&casovyRozsah=U&upStatAzZsj=on&uzemniPrvky=ST&dsZakladni=on&datovaSada=Z&vyZakladni=on&vyber=vyZakladni&search=
        // https://vdp.cuzk.cz/vymenny_format/soucasna/20231031_ST_UZSZ.xml.zip
        var ruianData = await LoadXmlData(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", RuianDumpFilename));
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
                            Console.WriteLine("Warning: Skipping additional coordinates for " + kod);
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
            var parsedCoords = CoordsParser().Match(coordsStr);
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