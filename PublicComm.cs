using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Topuino_Client_Windows
{
    internal class PublicComm
    {
        private HttpClient client = new HttpClient();
        private int errorCount = 0;

        public async void Post(Dictionary<string, string> data)
        {
            try
            {
                HttpContent content = new FormUrlEncodedContent(data);
                HttpResponseMessage response = await client.PostAsync("http://127.0.0.1:7766/putdata", content);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                PublicCommResponse? respData = JsonConvert.DeserializeObject<PublicCommResponse>(responseBody);
                if (respData == null || respData.CODE != 0)
                {
                    throw new Exception();
                }
                errorCount = 0;
            }
            catch
            {
                errorCount++;
                if (errorCount > 5)
                {
                    // TODO
                }
            }
        }
    }

    internal class PublicCommResponse
    {
        public int CODE;
    }
}
