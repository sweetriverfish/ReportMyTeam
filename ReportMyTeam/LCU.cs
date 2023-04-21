﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReportMyTeam
{
    internal class LCU
    {
        private static string[] leagueAuth;
        private static int lcuPid = 0;
        public static bool isClientOn = false;

        public static void CheckIfLeagueClientIsOpenTask()
        {
            while (true)
            {
                Process client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (client != null)
                {
                    leagueAuth = getLeagueAuth(client);
                    if (lcuPid != client.Id)
                    {
                        lcuPid = client.Id;
                        isClientOn = true;
                        Console.Clear();
                        Console.Write("Initializing..");
                    }
                }
                else
                {
                    isClientOn = false;
                    Program.resetData();
                }
                if (!Program.foundFriends && isClientOn)
                {
                    Program.getFriendsIds();
                }
                Thread.Sleep(1000);
            }
        }

        private static string[] getLeagueAuth(Process client)
        {
            string command = "wmic process where 'Processid=" + client.Id + "' get Commandline";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + command);
            psi.RedirectStandardOutput = true;

            Process cmd = new Process();
            cmd.StartInfo = psi;
            cmd.Start();

            string output = cmd.StandardOutput.ReadToEnd();
            cmd.WaitForExit();

            // Parse the port and auth token into variables
            string port = Regex.Match(output, @"--app-port=""?(\d+)""?").Groups[1].Value;
            string authToken = Regex.Match(output, @"--remoting-auth-token=([a-zA-Z0-9_-]+)").Groups[1].Value;

            // Compute the encoded key
            string auth = "riot:" + authToken;
            string authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));

            // Return content
            return new string[] { authBase64, port };
        }

        public static string[] clientRequest(string method, string url, string body = null)
        {
            // Ignore invalid https
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            try
            {
                using (HttpClient client = new HttpClient(handler))
                {
                    // Set URL
                    client.BaseAddress = new Uri("https://127.0.0.1:" + leagueAuth[1] + "/");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", leagueAuth[0]);

                    // Set headers
                    HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), url);

                    // Send POST data when doing a post request
                    if (!string.IsNullOrEmpty(body))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    // Get the response
                    HttpResponseMessage response = client.SendAsync(request).Result;

                    // If the response is null (League client closed?)
                    if (response == null)
                    {
                        return new string[] { "999", "" };
                    }

                    // Get the HTTP status code
                    int statusCode = (int)response.StatusCode;
                    string statusString = statusCode.ToString();

                    // Get the body
                    string responseFromServer = response.Content.ReadAsStringAsync().Result;

                    // Clean up the response
                    response.Dispose();

                    // Return content
                    return new string[] { statusString, responseFromServer };
                }
            }
            catch
            {
                // If the URL is invalid (League client closed?)
                return new string[] { "999", "" };
            }
        }
    }
}
