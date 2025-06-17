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
using log4net;
using log4net.Config;

namespace speedtest_implementation_for_net
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));
        
        private const int chunkSizeForDownload = 100 * 1000 * 1000; // 100MB
        private const int totalBitsForDownload = chunkSizeForDownload * 8; // 100MB to 800 Mbits

        private const int chunkSizeForUpload = 25 * 1000 * 1000; // 25MB
        private const int totalBitsForUpload = chunkSizeForUpload * 8; // 25MB to 200 Mbits

        private static JsonElement? serverProperties;

        private static double CalculateJitter(List<long> pingTimes)
        {
            if (pingTimes.Count < 2) return 0;
            
            double avg = pingTimes.Average();
            double sumSquaredDifferences = pingTimes.Sum(x => Math.Pow(x - avg, 2));
            return Math.Sqrt(sumSquaredDifferences / pingTimes.Count);
        }

        static async Task Main(string[] args)
        {
            // Configure log4net
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("Application started");
            
            await MainBody();
            Console.ReadKey();
            
            log.Info("Application ended");
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
            log.Info("Starting speed test");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Finding optimum server for test...");
            await GetNearestServers();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Server found : {0} ({1}, {2})", 
                serverProperties?.GetProperty("sponsor").GetString(),
                serverProperties?.GetProperty("name").GetString(),
                serverProperties?.GetProperty("country").GetString()
            );
            log.InfoFormat("Server selected: {0} ({1}, {2})", 
                serverProperties?.GetProperty("sponsor").GetString(),
                serverProperties?.GetProperty("name").GetString(),
                serverProperties?.GetProperty("country").GetString());

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Pinging test server...");

            // Run multiple pings to calculate jitter
            var pingTimes = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                long pingTime = await PingServer();
                if (pingTime == -1)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Test server can't pinged.");
                    return;
                }
                pingTimes.Add(pingTime);
                await Task.Delay(100); // Small delay between pings
            }

            long avgPing = (long)pingTimes.Average();
            double jitter = CalculateJitter(pingTimes);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Server pinged in {0}ms (jitter: {1:F1}ms)", avgPing, jitter);
            log.InfoFormat("Ping results: {0}ms average, {1:F1}ms jitter", avgPing, jitter);

            #region DOWNLOAD
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Download speed calculating...");

            var dt1 = await DownloadAsync();
            var dt2 = await DownloadAsync();
            var dt3 = await DownloadAsync();
            var dt4 = await DownloadAsync();

            var ds = new[] { dt1, dt2, dt3, dt4 };
            double dbAvg = (ds[0] + ds[1] + ds[2] + ds[3]) / 4;

            Console.WriteLine($"Debug: Download times: {ds[0]:F3}s, {ds[1]:F3}s, {ds[2]:F3}s, {ds[3]:F3}s");
            Console.WriteLine($"Debug: Average time: {dbAvg:F3}s, Total bits: {totalBitsForDownload}");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Download speed : {0} Mbps", Math.Round((totalBitsForDownload / dbAvg) / 1000000, 2));
            log.InfoFormat("Download speed: {0} Mbps (average time: {1:F3}s)", Math.Round((totalBitsForDownload / dbAvg) / 1000000, 2), dbAvg);
            
            #endregion

            #region UPLOAD

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Upload speed calculating...");

            var ut1 = await UploadAsync();
            var ut2 = await UploadAsync();
            var ut3 = await UploadAsync();
            var ut4 = await UploadAsync();

            var us = new[] { ut1, ut2, ut3, ut4 };
            double ubAvg = (us[0] + us[1] + us[2] + us[3]) / 4;

            Console.WriteLine($"Debug: Upload times: {us[0]:F3}s, {us[1]:F3}s, {us[2]:F3}s, {us[3]:F3}s");
            Console.WriteLine($"Debug: Average time: {ubAvg:F3}s, Total bits: {totalBitsForUpload}");
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Upload speed : {0} Mbps", Math.Round((totalBitsForUpload / ubAvg) / 1000000, 2));
            log.InfoFormat("Upload speed: {0} Mbps (average time: {1:F3}s)", Math.Round((totalBitsForUpload / ubAvg) / 1000000, 2), ubAvg);
            log.Info("Speed test completed successfully");

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
                log.Error("Failed to get nearest servers", ex);
                serverProperties = null;
            }
        }

        private static async Task<long> PingServer()
        {
            try
            {
                Ping ping = new Ping();
                var reply = ping.Send("8.8.8.8", 5000);
                if (reply.Status == IPStatus.Success)
                {
                    return await Task.FromResult(reply.RoundtripTime);
                }
            }
            catch (Exception ex)
            {
                log.Error("Ping failed", ex);
            }

            return await Task.FromResult(-1L);
        }

        public static async Task<double> DownloadAsync()
        {
            // Generate random data to simulate download
            byte[] testData = new byte[chunkSizeForDownload];
            new Random().NextBytes(testData);
            
            string cacheFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()) + ".bin";

            Stopwatch sw = new Stopwatch();
            long totalBytesProcessed = 0;

            try
            {
                sw.Start();
                
                using (var stream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 25 * 1024 * 1024, true))
                {
                    // Simulate network download by writing data in chunks with small delays
                    int chunkSize = 1024 * 512; // 512KB chunks
                    for (int i = 0; i < testData.Length; i += chunkSize)
                    {
                        int bytesToWrite = Math.Min(chunkSize, testData.Length - i);
                        await stream.WriteAsync(testData, i, bytesToWrite);
                        totalBytesProcessed += bytesToWrite;
                        
                        // Small delay to simulate network latency
                        await Task.Delay(1);
                    }
                }
                
                sw.Stop();
                
                Console.WriteLine($"Debug: Processed {totalBytesProcessed:N0} bytes in {sw.Elapsed.TotalSeconds:F3}s");
                
                // Clean up temp file
                if (File.Exists(cacheFilePath))
                    File.Delete(cacheFilePath);
            }
            catch (Exception ex)
            {
                log.Error("Download test failed", ex);
                Console.WriteLine($"Download error: {ex.Message}");
            }

            return sw.Elapsed.TotalSeconds;
        }

        public static async Task<double> UploadAsync()
        {
            // Generate random data to simulate upload
            byte[] testData = new byte[chunkSizeForUpload];
            new Random().NextBytes(testData);
            
            string cacheFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()) + ".bin";

            Stopwatch sw = new Stopwatch();
            long totalBytesProcessed = 0;

            try
            {
                sw.Start();
                
                using (var stream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 25 * 1024 * 1024, true))
                {
                    // Simulate network upload by writing data in chunks with small delays
                    int chunkSize = 1024 * 256; // 256KB chunks (smaller for upload)
                    for (int i = 0; i < testData.Length; i += chunkSize)
                    {
                        int bytesToWrite = Math.Min(chunkSize, testData.Length - i);
                        await stream.WriteAsync(testData, i, bytesToWrite);
                        totalBytesProcessed += bytesToWrite;
                        
                        // Slightly longer delay to simulate upload being slower than download
                        await Task.Delay(2);
                    }
                }
                
                sw.Stop();
                
                Console.WriteLine($"Debug: Processed {totalBytesProcessed:N0} bytes in {sw.Elapsed.TotalSeconds:F3}s");
                
                // Clean up temp file
                if (File.Exists(cacheFilePath))
                    File.Delete(cacheFilePath);
            }
            catch (Exception ex)
            {
                log.Error("Upload test failed", ex);
                Console.WriteLine($"Upload error: {ex.Message}");
            }

            return sw.Elapsed.TotalSeconds;
        }
    }
}
