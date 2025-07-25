using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using RimWorld;
using Verse;

namespace PawnPy
{
    public class PythonCommunication : GameComponent
    {
        public static PythonCommunication Instance;
        private Thread serverThread;
        private TcpListener listener;
        private bool running = true;
        private readonly object commandLock = new object();
        private Dictionary<int, PawnCommand> commandQueue = new Dictionary<int, PawnCommand>();
        private const int Port = 5000;

        // For state updates and pawn tracking
        private readonly object pawnLock = new object();
        public HashSet<int> ControlledPawns = new HashSet<int>();
        private ConcurrentQueue<string> stateQueue = new ConcurrentQueue<string>();

        public PythonCommunication(Game game)
        {
            Instance = this;
            StartServer();
        }

        private void StartServer()
        {
            Log.Message("[PawnPy] Starting server thread...");
            serverThread = new Thread(() =>
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, Port);
                    listener.Start();
                    Log.Message($"[PawnPy] Server started on port {Port}");

                    while (running)
                    {
                        if (listener.Pending())
                        {
                            TcpClient client = listener.AcceptTcpClient();
                            Log.Message("[PawnPy] Client connected");
                            ThreadPool.QueueUserWorkItem(HandleClient, client);
                        }
                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[PawnPy] Server crashed: {ex}");
                }
            })
            {
                IsBackground = true,
                Name = "PawnPyServer"
            };
            serverThread.Start();
        }

        private void HandleClient(object state)
        {
            TcpClient client = (TcpClient)state;

            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    Log.Message($"[PawnPy] Received: {json}");

                    // Handle "GET_PAWNS" request
                    if (json == "GET_PAWNS")
                    {
                        string pawnList = GetPawnListJSON();
                        byte[] data = Encoding.UTF8.GetBytes(pawnList);
                        stream.Write(data, 0, data.Length);
                        return;
                    }

                    // Handle "SUBSCRIBE_PAWN" request
                    if (json.StartsWith("{\"command\":\"SUBSCRIBE_PAWN\""))
                    {
                        var subCommand = JsonConvert.DeserializeObject<SubscribeCommand>(json);
                        lock (pawnLock)
                        {
                            ControlledPawns.Add(subCommand.pawn_id);
                        }
                        stream.Write(Encoding.UTF8.GetBytes("SUBSCRIBED"), 0, 10);
                        return;
                    }

                    // Handle "GET_STATE_UPDATES" request
                    if (json == "GET_STATE_UPDATES")
                    {
                        // Send all queued state updates
                        while (stateQueue.TryDequeue(out string stateJson))
                        {
                            byte[] data = Encoding.UTF8.GetBytes(stateJson + "\n");
                            stream.Write(data, 0, data.Length);
                        }
                        stream.Write(Encoding.UTF8.GetBytes("END_UPDATE"), 0, 10);
                        return;
                    }

                    // Handle commands
                    try
                    {
                        var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                        string commandType = jObject["CommandType"]?.ToString();

                        PawnCommand cmd = null;
                        switch (commandType)
                        {
                            case "MoveTo":
                                cmd = jObject.ToObject<MoveToCommand>();
                                break;
                            case "Attack":
                                cmd = jObject.ToObject<AttackCommand>();
                                break;
                            case "Interact":
                                cmd = jObject.ToObject<InteractCommand>();
                                break;
                            case "UseItem":
                                cmd = jObject.ToObject<UseItemCommand>();
                                break;
                            default:
                                Log.Error($"[PawnPy] Unknown command type: {commandType}");
                                break;
                        }

                        if (cmd != null)
                        {
                            lock (commandLock)
                            {
                                commandQueue[cmd.PawnID] = cmd;
                            }
                            stream.Write(Encoding.UTF8.GetBytes("ACK"), 0, 3);
                        }
                        else
                        {
                            stream.Write(Encoding.UTF8.GetBytes("ERROR: Invalid command"), 0, 22);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[PawnPy] Command error: {ex}");
                        stream.Write(Encoding.UTF8.GetBytes("ERROR"), 0, 5);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnPy] Client error: {ex}");
            }
            finally
            {
                client.Close();
            }
        }

        public override void GameComponentTick()
        {
            if (Find.CurrentMap?.mapPawns == null) return;

            List<PawnCommand> commandsToProcess;
            lock (commandLock)
            {
                commandsToProcess = commandQueue.Values.ToList();
                commandQueue.Clear();
            }

            foreach (var cmd in commandsToProcess)
            {
                Pawn targetPawn = Find.CurrentMap.mapPawns.AllPawns
                    .FirstOrDefault(p => p.thingIDNumber == cmd.PawnID && p.Spawned);

                if (targetPawn != null)
                {
                    try
                    {
                        cmd.Execute(targetPawn);
                        Log.Message($"[PawnPy] Executed {cmd.GetType().Name} on {targetPawn.Label}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[PawnPy] Execution failed: {ex}");
                    }
                }
                else
                {
                    Log.Warning($"[PawnPy] Pawn not found: ID {cmd.PawnID}");
                }
            }
        }

        public string GetPawnListJSON()
        {
            if (Find.CurrentMap?.mapPawns == null) return "[]";

            return JsonConvert.SerializeObject(
                Find.CurrentMap.mapPawns.AllPawns
                    .Where(p => p.Spawned && p.Faction == Faction.OfPlayer)
                    .Select(p => new {
                        id = p.thingIDNumber,
                        name = p.LabelShort,
                        position = new { x = p.Position.x, z = p.Position.z },
                        health = p.health.summaryHealth?.SummaryHealthPercent ?? 1f
                    })
            );
        }

        public void UpdatePawnState(Pawn pawn)
        {
            lock (pawnLock)
            {
                if (!ControlledPawns.Contains(pawn.thingIDNumber)) return;
            }

            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                // Remove invalid pawns
                lock (pawnLock)
                {
                    ControlledPawns.Remove(pawn.thingIDNumber);
                }
                return;
            }

            var state = new
            {
                id = pawn.thingIDNumber,
                position = new { x = pawn.Position.x, z = pawn.Position.z },
                health = pawn.health.summaryHealth?.SummaryHealthPercent ?? 1f,
                needs = pawn.needs?.AllNeeds?
                    .Where(n => n != null)
                    .ToDictionary(n => n.GetType().Name, n => n.CurLevel) ?? new Dictionary<string, float>(),
                skills = pawn.skills?.skills?
                    .ToDictionary(s => s.def.defName, s => s.Level) ?? new Dictionary<string, int>()
            };

            stateQueue.Enqueue(JsonConvert.SerializeObject(state));
        }

        public void StopServer()
        {
            running = false;
            listener?.Stop();
        }
    }

    // Helper class for subscription command
    public class SubscribeCommand
    {
        public string command { get; set; }
        public int pawn_id { get; set; }
    }
}