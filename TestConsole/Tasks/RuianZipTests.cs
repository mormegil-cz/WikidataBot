using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole.Tasks;

public class RuianZipTests
{
    private const string BasePath = @"/home/petr/Downloads";
    private static readonly DateOnly ImportCsvDate = new(2024, 4, 30);
    private static readonly DateTime ImportTimestamp = DateTime.UtcNow;

    // see https://nahlizenidokn.cuzk.cz/StahniAdresniMistaRUIAN.aspx
    private static readonly string ImportCsvFile = $"{ImportCsvDate:yyyyMMdd}_OB_ADR_csv.zip";

    public static async Task Run()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1250 = Encoding.GetEncoding(1250);

        var streetNumbers = new Dictionary<string, (Dictionary<string, string> cps, Dictionary<string, string> cos)>();

        //Console.WriteLine($"Obec;ID obce;Ulice;ID ulice;Číslo;Část obce 1;ID adr.bodu 1;Část obce 2;ID adr.bodu 2");
        Console.WriteLine($"Obec;ID obce;Část obce;Ulice;ID ulice;Číslo 1;ID adr.bodu 1;Číslo 2;ID adr.bodu 2");

        var maxDiff = 0;
        var best = "";

        using var zip = ZipFile.OpenRead(Path.Combine(BasePath, ImportCsvFile));
        foreach (var entry in zip.Entries)
        {
            using var csv = new StreamReader(entry.Open(), cp1250);

            // skip header
            await csv.ReadLineAsync();

            string? line;
            while ((line = await csv.ReadLineAsync()) != null)
            {
                if (line.Contains('"')) throw new FormatException("Escaped CSV values not supported");
                var fields = line.Split(';');
                var streetIdStr = fields[9];
                var zipCode = fields[15];
                if (streetIdStr.Length == 0) continue;
                if (zipCode.Length != 5 || streetIdStr.Length < 2 || streetIdStr.Length > 7) throw new FormatException($"Unexpected values: '{streetIdStr}' '{zipCode}' at '{entry.Name}': '{line}'");
                Int32.Parse(zipCode, CultureInfo.InvariantCulture);
                var addrId = fields[0];
                // var streetId = Int32.Parse(streetIdStr, CultureInfo.InvariantCulture);
                var muniId = fields[1];
                var muniName = fields[2];
                var part = fields[8];
                var streetName = fields[10];
                var typ = fields[11];
                var cp = fields[12];
                var co = fields[13];
                var coSuffix = fields[14];
                var streetNum = $"{typ} {cp}/{co}{coSuffix}".TrimEnd('/');
                var reversedNum = $"{typ} {co}{coSuffix}/{cp}";
                var fullCp = $"{typ} {cp}";
                var fullCo = co + coSuffix;
                //var streetInfo = $"{streetNum} ({addrId})";
                var streetInfo = $"{streetNum};{addrId}";

                var streetIdent = $"{streetIdStr}@{part}";
                if (!streetNumbers.TryGetValue(streetIdent, out var numberSets)) streetNumbers.Add(streetIdent, numberSets = ([], []));

                /*
                if (set.TryGetValue(reversedNum, out var otherId))
                {
                    //Console.WriteLine($"{muniName} ({muniId}) {part} has {streetName} ({streetIdStr}) {streetNum} ({addrId}) with {reversedNum} ({otherId})");
                    Console.WriteLine($"{muniName};{muniId};{part};{streetName};{streetIdStr};{streetNum};{addrId};{reversedNum};{otherId}");
                }
                set.Add(streetNum, addrId);
                */

                if (numberSets.cos.TryGetValue(cp, out var otherCo))
                {
                    // Console.WriteLine($"{muniName} ({muniId}) {part} has {streetName} ({streetIdStr}) {streetNum} ({addrId}) with {otherCo}");
                    //Console.WriteLine($"{muniName};{muniId};{part};{streetName};{streetIdStr};{streetNum};{addrId};{otherCo}");
                }
                if (fullCo != "" && fullCo != cp)
                {
                    if (numberSets.cps.TryGetValue(fullCo, out var otherCp))
                    {
                        //Console.WriteLine($"{muniName} ({muniId}) {part} has {streetName} ({streetIdStr}) {streetNum} ({addrId}) with {otherCp}");
                        //Console.WriteLine($"{muniName};{muniId};{part};{streetName};{streetIdStr};{streetNum};{addrId};{otherCp}");
                    }
                    numberSets.cos.TryAdd(fullCo, streetInfo);
                    var diff = Int32.Parse(co) - Int32.Parse(cp);
                    if (diff > maxDiff && addrId != "2088916")
                    {
                        maxDiff = diff;
                        best = $"{muniName};{muniId};{part};{streetName};{streetIdStr};{streetNum};{addrId}";
                    }
                }
                numberSets.cps.TryAdd(cp, streetInfo);

                /*
                var info = $"{part} (see {addrId})";
                //var info = $"{part};{addrId}";
                if (!set.TryAdd(streetNum, info))
                {
                    //Console.WriteLine($"{muniName} ({muniId}) has {streetName} ({streetId}) {streetNum} duplicated in {part} ({addrId}) and {set[streetNum]}");
                    //Console.WriteLine($"{muniName};{muniId};{streetName};{streetId};{streetNum};{part};{addrId};{set[streetNum]}");
                }
                */
            }
        }
        Console.WriteLine($"Best: {best}");
    }
}