using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

/*
 * Mod by rexxar.
 * 
 * As usual, you're free to use this mod, dissect, reverse engineer, print and set it on fire,
 * so long as you give credit where it's due.
 * 
 * Simplistic server linking mostly to prove a point that it can be done in a mod. If you think
 * it's neat, you can buy me some caffeine at https://paypal.me/rexxar
 * 
 * This mod works by using the clients as messengers. Since servers can't talk to each other, we
 * serialize the grid and some faction data, timestamp it, sign it with a password, then calculate
 * the MD5 hash. This data is then sent to the client and stored on disk locally. When the client
 * spawns into the target server, it sends the hashed grid data back to the server, which verifies
 * it hasn't been tampered with.
 * 
 * This solution isn't 100% foolproof, but it's more than secure enough for this task.
 * 
 * In order to get around faction limitations, factions are recreated on the target server. We kind
 * of implicitly trust clients here, if they say they were in faction [ASD] then we believe them
 * and just add them to faction on the target server.
 */

namespace ServerLinkMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class LinkModCore : MySessionComponentBase
    {
        private const string HELP_TEXT = "Use !join to find a server to join, then '!join #' to join that server. !hub will take you back to the hub. !countown will hide the countdown timer.";
        private const string MODERATOR_HELP = HELP_TEXT + " '!spectate #' will take you to a match server without your ship, only available to moderators.";
        private const string ADMIN_HELP = MODERATOR_HELP + " !endjoin ends the join timer. !endmatch ends the match timer.";
        private static bool _init;
        private static bool _playerInit;
        public static bool Debug;
        public static LinkModCore Instance;

        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private readonly Random _random = new Random();
        private Timer _cleanupTimer;
        private bool _countdown = true;

        private bool _lobbyRunning;
        private Timer _lobbyTimer;
        private bool _matchRunning;
        private Timer _matchTimer;
        private int _updateCount;
        public DateTime? LobbyTime;
        public DateTime MatchStart;
        public DateTime? MatchTime;
        public Dictionary<int, ServerItem> Servers = new Dictionary<int, ServerItem>();
        public Dictionary<ulong, IMyCubeGrid> PlayerGrids = new Dictionary<ulong, IMyCubeGrid>();
        public Dictionary<int, NodeItem> Nodes = new Dictionary<int, NodeItem>();

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                _updateCount++;

                if (!_init)
                    Initialize();

                if (!_playerInit && MyAPIGateway.Session?.Player?.Character != null)
                {
                    _playerInit = true;
                    if (MyAPIGateway.Utilities.FileExistsInLocalStorage("Ship.bin", typeof(LinkModCore)))
                    {
                        BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage("Ship.bin", typeof(LinkModCore));
                        int count = reader.ReadInt32();
                        byte[] bytes = reader.ReadBytes(count);

                        Logging.Instance.WriteLine($"Sending grid parts: {count} bytes.");

                        Communication.SegmentAndSend(Communication.MessageType.ServerGridPart, bytes, MyAPIGateway.Session.Player.SteamUserId);
                    }
                    else
                    {
                        var character = MyAPIGateway.Session.Player.Character;
                        var pos = Utilities.RandomPositionFromPoint(character.GetPosition(), 500000);
                        var newPos = MyAPIGateway.Entities.FindFreePlace(pos, 5f);
                        character.SetPosition(newPos??pos);
                    }
                    //if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("Greeting.cfm", typeof(LinkModCore)))
                    //{
                    //    MyAPIGateway.Utilities.ShowMissionScreen("ServerLink",
                    //                                             "",
                    //                                             null,
                    //                                             "Welcome to the server link demo! Important rules and explanations:\r\n" +
                    //                                             "Pistons and rotors are prohibited.\r\n" +
                    //                                             "Grids are limited to 5k blocks.\r\n" +
                    //                                             "Grids in the hub will always be static, and all weapons are disabled.\r\n" +
                    //                                             "Grids in the hub MUST be owned! Unowned grids and grids beloning to offline players will be deleted every 10 minutes.\r\n\r\n" +
                    //                                             "Hub server provided by Neimoh of Galaxy Strike Force\r\n" +
                    //                                             "Match server #1 provided by Franky500 of Frankys Space\r\n" +
                    //                                             "Be sure to visit their regular servers!\r\n" +
                    //                                             "All other servers provided by X_Wing_Ian\r\n\r\n" +
                    //                                             "Use !join to get a list of servers you can join. Use !help for a full list of commands you can use.\r\n\r\n\r\n" +
                    //                                             "Enjoy!\r\n" +
                    //                                             "-rexxar",
                    //                                             null,
                    //                                             "Close");
                    //    var w = MyAPIGateway.Utilities.WriteFileInLocalStorage("Greeting.cfm", typeof(LinkModCore));
                    //    w.Write("true");
                    //    w.Flush();
                    //    w.Close();
                    //}
                    //else if (!Settings.Instance.Hub && !Exempt.Contains(MyAPIGateway.Session.Player.SteamUserId))
                    //{
                    //    MyAPIGateway.Utilities.ShowMessage("System", "You shouldn't be here!");
                    //    Communication.RedirectClient(MyAPIGateway.Session.Player.SteamUserId, Utilities.ZERO_IP);
                    //}
                }

                if (MyAPIGateway.Session.Player != null)
                {
                    if (LobbyTime.HasValue && LobbyTime > DateTime.UtcNow)
                    {
                        IMyHudObjectiveLine line = MyAPIGateway.Utilities.GetObjectiveLine();
                        line.Title = "Match starting in:";
                        line.Objectives.Clear();
                        line.Objectives.Add((DateTime.UtcNow - LobbyTime.Value).ToString(@"mm\:ss"));
                        if (_countdown && !line.Visible)
                            line.Show();
                    }
                    else
                    {
                        if (MatchTime.HasValue && MatchTime >= DateTime.UtcNow)
                        {
                            IMyHudObjectiveLine line = MyAPIGateway.Utilities.GetObjectiveLine();
                            line.Title = "Match ending in:";
                            line.Objectives.Clear();
                            line.Objectives.Add((DateTime.UtcNow - MatchTime.Value).ToString(@"mm\:ss"));
                            if (_countdown && !line.Visible)
                                line.Show();
                        }
                    }
                }

                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    if (Settings.Instance == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage("LinkMod", "Settings not defined on this server! Link mod will not work!");
                        MyAPIGateway.Utilities.SendMessage("Settings not defined on this server! Link mod will not work!");
                        return;
                    }

                    if (Settings.Instance.Hub && Settings.Instance.HubEnforcement)
                    {
                        if (_updateCount % 10 == 0)
                            ProcessEnforcement();
                    }
                    else
                    {
                        if (Settings.Instance.NodeEnforcement && _updateCount % 120 == 0)
                            ProcessCleanup();

                        if (_lobbyRunning)
                        {
                            var entities = new HashSet<IMyEntity>();
                            MyAPIGateway.Entities.GetEntities(entities);
                            foreach (var entity in entities)
                            {
                                var grid = entity as IMyCubeGrid;
                                if (grid?.Physics !=null)
                                {
                                    entity.Physics.LinearVelocity = Vector3D.ClampToSphere(entity.Physics.LinearVelocity, 10);
                                }
                            }
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"Exception during update:\r\n{ex}");
            }
        }

        public void TryStartLobby()
        {
            if (!_lobbyRunning && !_matchRunning && !Settings.Instance.Hub)
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                if (players.Count == 0)
                    return;

                _lobbyRunning = true;
                _lobbyTimer.Start();

                MatchStart = DateTime.UtcNow;
                Logging.Instance.WriteLine("Starting lobby");
            }
        }

        public void HandleChatCommand(ulong steamId, string command)
        {
            MyPromoteLevel level = MyAPIGateway.Session.GetUserPromoteLevel(steamId);
            Logging.Instance.WriteLine($"Got chat command from {steamId} : {command}");

            if (command.Equals("!join", StringComparison.CurrentCultureIgnoreCase))
            {
                if (!Settings.Instance.Hub)
                {
                    Communication.SendServerChat(steamId, "Join commands are not valid in battle servers!");
                    return;
                }
                Communication.SendServerChat(steamId, $"There are {Nodes.Count} battle servers. Please select the one you want by sending '!join [number]'");
                List<int> availableServers = (from server in Nodes.Values where server.CanJoin select server.Index + 1).ToList();

                if (availableServers.Count < Nodes.Count)
                    Communication.SendServerChat(steamId, $"These servers are ready for matches: {string.Join(", ", availableServers)}");
                else
                    Communication.SendServerChat(steamId, $"All {Nodes.Count} servers are available for new matches!");

                return;
            }

            if (command.StartsWith("!join", StringComparison.CurrentCultureIgnoreCase))
            {
                int ind = command.IndexOf(" ");
                if (ind == -1)
                {
                    Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                    return;
                }

                string numtex = command.Substring(ind);
                NodeItem node;
                int num;
                if (!int.TryParse(numtex, out num))
                {
                    Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                    return;
                }

                if (!Nodes.TryGetValue(num - 1, out node))
                {
                    Communication.SendServerChat(steamId, $"Couldn't find server {num}");
                    return;
                }

                if (!node.CanJoin)
                {
                    Communication.SendServerChat(steamId, "Sorry, this server is not open to new members. Please try another.");
                    return;
                }

                IMyPlayer player = Utilities.GetPlayerBySteamId(steamId);

                var block = player?.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
                IMyCubeGrid grid = block?.CubeGrid;
                if (grid == null)
                {
                    Communication.SendServerChat(steamId, "Can't find your ship. Make sure you're seated in the ship you want to take with you.");
                    return;
                }

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

                if (blocks.Count > Settings.Instance.MaxBlockCount)
                {
                    Communication.SendServerChat(steamId, $"Your ship has {blocks.Count} blocks. The limit for this server is {Settings.Instance.MaxBlockCount}");
                    return;
                }

                byte[] payload = Utilities.SerializeAndSign(grid, Utilities.GetPlayerBySteamId(steamId), block.Position);
                Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, steamId);

                node.Join(steamId);

                var timer = new Timer(10000);
                timer.AutoReset = false;
                timer.Elapsed += (a, b) => MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                timer.Start();
                return;
            }

            if (command.Equals("!hub", StringComparison.CurrentCultureIgnoreCase))
            {
                if (Settings.Instance.Hub)
                {
                    Communication.SendServerChat(steamId, "You're already in the hub!");
                    return;
                }
                else if(Settings.Instance.ReturnShip)
                {
                    var player = Utilities.GetPlayerBySteamId(steamId);
                    var block = player?.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
                    IMyCubeGrid grid = block?.CubeGrid;

                    if (grid == null)
                        PlayerGrids.TryGetValue(steamId, out grid);

                    if (grid != null)
                    {
                        byte[] payload = Utilities.SerializeAndSign(grid, player, block?.Position ?? Vector3I.Zero);
                        Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, steamId);
                    }
                }
                Communication.RedirectClient(steamId, Settings.Instance.HubIP);
                return;
            }

            if (level >= MyPromoteLevel.Moderator)
            {
                if (command.StartsWith("!spectate", StringComparison.CurrentCultureIgnoreCase))
                {
                    int ind = command.IndexOf(" ");
                    if (ind == -1)
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    string numtex = command.Substring(ind);
                    NodeItem node;
                    int num;
                    if (!int.TryParse(numtex, out num))
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    if (!Nodes.TryGetValue(num - 1, out node))
                    {
                        Communication.SendServerChat(steamId, $"Couldn't find server {num}");
                        return;
                    }

                    MyAPIGateway.Utilities.DeleteFileInLocalStorage("Ship.bin", typeof(LinkModCore));
                    Communication.RedirectClient(steamId, node.IP);
                    return;
                }
            }

            if (level >= MyPromoteLevel.Admin)
            {
                if (command.Equals("!endjoin", StringComparison.CurrentCultureIgnoreCase))
                {
                    _lobbyTimer.Stop();
                    _lobbyRunning = false;
                    _matchRunning = true;
                    Communication.SendNotification(0, "MATCH START!", MyFontEnum.Green);
                    Logging.Instance.WriteLine("Starting match");
                    _matchTimer.Start();
                    return;
                }

                if (command.Equals("!endmatch", StringComparison.CurrentCultureIgnoreCase))
                {
                    _matchTimer.Stop();
                    _matchRunning = false;
                    Communication.SendNotification(0, "MATCH OVER!", MyFontEnum.Red, 10000);
                    Logging.Instance.WriteLine("Ending match");
                    Utilities.ScrubServer();
                    return;
                }

                if (command.Equals("!reload", StringComparison.CurrentCultureIgnoreCase))
                {
                    Settings.LoadSettings();
                    Communication.SendServerChat(steamId, "Okay.");
                    return;
                }

                if (command.Equals("!save", StringComparison.CurrentCultureIgnoreCase))
                {
                    Settings.SaveSettings();
                    Communication.SendServerChat(steamId, "Okay.");
                    return;
                }

                if (command.StartsWith("!reset", StringComparison.CurrentCultureIgnoreCase))
                {
                    int ind = command.IndexOf(" ");
                    if (ind == -1)
                    {
                        foreach(var s in Nodes.Values)
                            s.Reset();
                        Communication.SendServerChat(steamId, "Reset all servers");
                        return;
                    }

                    string numtex = command.Substring(ind);
                    NodeItem node;
                    int num;
                    if (!int.TryParse(numtex, out num))
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    if (Nodes.TryGetValue(num -1, out node))
                    {
                        node.Reset();
                        Communication.SendServerChat(steamId, $"Reset server {num}");
                    }
                }
            }

            if (command.Equals("!help", StringComparison.CurrentCultureIgnoreCase))
            {
                if (level >= MyPromoteLevel.Admin)
                    Communication.SendServerChat(steamId, ADMIN_HELP);
                else
                {
                    if (level >= MyPromoteLevel.Moderator)
                        Communication.SendServerChat(steamId, MODERATOR_HELP);
                    else
                        Communication.SendServerChat(steamId, HELP_TEXT);
                }
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            Communication.UnregisterHandlers();
            _cleanupTimer?.Stop();
            _lobbyTimer?.Stop();
            _matchTimer?.Stop();
            Logging.Instance.Close();
        }

        private void Initialize()
        {
            Instance = this;
            _init = true;
            Logging.Instance.WriteLine("LinkMod initialized.");
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            Communication.RegisterHandlers();

            foreach (MyDefinitionBase def in MyDefinitionManagerBase.Static.GetAllDefinitions<MyDefinitionBase>())
            {
                var c = def as MyComponentDefinition;
                if (c == null)
                    continue;
                c.DropProbability = 0;
            }

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                Settings.LoadSettings();

                int index = 0;
                foreach (var entry in Settings.Instance.Nodes.Dictionary)
                    Nodes.Add(index++, new NodeItem(index, entry.Key, entry.Value));
                
                _lobbyTimer = new Timer(Settings.Instance.JoinTime * 60 * 1000);
                _lobbyTimer.AutoReset = false;
                _lobbyTimer.Elapsed += LobbyTimer_Elapsed;

                _matchTimer = new Timer(Settings.Instance.BattleTime * 60 * 1000);
                _matchTimer.AutoReset = false;
                _matchTimer.Elapsed += MatchTimer_Elapsed;

                if (Settings.Instance.Hub && Settings.Instance.HubEnforcement)
                {
                    _cleanupTimer = new Timer(10 * 60 * 1000);
                    _cleanupTimer.Elapsed += CleanupTimer_Elapsed;
                    _cleanupTimer.Start();
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, HubDamage);
                }
                else
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, BattleDamage);
            }
        }

        private void BattleDamage(object target, ref MyDamageInformation info)
        {
            if (target is IMyCharacter || Instance._lobbyRunning)
                info.Amount = 0;
        }

        private void HubDamage(object target, ref MyDamageInformation info)
        {
            info.Amount = 0;
        }

        private void CleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var toRemove = new MyConcurrentHashSet<IMyEntity>();
            var entities = new HashSet<IMyEntity>();
            var players = new List<IMyPlayer>();

            MyAPIGateway.Entities.GetEntities(entities);
            MyAPIGateway.Players.GetPlayers(players);

            MyAPIGateway.Parallel.ForEach(entities, entity =>
                                                    {
                                                        var grid = entity as IMyCubeGrid;
                                                        if (grid == null)
                                                            return;

                                                        if (grid.BigOwners.Count == 0)
                                                        {
                                                            toRemove.Add(entity);
                                                            return;
                                                        }

                                                        if (!grid.BigOwners.Any(o => players.Any(p => p.IdentityId == o)))
                                                            toRemove.Add(entity);
                                                    });

            if (toRemove.Any())
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                          {
                                                              foreach (IMyEntity ent in toRemove)
                                                                  ent.Close();

                                                              Communication.SendServerChat(0, $"Removed {toRemove.Count} grids without online owners");
                                                          });
            }
        }

        private void MatchTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _matchRunning = false;
            Communication.SendNotification(0, "MATCH OVER!", MyFontEnum.Red, 10000);
            Logging.Instance.WriteLine("Ending match");
            Utilities.ScrubServer();
        }

        private void LobbyTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _lobbyRunning = false;
            _matchRunning = true;
            Communication.SendNotification(0, "MATCH START!", MyFontEnum.Green, 5000);
            Logging.Instance.WriteLine("Starting match");
            _matchTimer.Start();
        }

        private void MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("!countdown", StringComparison.CurrentCultureIgnoreCase))
            {
                sendToOthers = false;
                _countdown = !_countdown;
                MyAPIGateway.Utilities.GetObjectiveLine().Hide();
                return;
            }

            if (messageText.StartsWith("!"))
            {
                sendToOthers = false;
                Communication.SendClientChat(messageText);
            }
        }

        /// <summary>
        ///     Enforces grid rules in hub
        /// </summary>
        private void ProcessEnforcement()
        {
            _entityCache.Clear();
            MyAPIGateway.Entities.GetEntities(_entityCache);

            MyAPIGateway.Parallel.ForEach(_entityCache, entity =>
                                                        {
                                                            var grid = entity as IMyCubeGrid;
                                                            if (grid?.Physics == null)
                                                                return;

                                                            ulong id = 0;
                                                            if (grid.BigOwners.Any())
                                                                id = MyAPIGateway.Players.TryGetSteamId(grid.BigOwners[0]);

                                                            MyPromoteLevel level = MyAPIGateway.Session.GetUserPromoteLevel(id);
                                                            if (level < MyPromoteLevel.Admin)
                                                            {
                                                                if (grid.Physics.LinearVelocity.LengthSquared() > 0.1)
                                                                    MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Physics.LinearVelocity = Vector3.Zero);
                                                                if (grid.Physics.AngularVelocity.LengthSquared() > 0.1)
                                                                    MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Physics.AngularVelocity = Vector3.Zero);

                                                                if (!grid.IsStatic)
                                                                    MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.IsStatic = true);
                                                            }

                                                            var blocks = new List<IMySlimBlock>();
                                                            grid.GetBlocks(blocks);

                                                            var toRaze = new HashSet<IMySlimBlock>();

                                                            foreach (IMySlimBlock slim in blocks)
                                                            {
                                                                IMyCubeBlock block = slim?.FatBlock;
                                                                if (block == null)
                                                                    continue;

                                                                if (block is IMyAttachableTopBlock)
                                                                    toRaze.Add(slim);

                                                                if (block is IMyMechanicalConnectionBlock)
                                                                    toRaze.Add(slim);

                                                                var gun = block as IMyUserControllableGun;
                                                                if (gun != null)
                                                                {
                                                                    if (gun.Enabled)
                                                                        gun.Enabled = false;
                                                                }

                                                                var warhead = block as IMyWarhead;
                                                                if (warhead != null)
                                                                {
                                                                    if (!warhead.GetValueBool("Safety"))
                                                                        warhead.SetValueBool("Safety", true);
                                                                }
                                                            }

                                                            if (toRaze.Any())
                                                                Communication.SendServerChat(id, $"The ship {grid.DisplayName} has violated block rules. Some blocks have been removed to enforce rules.");

                                                            blocks.Clear();
                                                            grid.GetBlocks(blocks);
                                                            if (blocks.Count > Settings.Instance.MaxBlockCount)
                                                            {
                                                                Communication.SendServerChat(id, $"The ship {grid.DisplayName} has gone over the max block count of {Settings.Instance.MaxBlockCount}. Blocks will be removed to enforce rules.");

                                                                toRaze.UnionWith(blocks.GetRange(Settings.Instance.MaxBlockCount - 1, blocks.Count - Settings.Instance.MaxBlockCount - 1));
                                                            }

                                                            if (toRaze.Any())
                                                            {
                                                                List<Vector3I> l = toRaze.Select(b => b.Position).ToList();
                                                                toRaze.Clear();
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.RazeBlocks(l));
                                                            }
                                                        });
        }

        /// <summary>
        ///     Cleans up match servers during the match
        /// </summary>
        private void ProcessCleanup()
        {
            _entityCache.Clear();
            MyAPIGateway.Entities.GetEntities(_entityCache);

            MyAPIGateway.Parallel.ForEach(_entityCache, entity =>
                                                        {
                                                            if (entity.Closed || entity.MarkedForClose)
                                                                return;

                                                            var floating = entity as IMyFloatingObject;
                                                            if (floating != null)
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => floating.Close());
                                                                return;
                                                            }

                                                            var grid = entity as IMyCubeGrid;
                                                            if (grid?.Physics == null)
                                                                return;

                                                            var blocks = new List<IMySlimBlock>();
                                                            grid.GetBlocks(blocks);

                                                            if (blocks.Count < 5)
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                                                                return;
                                                            }

                                                            if (blocks.All(s => s.FatBlock == null))
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                                                                return;
                                                            }

                                                            ulong id = 0;
                                                            if (grid.BigOwners.Count > 0)
                                                                id = MyAPIGateway.Players.TryGetSteamId(grid.BigOwners[0]);

                                                            Vector3D pos = grid.GetPosition();
                                                            if (pos.LengthSquared() > (Settings.Instance.SpawnRadius + 1000) * (Settings.Instance.SpawnRadius + 1000))
                                                            {
                                                                if (id != 0)
                                                                    Communication.SendNotification(id, "You have left the battle zone! Turn back now or face consequences!");
                                                            }

                                                            if (pos.LengthSquared() > (Settings.Instance.SpawnRadius + 2000) * (Settings.Instance.SpawnRadius + 2000))
                                                            {
                                                                                                              IMySlimBlock b = blocks[_random.Next(blocks.Count)];
                                                                var p = b.GetPosition();
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                                                                                                              Utilities.Explode(p, 7000f, 22.5, grid, MyExplosionTypeEnum.WARHEAD_EXPLOSION_50); });

                                                            }
                                                        });
        }
    }
}
