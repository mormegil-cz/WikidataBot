using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace TestConsole.Integration.AresV3;

public static class AresRestApi
{
    private static readonly AresV3ApiClient apiClient = InitApiClient();

    private static AresV3ApiClient InitApiClient()
    {
        var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri("https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/");
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(WikidataTools.ProductName, WikidataTools.Version));
        return new AresV3ApiClient(httpClient);
    }

    public static async Task<EkonomickySubjekt?> GetAresData(string ico)
    {
        try
        {
            return await apiClient.VratEkonomickySubjektAsync(ico);
        }
        catch (ApiException apiException)
        {
            await Console.Error.WriteLineAsync($"WARNING: Fetching data for {ico} from ARES failed: {apiException.Message}");
            return null;
        }
    }

    public static string GetAresUrl(string ico) => "https://ares.gov.cz/ekonomicke-subjekty?ico=" + ico;
}