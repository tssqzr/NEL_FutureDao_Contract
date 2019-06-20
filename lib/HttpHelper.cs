using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NEL_FutureDao_Contract.lib
{
    class HttpHelper
    {
        public static string Post(string url, string method, JArray _params)
        {
            var json = new JObject() {
                    { "jsonrpc", "2.0" },
                    { "method", method },
                    { "params", _params },
                    { "id", 1 },
                }.ToString();

            var Res = HttpPost(url, Encoding.UTF8.GetBytes(json.ToString()));
            return Res.Result;
        }
        public static async Task<string> HttpPost(string url, byte[] data)
        {
            WebClient wc = new WebClient();
            wc.Headers["content-type"] = "text/plain;charset=UTF-8";
            byte[] retdata = await wc.UploadDataTaskAsync(url, "POST", data);
            return Encoding.UTF8.GetString(retdata);
        }
    }
}
