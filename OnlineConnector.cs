﻿using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Topuino_Client_Windows
{
    internal class OnlineConnector
    {
        private HttpClient client = new HttpClient();
        private int errorCount = 0;

        internal async void Post(Dictionary<string, string> data)
        {
            try
            {
                HttpContent content = new FormUrlEncodedContent(data);
                HttpResponseMessage response = await client.PostAsync("https://iot.vvzero.com/topuino/putdata", content);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                OnlineConnectorResponse? respData = JsonConvert.DeserializeObject<OnlineConnectorResponse>(responseBody);
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