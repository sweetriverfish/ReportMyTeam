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

namespace ReportMyTeam
{
    class Program
    {
        public static string lastGameId = "0";
        public static string currentPlayerId = "0";
        public static List<string> friendsIds = new List<string>();
        public static bool foundFriends = false;

        private static void Main()
        {
            Console.Write("Initializing..");

            // Set console title
            Console.Title = "ReportMyTeam";

            // Set output to UTF8
            Console.OutputEncoding = Encoding.UTF8;

            var taskPhase = new Task(findPhase);
            taskPhase.Start(); 
            var taskLeagueAlive = new Task(LCU.CheckIfLeagueClientIsOpenTask);
            taskLeagueAlive.Start();

            var tasks = new[] { taskPhase, taskLeagueAlive };
            Task.WaitAll(tasks);

            Console.ReadKey();
        }

        public static void resetData()
        {
            // if client was restarted, reset data
            currentPlayerId = "0";

            friendsIds.Clear();
            foundFriends = false;
        }

        public static void getFriendsIds()
        {
            string[] friendsList = LCU.clientRequest("GET", "lol-chat/v1/friends");
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
            while (!LCU.isClientOn || !foundFriends)
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
                if (LCU.isClientOn)
                {
                    string[] gameSession = LCU.clientRequest("GET", "lol-gameflow/v1/session");

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
            string[] currentTeam = LCU.clientRequest("GET", "lol-end-of-game/v1/eog-stats-block");

            if (currentTeam[0] != "200")
            {
                // Handle error
                return;
            }

            string currentGameId = currentTeam[1].Split("\"gameId\":")[1].Split(',')[0];
            if (lastGameId == currentGameId)
            {

                // Already processed this game
                return;
            }
            lastGameId = currentGameId;

            if (currentPlayerId == "0")
            {
                currentPlayerId = currentTeam[1].Split("\"localPlayer\"")[1].Split("\"summonerId\":")[1].Split(',')[0];
            }

            string[] teams = currentTeam[1].Split("\"teams\"")[1].Split(",\"timeUntilNextFirstWinBonus\"")[0].Split("},{\"fullId\":");
            foreach (var team in teams)
            {
                string[] players = team.Split("},{");

                // find average level of a player in team and total kills count
                (float averageLevel, int teamKills) = GetTeamStats(players);

                foreach (var player in players)
                {
                    handlePlayer(player, averageLevel, teamKills, currentGameId);
                }
            }
            Console.WriteLine("------------------");
            
        }

        private static (float averageLevel, int teamKills) GetTeamStats(string[] players)
        {
            float averageLevel = 0;
            int teamKills = 0;
            foreach (var player in players)
            {
                int level = Int32.Parse(player.Split("LEVEL\":")[1].Split(',')[0]);
                int kills = Int32.Parse(player.Split("CHAMPIONS_KILLED\":")[1].Split(',')[0]);
                averageLevel += level;
                teamKills += kills;
            }
            averageLevel = averageLevel / players.Length;

            return (averageLevel, teamKills);
        }

        private static void handlePlayer(string player, float averageLevel, int teamKills, string currentGameId)
        {
            // parse some basic data about the player we are currently looping through
            string playerName = player.Split("summonerName\":\"")[1].Split('"')[0];
            string playerId = player.Split("summonerId\":")[1].Split(',')[0];

            // ignored certain players cause bias
            if (currentPlayerId == playerId)
            {
                Console.WriteLine(playerName + " is the current account, ignoring");
            }
            else if (friendsIds.Contains(playerId))
            {
                Console.WriteLine(playerName + " is a friend, ignoring");
            }
            else
            {
                // parse some data
                string playerPuuid = player.Split("puuid\":\"")[1].Split('"')[0];
                string isLeaver = player.Split("\"leaver\":")[1].Split(',')[0];
                int level = Int32.Parse(player.Split("LEVEL\":")[1].Split(',')[0]);
                int kills = Int32.Parse(player.Split("CHAMPIONS_KILLED\":")[1].Split(',')[0]);
                int deaths = Int32.Parse(player.Split("NUM_DEATHS\":")[1].Split(',')[0]);
                int assists = Int32.Parse(player.Split("ASSISTS\":")[1].Split(',')[0]);

                // make up some reasons
                int reasons = 0;
                string reportReason = "";

                // if is marked as afk by the system, or is 25% behind in levels compared to average level, or has <25% kp, report for afking
                float kp = (float)(kills + assists) / teamKills;
                if (isLeaver == "true" || (float)(level * 0.75) > averageLevel || kp < 0.25)
                {
                    reportReason += ",\"LEAVING_AFK\"";
                    reasons++;
                }

                // if has <0.5 kda, report for inting
                float kda = (float)(kills + assists) / deaths;
                if (kda < 0.5)
                {
                    reportReason += ",\"ASSISTING_ENEMY_TEAM\"";
                    reasons++;
                }

                // fill the reason with generic stuff related to toxicity cause everyone is toxic in this shitty game
                if (reasons == 2)
                {
                    reportReason = "\"NEGATIVE_ATTITUDE\"" + reportReason;
                }
                else if (reasons == 1)
                {
                    reportReason = "\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\"" + reportReason;
                }
                else
                {
                    reportReason = "\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"HATE_SPEECH\"";
                }

                // send the report
                string[] result = LCU.clientRequest("POST", "lol-end-of-game/v2/player-reports", '{' + "\"gameId\":" + currentGameId + ",\"categories\":[" + reportReason + "],\"offenderSummonerId\":" + playerId + ",\"offenderPuuid\":\"" + playerPuuid + "\"" + '}');

                if (result[0] == "200")
                {
                    Console.WriteLine(playerName + " is a being reported for " + reportReason);
                }
                else
                {
                    Console.WriteLine("Failed to report " + playerName);
                }
            }
        }
    }
}
