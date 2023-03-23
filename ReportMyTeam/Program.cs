using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Net.Http;

namespace ReportMyTeam
{
    class Program
    {
        public static string[] leagueAuth;
        public static int lcuPid = 0;
        public static bool isClientOn = false;

        public static string lastGameId = "0";
        public static string currentPlayerId = "0";
        public static List<string> friendsIds = new List<string>();
        public static bool foundFriends = false;

        private static void Main()
        {
            Console.Write("Initializing..");
            //Console.CursorVisible = false;
            Console.Title = "ReportMyTeam";

            var taskPhase = new Task(findPhase);
            taskPhase.Start(); 
            var taskLeagueAlive = new Task(CheckIfLeagueClientIsOpenTask);
            taskLeagueAlive.Start();

            var tasks = new[] { taskPhase, taskLeagueAlive };
            Task.WaitAll(tasks);

            Console.ReadKey();
        }

        private static void CheckIfLeagueClientIsOpenTask()
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
                    currentPlayerId = "0";

                    friendsIds.Clear();
                    foundFriends = false;
                }
                if (!foundFriends && isClientOn)
                {
                    getFriendsIds();
                }
                Thread.Sleep(2000);
            }
        }

        private static bool CheckIfLeagueClientIsOpen()
        {
            Process client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
            if (client != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static void getFriendsIds()
        {
            string[] friendsList = clientRequest(leagueAuth, "GET", "lol-chat/v1/friends", "");
            //Console.WriteLine(friendsList[1]);

            if (friendsList[0] == "200")
            {
                string[] friends = friendsList[1].Split("},{");
                foreach (var friend in friends)
                {
                    string friendId = friend.Split("\"summonerId\":")[1].Split(',')[0];
                    friendsIds.Add(friendId);
                }
                foundFriends = true;
            }
        }

        private static void findPhase()
        {
            while (leagueAuth == null && isClientOn || !foundFriends)
            {
                Console.Write(".");
                Thread.Sleep(200);
            }
            Console.WriteLine("");
            Console.WriteLine("Ready.");
            Thread.Sleep(1069);
            Console.Clear();
            Console.WriteLine("Awaiting for a game to be over.");
            Console.WriteLine("------------------");
            while (true)
            {
                if (CheckIfLeagueClientIsOpen())
                {
                    string[] gameSession = clientRequest(leagueAuth, "GET", "lol-gameflow/v1/session", "");

                    if (gameSession[0] == "200")
                    {
                        string phase = gameSession[1].Split("phase").Last().Split('"')[2];

                        switch (phase)
                        {
                            case "Lobby":
                                Thread.Sleep(60000);
                                break;
                            case "Matchmaking":
                                Thread.Sleep(60000);
                                break;
                            case "ReadyCheck":
                                Thread.Sleep(60000);
                                break;
                            case "ChampSelect":
                                Thread.Sleep(60000);
                                break;
                            case "InProgress":
                                Thread.Sleep(4000);
                                break;
                            case "WaitingForStats":
                                Thread.Sleep(2000);
                                break;
                            case "PreEndOfGame":
                                hanldeEndGame();
                                Thread.Sleep(1000);
                                break;
                            case "EndOfGame":
                                hanldeEndGame();
                                Thread.Sleep(1000);
                                break;
                            default:
                                //Debug.WriteLine(phase);
                                // TODO: add more special cases?
                                Thread.Sleep(2000);
                                break;
                        }
                    }
                    Thread.Sleep(50);
                }
                else
                {
                    Console.WriteLine("League client is closed.");
                    Thread.Sleep(1000);
                }
            }
        }

        private static void hanldeEndGame()
        {
            string[] currentTeam = clientRequest(leagueAuth, "GET", "lol-end-of-game/v1/eog-stats-block", "");
            //Console.WriteLine(currentTeam[1]);

            if (currentTeam[0] == "200")
            {
                string currentGameId = currentTeam[1].Split("\"gameId\":")[1].Split(',')[0];
                if (lastGameId == currentGameId)
                {
                    return;
                }
                else
                {
                    lastGameId = currentGameId;
                }

                if (currentPlayerId == "0")
                {
                    currentPlayerId = currentTeam[1].Split("\"localPlayer\"")[1].Split("\"summonerId\":")[1].Split(',')[0];
                }

                string[] teams = currentTeam[1].Split("\"teams\"")[1].Split(",\"timeUntilNextFirstWinBonus\"")[0].Split("},{\"fullId\":");
                foreach (var team in teams)
                {
                    string[] players = team.Split("},{");
                    foreach (var player in players)
                    {
                        string playerName = player.Split("summonerName\":\"")[1].Split('"')[0];
                        string playerId = player.Split("summonerId\":")[1].Split(',')[0];


                        if (currentPlayerId == playerId) {
                            Console.WriteLine(playerName + " is the current account, ignoring");
                        }
                        else if (friendsIds.Contains(playerId))
                        {
                            Console.WriteLine(playerName + " is a friend, ignoring");
                        }
                        else
                        {
                            // INAPPROPRIATE_NAME,LEAVING_AFK,ASSISTING_ENEMY_TEAM,THIRD_PARTY_TOOLS,LEAVING_AFK,ASSISTING_ENEMY_TEAM,THIRD_PARTY_TOOLS
                            string reportReason = "NEGATIVE_ATTITUDE,VERBAL_ABUSE,HATE_SPEECH";
                            clientRequest(leagueAuth, "POST", "lol-end-of-game/v2/player-complaints", '{' + "\"gameId\":" + currentGameId + ",\"offenses\":\"" + reportReason + "\",\"reportedSummonerId\":" + playerId + '}');
                            Console.WriteLine(playerName + " is a being reported for " + reportReason);
                        }
                    }
                }
                Console.WriteLine("------------------");
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

        private static string[] clientRequest(string[] leagueAuth, string method, string url, string body)
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
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    // Send POST data when doing a post request
                    if (method == "POST" || method == "PUT" || method == "PATCH")
                    {
                        string postData = body;
                        byte[] byteArray = Encoding.UTF8.GetBytes(postData);
                        request.Content = new ByteArrayContent(byteArray);
                    }

                    // Get the response
                    HttpResponseMessage response = client.SendAsync(request).Result;

                    // If the response is null (League client closed?)
                    if (response == null)
                    {
                        string[] outputDef = { "999", "" };
                        return outputDef;
                    }

                    // Get the HTTP status code
                    int statusCode = (int)response.StatusCode;
                    string statusString = statusCode.ToString();

                    // Get the body
                    string responseFromServer = response.Content.ReadAsStringAsync().Result;

                    // Clean up the response
                    response.Dispose();

                    // Return content
                    string[] output = { statusString, responseFromServer };
                    return output;
                }
            }
            catch
            {
                // If the URL is invalid (League client closed?)
                string[] output = { "999", "" };
                return output;
            }
        }
    }
}
