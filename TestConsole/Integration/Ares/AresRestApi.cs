using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestConsole.Integration.Ares.Schema;

namespace TestConsole.Integration.Ares;

public static class AresRestApi
{
    private static readonly HttpClient httpClient = InitHttpClient();

    private static HttpClient InitHttpClient()
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri("https://wwwinfo.mfcr.cz/cgi-bin/ares/darv_bas.cgi?xml=1");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(WikidataTools.ProductName, WikidataTools.Version));
        return client;
    }

    private static readonly XmlSerializer xmlSerializer = new(typeof(Ares_odpovedi));

    public static async Task<vypis_basic?> GetAresData(string ico)
    {
        var response = await httpClient.GetAsync("?ico=" + ico);
        if (!response.IsSuccessStatusCode) return null;

        Ares_odpovedi? aresData;
        await using (var stream = await response.Content.ReadAsStreamAsync())
        {
            aresData = (Ares_odpovedi?)xmlSerializer.Deserialize(stream);
        }
        if (aresData is not { Fault: null }) return null;
        if (aresData.Odpoved?.Length != 1 || aresData.Odpoved[0].VBAS?.Length != 1) return null;
        
        return aresData.Odpoved[0].VBAS[0];
    }

    public static string GetAresUrl(string ico) => httpClient.BaseAddress + "&ico=" + ico;
}