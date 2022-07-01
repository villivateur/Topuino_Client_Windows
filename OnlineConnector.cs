using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Topuino_Client_Windows
{
    internal class OnlineConnector
    {
        private HttpClient client = new HttpClient();

        internal async Task Post(Dictionary<string, string> data)
        {
            HttpContent content = new FormUrlEncodedContent(data);
            HttpResponseMessage response = await client.PostAsync("https://iot.vvzero.com/topuino/putdata", content);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            OnlineConnectorResponse? respData = JsonConvert.DeserializeObject<OnlineConnectorResponse>(responseBody);
            if (respData == null || respData.CODE != 0)
            {
                throw new Exception("网络连接异常");
            }
        }

        internal void Dispose()
        {
            client.Dispose();
        }
    }

    internal class OnlineConnectorResponse
    {
        public int CODE = 0;
    }
}
