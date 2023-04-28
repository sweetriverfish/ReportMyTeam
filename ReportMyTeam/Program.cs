using System.Text;

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
            Console.WriteLine(currentTeam[1]);

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

                foreach (var player in players)
                {
                    handlePlayer(player, currentGameId);
                }
            }
            Console.WriteLine("------------------");
            
        }

        private static void handlePlayer(string player, string currentGameId)
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

                // make up some reasons
                string reportReason = "\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"LEAVING_AFK\",\"ASSISTING_ENEMY_TEAM\",\"HATE_SPEECH\",\"THIRD_PARTY_TOOLS\",\"INAPPROPRIATE_NAME\"";

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
