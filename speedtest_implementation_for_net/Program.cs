using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace speedtest_implementation_for_net
{
    class Program
    {
        private const int chunkSizeForDownload = 25 * 1000 * 1000; // approximately 23MB
        private const int totalBitsForDownload = chunkSizeForDownload * 8; // 23MB to 180 Mbits (approximately)

        private const int chunkSizeForUpload = 4 * 1000 * 1000; // approximately 4MB
        private const int totalBitsForUpload = chunkSizeForUpload * 8; // 4MB to 32 Mbits (approximately)

        private static JsonElement? serverProperties;

        static void Main(string[] args)
        {
            MainBody();
            Console.ReadKey();
        }

        private static async Task MainBody()
        {
            Console.WriteLine("Press ENTER key for run test.");

            WAITKEY:

            ConsoleKeyInfo cki = Console.ReadKey();
            if (cki.Key != ConsoleKey.Enter) goto WAITKEY;

            await RunTest();

            Console.WriteLine();
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("Press ENTER key for re-run test.");

            ConsoleKeyInfo cki2 = Console.ReadKey();
            if (cki2.Key == ConsoleKey.Enter)
            {
                Console.Clear();
                goto WAITKEY;
            }
        }

        private static async Task RunTest()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Finding optimum server for test...");
            await GetNearestServers();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Server found : {0} ({1}, {2})", 
                serverProperties?.GetProperty("sponsor").GetString(),
                serverProperties?.GetProperty("name").GetString(),
                serverProperties?.GetProperty("country").GetString()
            );

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Pinging test server...");

            long pingMS = await PingServer();
            if (pingMS == -1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Test server can't pinged.");

                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Server pinged in {0}ms", pingMS);

            #region DOWNLOAD
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Download speed calculating...");

            var dt1 = DownloadAsync();
            var dt2 = DownloadAsync();
            var dt3 = DownloadAsync();
            var dt4 = DownloadAsync();

            var ds = await Task.WhenAll(dt1, dt2, dt3, dt4);
            double dbAvg = (ds[0] + ds[1] + ds[2] + ds[3]) / 4;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Download speed : {0} Mbps", Math.Round((totalBitsForDownload / dbAvg) / (1024 * 1024), 2));
            
            #endregion

            #region UPLOAD

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Upload speed calculating...");

            var ut1 = await UploadAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Upload speed : {0} Mbps", Math.Round((totalBitsForUpload / ut1) / (1024 * 1024), 2));

            #endregion
        
        }

        private static async Task GetNearestServers()
        {
            string urlForServers = "http://www.speedtest.net/api/js/servers";
            string paramsForServers = "?engine=js";

            HttpClient client;

            try
            {
                client = new HttpClient();
                client.BaseAddress = new Uri(urlForServers);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage responseMessage = await client.GetAsync(paramsForServers);
                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseString = await responseMessage.Content.ReadAsStringAsync();
                    using (JsonDocument document = JsonDocument.Parse(responseString))
                    {
                        if (document.RootElement.GetArrayLength() > 0)
                        {
                            serverProperties = document.RootElement[0].Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                serverProperties = null;
            }
        }

        private static async Task<long> PingServer()
        {
            Ping ping = new Ping();
            PingReply reply = null;

            try
            {
                Uri hostUri = new Uri(serverProperties?.GetProperty("url").GetString());
                IPAddress ip = Dns.GetHostEntry(hostUri.Host).AddressList[0];
                reply = ping.Send(ip);
            }
            catch (Exception ex)
            {
                
            }

            return reply?.RoundtripTime ?? -1;
        }

        public static async Task<double> DownloadAsync()
        {
            string guidForThisDownload = Guid.NewGuid().ToString();
            string parameterForDownload = "download?nocache={0}&size={1}";
            string downloadUrl = "http://" + serverProperties?.GetProperty("host").GetString() + "/" + string.Format(parameterForDownload, guidForThisDownload, chunkSizeForDownload);
            string cacheFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()) + ".bin";

            Stopwatch swAll = new Stopwatch();
            Stopwatch swResponse = new Stopwatch();
            swAll.Start();
            swResponse.Start();

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.MaxResponseContentBufferSize = 30 * 1024 * 1024;

                    using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(downloadUrl)))
                    using (
                        Stream contentStream = await (await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)).Content.ReadAsStreamAsync(),
                        stream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 25 * 1024 * 1024, true))
                    {

                        swResponse.Stop();

                        byte[] buff = new byte[1024 * 512];
                        while (contentStream.Read(buff, 0, 1024 * 512) > 0)
                        {
                            await stream.WriteAsync(buff, 0, 1024 * 512);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            /*
            */

            swAll.Stop();
            return swAll.Elapsed.TotalSeconds;
        }

        public static async Task<double> UploadAsync()
        {
            string guidForThisUpload = Guid.NewGuid().ToString();
            string parameterForUpload = "upload?nocache={0}";
            string uploadUrl = "http://" + serverProperties?.GetProperty("host").GetString() + "/" + string.Format(parameterForUpload, guidForThisUpload);
            string cacheFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()) + ".bin";

            Stopwatch swAll = new Stopwatch();
            Stopwatch swResponse = new Stopwatch();
            swAll.Start();
            swResponse.Start();

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.BaseAddress = new Uri("http://" + serverProperties?.GetProperty("host").GetString());
                    httpClient.MaxResponseContentBufferSize = 30 * 1024 * 1024;
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                    //httpClient.DefaultRequestHeaders.Add("Content-Length", "100000");

                    await httpClient.PostAsync(parameterForUpload, new ByteArrayContent(new byte[chunkSizeForUpload]));
                    swResponse.Stop();

                    /*
                    using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(uploadUrl)))
                    using (
                        Stream contentStream = await (await httpClient.SendAsync(request)).Content.ReadAsStreamAsync(),
                        stream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 25 * 1024 * 1024, true))
                    {

                        swResponse.Stop();

                        byte[] buff = new byte[1024 * 512];
                        while (contentStream.Read(buff, 0, 1024 * 512) > 0)
                        {
                            await stream.WriteAsync(buff, 0, 1024 * 512);
                        }
                    }
                    */
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            /*
            */

            swAll.Stop();
            return swAll.Elapsed.TotalSeconds;
        }
    }
}
