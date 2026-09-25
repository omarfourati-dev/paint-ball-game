using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Core.Matchmaking;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;
using Paintball.Core.Social;
using Paintball.Net.Accounts;
using Paintball.Net.Protocol;
using Paintball.Net.Simulation;

namespace Paintball.Net.Rooms
{
    /// <summary>Betriebskennzahlen (NFR-06, NFR-20).</summary>
    public sealed class ServerMetrics
    {
        public long MessagesIn;
        public long MessagesRejected;
        public long Logins;
        public long MatchesStarted;
        public long MatchesFinished;
        public long Reconnects;
        public long AfkKicks;
        public long FloodKicks;
        public double LastTickMs;
        public double MaxTickMs;
    }

    /// <summary>
    /// Autoritativer Spielserver (FR-22..FR-32, AR-06/07): Sitzungen, Login, Matchmaking,
    /// Räume und Nachrichten-Dispatch. Transport-Threads legen nur Nachrichten in eine
    /// Warteschlange; die gesamte Spiellogik läuft deterministisch im <see cref="Tick"/>.
    /// </summary>
    public sealed class GameServer
    {
        public const float TickRate = GameMatch.TickRate;
        public const float TickDt = GameMatch.TickDt;
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly string[] Phrases = BuildPhrases();

        private readonly ConcurrentQueue<Action> _inbox = new();
        private readonly Dictionary<int, Session> _sessions = new();
        private readonly Dictionary<string, Room> _rooms = new();
        private readonly MapCatalog _maps = new();
        private int _nextSessionId = 1;
        private int _mapRotation;
        /// <summary>
        /// Schnappschuss der Konten, die als „online“ gelten – authentifizierte Sitzung ODER (Fix Runde 1) nicht-Bot-Mitglied
        /// irgendeines Raums in jeder Phase (auch getrennt in der Reconnect-Gnadenfrist: ein solches Mitglied hat keine
        /// Sitzung mehr, aber <see cref="Room.FinishMatch"/> greift beim Matchende trotzdem auf sein Konto zu). Im Tick neu
        /// gebaut und nie in-place verändert – nur als Ganzes ersetzt (Task 6: <see cref="IsOnline"/> ist so threadsicher lesbar).
        /// </summary>
        private volatile HashSet<string> _onlineSnapshot = new();
        /// <summary>Erzwingt den nächsten Tick eine Neuberechnung von <see cref="_onlineSnapshot"/> (Fix Runde 1, Minor).</summary>
        private bool _onlineDirty = true;
        /// <summary>Spätestens zu diesem Spielzeitpunkt wird <see cref="_onlineSnapshot"/> neu gebaut, auch ohne <see cref="_onlineDirty"/>
        /// (fängt Raum-Beitritte/-Austritte ab, die keine Sitzungsänderung sind, z. B. Ablauf der Reconnect-Gnadenfrist).</summary>
        private float _nextOnlineBuild;

        public ServerOptions Options { get; }
        public AccountStore Accounts { get; }
        public ServerMetrics Metrics { get; } = new();
        public float Time { get; private set; }
        public long TickCount { get; private set; }
        public IReadOnlyCollection<Room> Rooms => _rooms.Values;
        public int SessionCount => _sessions.Count;
        public static IReadOnlyList<string> QuickChatPhrases => Phrases;

        /// <summary>
        /// Ist gerade eine authentifizierte Sitzung dieses Kontos verbunden – unabhängig davon, ob es in einem Raum ist?
        /// Threadsicher: liest einen im Tick ersetzten, unveränderlichen Schnappschuss (Task 6: Speicherbereinigung).
        /// </summary>
        public bool IsOnline(string playerId) => playerId != null && _onlineSnapshot.Contains(playerId);

        public GameServer(ServerOptions options, AccountStore accounts)
        {
            Options = options ?? new ServerOptions();
            Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        }

        private static string[] BuildPhrases()
        {
            var q = new QuickChatMessages();
            var list = new List<string>();
            foreach (QuickChatCategory c in Enum.GetValues(typeof(QuickChatCategory))) list.AddRange(q.GetByCategory(c));
            return list.ToArray();
        }

        public Room FindRoom(string code) => code != null && _rooms.TryGetValue(code.ToUpperInvariant(), out Room r) ? r : null;

        // ---------------- Transport-API (thread-sicher) ----------------

        public Session Connect(IClientSink sink, string remote, string playerId)
        {
            var session = new Session { Sink = sink, Remote = remote, Tokens = Options.MessageBurst, AccountId = playerId };
            lock (_sessions) session.Id = _nextSessionId++;
            _inbox.Enqueue(() => { _sessions[session.Id] = session; session.NextFloodCheck = Time + 1f; });
            return session;
        }

        public void Receive(Session session, string text)
        {
            if (session == null || session.Closed) return;
            if (text == null || text.Length > Options.MaxMessageBytes)
            {
                _inbox.Enqueue(() => { Metrics.MessagesRejected++; session.Send(Json.Error("too_large")); });
                return;
            }
            if (!session.TryConsumeToken()) return;
            _inbox.Enqueue(() => Handle(session, text));
        }

        public void Disconnect(Session session)
        {
            if (session == null) return;
            _inbox.Enqueue(() => DropSession(session));
        }

        /// <summary>Nach Namensänderung über die HTTP-API: Sitzungen und Profil aktualisieren.</summary>
        public void Renamed(string playerId)
        {
            _inbox.Enqueue(() =>
            {
                string name = Accounts.GetAccount(playerId)?.DisplayName;
                if (name == null) return;
                foreach (Session s in _sessions.Values)
                    if (s.AccountId == playerId && s.Authenticated) { s.Name = name; s.Send(ProfileJson(playerId)); }
            });
        }

        /// <summary>Trennt alle Sitzungen eines Kontos (Abmelden, Löschen).</summary>
        public void KickAccount(string playerId, string reason)
        {
            _inbox.Enqueue(() =>
            {
                foreach (Session s in _sessions.Values.ToList())
                    if (s.AccountId == playerId) { s.Sink.Close(reason); DropSession(s); }
            });
        }

        // ---------------- Tick ----------------

        public void Tick()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Time += TickDt;
            TickCount++;

            while (_inbox.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Console.Error.WriteLine("[GameServer] Nachricht fehlgeschlagen: " + ex.Message); }
            }

            // Nur neu bauen, wenn sich etwas geändert haben könnte (Sitzung), oder spätestens einmal pro Sekunde (fängt
            // Raum-Mitgliedschaftsänderungen ohne Sitzungsänderung ab, z. B. Ablauf der Reconnect-Gnadenfrist). Immer als
            // Ganzes ersetzt, nie in-place verändert: andere Threads (Wartungsdienst) lesen ihn so gefahrlos mit.
            if (_onlineDirty || Time >= _nextOnlineBuild)
            {
                var online = new HashSet<string>();
                foreach (Session s in _sessions.Values)
                    if (s.Authenticated && s.AccountId != null) online.Add(s.AccountId);
                foreach (Room room in _rooms.Values)
                    foreach (Member m in room.Members)
                        if (!m.IsBot && m.AccountId != null) online.Add(m.AccountId);
                _onlineSnapshot = online;
                _onlineDirty = false;
                _nextOnlineBuild = Time + 1f;
            }

            foreach (Session s in _sessions.Values.ToList())
            {
                s.Refill(Options.MessagesPerSecond * TickDt, Options.MessageBurst);
                if (Time < s.NextFloodCheck) continue;
                s.NextFloodCheck = Time + 1f;
                s.Violations = s.TakeDropped() > 0 ? s.Violations + 1 : Math.Max(0, s.Violations - 1);
                if (s.Violations >= Options.FloodViolationsToKick)
                {
                    Metrics.FloodKicks++;
                    s.Sink.Close("rate_limited");
                    DropSession(s);
                }
            }

            foreach (Room room in _rooms.Values.ToList())
            {
                try { room.Tick(Time); }
                catch (Exception ex) { Console.Error.WriteLine($"[GameServer] Raum {room.Code} fehlerhaft: {ex}"); }
                if (room.IsEmpty) _rooms.Remove(room.Code);
            }

            sw.Stop();
            Metrics.LastTickMs = sw.Elapsed.TotalMilliseconds;
            Metrics.MaxTickMs = Math.Max(Metrics.MaxTickMs * 0.999, Metrics.LastTickMs);
        }

        private void DropSession(Session s)
        {
            if (s.Closed && !_sessions.ContainsKey(s.Id)) return;
            s.Closed = true;
            _sessions.Remove(s.Id);
            _onlineDirty = true;   // Sitzung weg – der Online-Schnappschuss muss neu gebaut werden (Fix Runde 1)
            if (s.Room != null && s.Member != null) s.Room.OnDisconnected(s.Member, Time);
            s.Room = null;
            s.Member = null;
        }

        // ---------------- Dispatch ----------------

        private void Handle(Session s, string text)
        {
            if (s.Closed) return;
            Metrics.MessagesIn++;
            if (!Msg.TryParse(text, out Msg msg, out string error))
            {
                Metrics.MessagesRejected++;
                s.Send(Json.Error(error));
                return;
            }

            string type = msg.Type;
            if (type == "ping") { HandlePing(s, msg); return; }
            if (type == "hello") { HandleHello(s, msg); return; }
            if (!s.Authenticated) { s.Send(Json.Error("not_authenticated")); return; }

            switch (type)
            {
                case "in": HandleInput(s, msg); break;
                case "quick": HandleQuick(s, msg); break;
                case "create": HandleCreate(s, msg); break;
                case "join": HandleJoin(s, msg); break;
                case "leave": HandleLeave(s); break;
                case "team": HandleTeam(s, msg); break;
                case "ready": HandleReady(s, msg); break;
                case "start": HandleStart(s); break;
                case "config": HandleConfig(s, msg); break;
                case "bot": HandleBot(s, msg); break;
                case "chat": HandleChat(s, msg); break;
                case "emote": HandleEmote(s, msg); break;
                case "mark": HandleMark(s, msg); break;
                case "report": HandleReport(s, msg); break;
                case "loadout": HandleLoadout(s, msg); break;
                case "buy": HandleBuy(s, msg); break;
                case "profile": s.Send(ProfileJson(s.AccountId)); break;
                case "leaderboard": s.Send(LeaderboardJson(s.AccountId)); break;
                default:
                    Metrics.MessagesRejected++;
                    s.Send(Json.Error("unknown_type"));
                    break;
            }
        }

        private void HandlePing(Session s, Msg msg)
        {
            if (msg.TryNum("rtt", out double rtt)) s.RttMs = Math.Clamp(rtt, 0, 5000);
            if (msg.TryNum("loss", out double loss)) s.PacketLoss = Math.Clamp(loss, 0, 100);
            double clientTime = msg.Num("c", 0, double.MinValue, double.MaxValue);
            s.Send(Json.Write(w =>
            {
                w.WriteString("t", "pong");
                w.WriteNumber("c", clientTime);
                w.Num("st", Time, 3);
            }));
        }

        private void HandleHello(Session s, Msg msg)
        {
            PlayerAccount account = Accounts.GetAccount(s.AccountId);
            if (account == null || Accounts.NeedsName(s.AccountId))
            {
                s.Send(Json.Error("not_authenticated"));
                s.Sink.Close("unauthorized");
                DropSession(s);
                return;
            }
            Metrics.Logins++;

            // Altes Sitzungsobjekt desselben Kontos ersetzen (nur eine aktive Sitzung)
            foreach (Session other in _sessions.Values.ToList())
            {
                if (other == s || other.AccountId != account.PlayerId || !other.Authenticated) continue;
                other.Sink.Close("replaced");
                DropSession(other);
            }

            s.Authenticated = true;
            _onlineDirty = true;   // neu authentifiziert – der Online-Schnappschuss muss neu gebaut werden (Fix Runde 1)
            s.Name = account.DisplayName;
            s.Input = msg.Str("input", 8, "kbm") switch { "touch" => "touch", "pad" => "pad", _ => "kbm" };
            s.CrossPlay = msg.Bool("crossPlay", true);
            s.Lang = msg.Str("lang", 5, "de") == "en" ? "en" : "de";
            s.Platform = msg.Str("platform", 12, "web") switch
            {
                "android" => AppPlatform.Android,
                "ios" => AppPlatform.iOS,
                "windows" => AppPlatform.Windows,
                "mac" => AppPlatform.Mac,
                "linux" => AppPlatform.Linux,
                _ => AppPlatform.Web
            };

            s.Send(Json.Write(w =>
            {
                w.WriteString("t", "welcome");
                w.WriteString("account", account.PlayerId);
                w.WriteString("name", account.DisplayName);
                w.Num("serverTime", Time, 3);
                w.WritePropertyName("profile");
                WriteProfile(w, account.PlayerId);
            }));

            // Reconnect in laufendes Match (FR-27)
            foreach (Room room in _rooms.Values)
                if (room.State == RoomState.Match && room.TryReconnect(s, Time)) return;
        }

        private void HandleInput(Session s, Msg msg)
        {
            if (s.Room == null || s.Member == null) return;
            if (!msg.TryNum("s", out double seq) || !msg.TryNum("mx", out double mx) || !msg.TryNum("mz", out double mz)
                || !msg.TryNum("y", out double yaw) || !msg.TryNum("p", out double pitch))
            {
                s.Send(Json.Error("bad_input"));
                return;
            }
            int buttons = msg.Int("b", 0, 0, 1023);
            var frame = new PlayerInputFrame
            {
                Seq = (int)Math.Clamp(seq, 0, int.MaxValue),
                Move = new MoveInput
                {
                    MoveX = (float)Math.Clamp(mx, -1, 1),
                    MoveZ = (float)Math.Clamp(mz, -1, 1),
                    Yaw = (float)(yaw % (Math.PI * 2)),
                    Pitch = (float)Math.Clamp(pitch, -1.5, 1.5),
                    Jump = (buttons & 2) != 0,
                    Crouch = (buttons & 4) != 0,
                    Sprint = (buttons & 8) != 0
                },
                AimYaw = (float)(msg.Num("ay", yaw, -1e4, 1e4) % (Math.PI * 2)),
                AimPitch = (float)msg.Num("ap", pitch, -1.5, 1.5),
                Fire = (buttons & 1) != 0,
                Reload = (buttons & 16) != 0,
                Dash = (buttons & 32) != 0,
                UseItem = (buttons & 64) != 0
            };
            s.Room.HandleInput(s.Member, frame, Time);
        }

        // ---------------- Matchmaking & Räume ----------------

        private bool EnsureRoomCapacity(Session s)
        {
            if (_rooms.Count < Options.MaxRooms) return true;
            s.Send(Json.Error("server_full", "Alle Server sind ausgelastet – bitte gleich erneut versuchen."));
            return false;
        }

        private void HandleQuick(Session s, Msg msg)
        {
            if (!GameModes.TryParse(msg.Str("mode", 12, "tdm"), out GameMode mode) || mode == GameMode.Training)
            {
                s.Send(Json.Error("bad_mode"));
                return;
            }
            LeaveCurrent(s, abandoned: true);

            // FR-23: nach Modus, Cross-Play-Pool und MMR-Nähe; Fenster weitet sich mit der Wartezeit
            int mmr = Accounts.GetAccount(s.AccountId)?.Mmr ?? MmrCalculator.DefaultMmr;
            Room best = null;
            double bestDelta = double.MaxValue;
            foreach (Room r in _rooms.Values)
            {
                if (!r.IsQuick || r.Settings.Mode != mode || !(r.State == RoomState.Lobby || r.State == RoomState.Match) || !r.Accepts(s)) continue;
                if (r.State == RoomState.Match && (r.Match == null || r.Match.Phase == MatchPhase.Finished || !r.Members.Any(m => m.IsBot))) continue;
                double window = 250 + 40 * (Time - r.CreatedAt);
                double delta = Math.Abs(r.AverageMmr - mmr);
                if (delta > window) continue;
                if (r.State == RoomState.Lobby) delta -= 10000; // wartende Lobbys bevorzugen
                if (delta < bestDelta) { bestDelta = delta; best = r; }
            }

            if (best == null)
            {
                if (!EnsureRoomCapacity(s)) return;
                MatchSettings settings = MatchSettings.For(mode);
                settings.MapId = _maps.Get(_mapRotation++).Id;
                best = CreateRoom(settings, isPrivate: false, isQuick: true);
                best.BotSkill = Options.QuickMatchBotSkill;
            }
            best.AddHuman(s, Time);
            best.BroadcastLobby();
        }

        private Room CreateRoom(MatchSettings settings, bool isPrivate, bool isQuick)
        {
            string code;
            do code = NewCode(); while (_rooms.ContainsKey(code));
            var room = new Room(this, code, settings, isPrivate, isQuick, Time);
            _rooms[code] = room;
            return room;
        }

        private static string NewCode()
        {
            var sb = new StringBuilder(6);
            for (int i = 0; i < 6; i++) sb.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
            return sb.ToString();
        }

        private void HandleCreate(Session s, Msg msg)
        {
            if (!GameModes.TryParse(msg.Str("mode", 12, "tdm"), out GameMode mode)) { s.Send(Json.Error("bad_mode")); return; }
            if (!EnsureRoomCapacity(s)) return;
            LeaveCurrent(s, abandoned: true);

            MatchSettings settings = MatchSettings.For(mode);
            string mapId = msg.Str("map", 16, "warehouse");
            settings.MapId = _maps.GetById(mapId)?.Id ?? "warehouse";
            ApplyRules(settings, msg);

            Room room = CreateRoom(settings, isPrivate: msg.Bool("private", true), isQuick: false);
            room.BotSkill = (float)msg.Num("skill", 0.55, 0.05, 1.0);
            room.AddHuman(s, Time);

            int bots = msg.Int("bots", 0, 0, 15);
            if (mode == GameMode.Training)
            {
                var training = new TrainingRules();
                training.SetBotCount(bots == 0 ? 5 : bots);
                training.SetDuration(settings.TimeLimitSeconds);
                settings.TimeLimitSeconds = training.DurationSeconds;
                bots = training.BotCount;
            }
            for (int i = 0; i < bots; i++) room.AddBot();

            if (mode == GameMode.Training)
            {
                s.Member.Team = 0;
                room.StartMatch(Time);
                return;
            }
            room.BroadcastLobby();
        }

        /// <summary>Benutzerdefinierte Regeln (FR-21) – geklemmt über Core CustomGameRules.</summary>
        private static void ApplyRules(MatchSettings settings, Msg msg)
        {
            var rules = new CustomGameRules();
            rules.SetTimeLimit(settings.TimeLimitSeconds);
            rules.SetTargetScore(settings.TargetScore);
            if (msg.TryNum("timeLimit", out double tl)) rules.SetTimeLimit((float)Math.Clamp(tl, -1e6, 1e6));
            if (msg.TryNum("targetScore", out double ts)) rules.SetTargetScore((int)Math.Clamp(ts, -1e6, 1e6));
            settings.TimeLimitSeconds = rules.TimeLimitSeconds;
            settings.TargetScore = rules.TargetScore;
            if (msg.Has("friendlyFire")) settings.FriendlyFire = msg.Bool("friendlyFire", false);
            if (msg.Has("powerUps")) settings.PowerUpsEnabled = msg.Bool("powerUps", true);
        }

        private void HandleJoin(Session s, Msg msg)
        {
            string code = msg.Str("code", 12);
            Room room = FindRoom(code);
            if (room == null) { s.Send(Json.Error("room_not_found", "Kein Raum mit diesem Code gefunden.")); return; }
            if (room.Members.Count >= room.MaxPlayers) { s.Send(Json.Error("room_full")); return; }
            if (room.State == RoomState.Results) { s.Send(Json.Error("room_busy")); return; }
            if (s.Room == room) { s.Send(room.LobbyJson(s.Member)); return; }
            LeaveCurrent(s, abandoned: true);
            room.AddHuman(s, Time);
            room.BroadcastLobby();
        }

        private void LeaveCurrent(Session s, bool abandoned)
        {
            if (s.Room == null || s.Member == null) return;
            Room room = s.Room;
            Member m = s.Member;
            m.Abandoned = abandoned && room.State == RoomState.Match;
            room.Remove(m, m.Abandoned);
        }

        private void HandleLeave(Session s)
        {
            LeaveCurrent(s, abandoned: true);
            s.Send(Json.Write(w => w.WriteString("t", "left")));
        }

        private bool RequireLobby(Session s, bool hostOnly)
        {
            if (s.Room == null || s.Member == null) { s.Send(Json.Error("not_in_room")); return false; }
            if (hostOnly && s.Room.HostId != s.Member.Id) { s.Send(Json.Error("not_host")); return false; }
            if (s.Room.State != RoomState.Lobby) { s.Send(Json.Error("not_in_lobby")); return false; }
            return true;
        }

        private void HandleTeam(Session s, Msg msg)
        {
            if (!RequireLobby(s, hostOnly: false)) return;
            s.Member.Team = msg.Int("team", 0, 0, 1);
            s.Member.Ready = false;
            s.Room.BroadcastLobby();
        }

        private void HandleReady(Session s, Msg msg)
        {
            if (!RequireLobby(s, hostOnly: false)) return;
            s.Member.Ready = msg.Bool("ready", true);
            s.Room.BroadcastLobby();
        }

        private void HandleStart(Session s)
        {
            if (!RequireLobby(s, hostOnly: true)) return;
            if (!s.Room.AllReady) { s.Send(Json.Error("not_all_ready", "Alle Spieler müssen bereit sein.")); return; }
            s.Room.BeginCountdown(Time);
        }

        private void HandleConfig(Session s, Msg msg)
        {
            if (!RequireLobby(s, hostOnly: true)) return;
            Room room = s.Room;
            if (msg.Has("mode") && GameModes.TryParse(msg.Str("mode", 12), out GameMode mode) && mode != GameMode.Training)
            {
                MatchSettings defaults = MatchSettings.For(mode);
                room.Settings.Mode = mode;
                room.Settings.TargetScore = defaults.TargetScore;
                room.Settings.TimeLimitSeconds = defaults.TimeLimitSeconds;
                room.Settings.AllowRespawn = defaults.AllowRespawn;
                room.Settings.RoundsToWin = defaults.RoundsToWin;
            }
            if (msg.Has("map") && _maps.GetById(msg.Str("map", 16)) != null) room.Settings.MapId = _maps.GetById(msg.Str("map", 16)).Id;
            ApplyRules(room.Settings, msg);
            if (msg.TryNum("skill", out double skill)) room.BotSkill = (float)Math.Clamp(skill, 0.05, 1.0);
            while (room.Members.Count > room.MaxPlayers && room.RemoveBot()) { }
            foreach (Member m in room.Members) if (!m.IsBot) m.Ready = false;
            room.BroadcastLobby();
        }

        private void HandleBot(Session s, Msg msg)
        {
            if (!RequireLobby(s, hostOnly: true)) return;
            bool ok = msg.Bool("add", true) ? s.Room.AddBot() != null : s.Room.RemoveBot();
            if (!ok) s.Send(Json.Error("bot_limit"));
            s.Room.BroadcastLobby();
        }

        // ---------------- Soziales ----------------

        private void HandleChat(Session s, Msg msg)
        {
            if (s.Room == null || s.Member == null) { s.Send(Json.Error("not_in_room")); return; }
            int id = msg.Int("id", -1, -1, 10000);
            if (id < 0 || id >= Phrases.Length) { s.Send(Json.Error("invalid_chat")); return; }
            while (s.ChatTimes.Count > 0 && Time - s.ChatTimes.Peek() > 5f) s.ChatTimes.Dequeue();
            if (s.ChatTimes.Count >= 3) { s.Send(Json.Error("chat_cooldown")); return; }
            s.ChatTimes.Enqueue(Time);
            s.Room.RegisterActivity(s.Member, Time);

            bool team = s.Room.State == RoomState.Match && s.Room.Match != null && s.Room.Match.IsTeamMode;
            string json = Json.Write(w =>
            {
                w.WriteString("t", "chat");
                w.WriteNumber("from", s.Member.SimId >= 0 ? s.Member.SimId : s.Member.Id);
                w.WriteString("name", s.Member.Name);
                w.WriteNumber("id", id);
                w.WriteBoolean("team", team);
            });
            IEnumerable<Member> targets = team ? s.Room.TeamOf(s.Member) : s.Room.Members;
            foreach (Member m in targets) if (!m.IsBot && m.Connected) m.Session?.Send(json);
        }

        private void HandleEmote(Session s, Msg msg)
        {
            if (s.Room == null || s.Member == null) return;
            int id = msg.Int("id", -1, -1, 100);
            if (id < 0 || id > 7) { s.Send(Json.Error("invalid_emote")); return; }
            while (s.ChatTimes.Count > 0 && Time - s.ChatTimes.Peek() > 5f) s.ChatTimes.Dequeue();
            if (s.ChatTimes.Count >= 3) return;
            s.ChatTimes.Enqueue(Time);
            string json = Json.Write(w => { w.WriteString("t", "emote"); w.WriteNumber("from", s.Member.SimId); w.WriteNumber("id", id); });
            foreach (Member m in s.Room.Members) if (!m.IsBot && m.Connected) m.Session?.Send(json);
        }

        private void HandleMark(Session s, Msg msg)
        {
            if (s.Room == null || s.Member == null || s.Room.State != RoomState.Match) return;
            double x = msg.Num("x", 0, -200, 200), y = msg.Num("y", 0, -5, 50), z = msg.Num("z", 0, -200, 200);
            string json = Json.Write(w =>
            {
                w.WriteString("t", "mark");
                w.WriteNumber("from", s.Member.SimId);
                w.Num("x", x); w.Num("y", y); w.Num("z", z);
            });
            foreach (Member m in s.Room.TeamOf(s.Member)) if (!m.IsBot && m.Connected) m.Session?.Send(json);
        }

        private void HandleReport(Session s, Msg msg)
        {
            if (s.Room == null || s.Member == null) { s.Send(Json.Error("not_in_room")); return; }
            ReportReason reason = msg.Str("reason", 16, "toxicity") switch
            {
                "cheating" => ReportReason.Cheating,
                "afk" => ReportReason.Afk,
                "bug" => ReportReason.Bug,
                _ => ReportReason.Toxicity
            };
            bool accepted = s.Room.Report(s.Member, msg.Int("player", -1, -1, 100000), reason);
            if (accepted) Console.WriteLine($"[Moderation] Meldung ({reason}) in Raum {s.Room.Code}");
            s.Send(Json.Write(w => { w.WriteString("t", "reported"); w.WriteBoolean("accepted", accepted); }));
        }

        // ---------------- Profil, Loadout, Shop ----------------

        private void HandleLoadout(Session s, Msg msg)
        {
            string marker = msg.Str("marker", 16), paint = msg.Str("paint", 32), accent = msg.Str("accent", 32);
            if (marker != null && !Accounts.TryEquipMarker(s.AccountId, marker)) { s.Send(Json.Error("locked", "Marker noch nicht freigeschaltet.")); return; }
            if (paint != null && !Accounts.TryEquipCosmetic(s.AccountId, paint)) { s.Send(Json.Error("locked", "Farbe nicht im Besitz.")); return; }
            if (accent != null && !Accounts.TryEquipCosmetic(s.AccountId, accent)) { s.Send(Json.Error("locked", "Akzent nicht im Besitz.")); return; }

            if (s.Member != null && s.Room?.State == RoomState.Lobby)
            {
                PlayerProfile p = Accounts.GetProfile(s.AccountId);
                s.Member.Marker = p.Marker;
                s.Member.Paint = AccountStore.ColorOf(p.Paint) ?? s.Member.Paint;
                s.Member.Accent = AccountStore.ColorOf(p.Accent) ?? s.Member.Accent;
            }
            s.Send(ProfileJson(s.AccountId));
        }

        private void HandleBuy(Session s, Msg msg)
        {
            if (!Accounts.TryBuy(s.AccountId, msg.Str("item", 32), out string error)) { s.Send(Json.Error(error)); return; }
            s.Send(ProfileJson(s.AccountId));
        }

        public string ProfileJson(string accountId) => Json.Write(w =>
        {
            w.WriteString("t", "profile");
            WriteProfileBody(w, accountId);
        });

        private void WriteProfile(System.Text.Json.Utf8JsonWriter w, string accountId)
        {
            w.WriteStartObject();
            WriteProfileBody(w, accountId);
            w.WriteEndObject();
        }

        private void WriteProfileBody(System.Text.Json.Utf8JsonWriter w, string accountId)
        {
            PlayerAccount a = Accounts.GetAccount(accountId);
            PlayerProfile p = Accounts.GetProfile(accountId);
            if (a == null || p == null) return;
            w.WriteString("name", a.DisplayName);
            w.WriteNumber("level", a.Level);
            w.WriteNumber("xp", a.TotalXp);
            w.WriteNumber("xpLevel", XpCalculator.XpForLevel(a.Level));
            w.WriteNumber("xpNext", XpCalculator.XpForLevel(a.Level + 1));
            w.WriteNumber("mmr", a.Mmr);
            w.WriteString("league", SeasonRanker.GetRankName(a.Mmr));
            w.WriteNumber("division", SeasonRanker.GetDivision(a.Mmr));
            w.WriteNumber("coins", p.Coins);
            w.WriteNumber("matches", a.TotalMatches);
            w.WriteNumber("wins", a.TotalWins);
            w.WriteNumber("kills", a.TotalEliminations);
            w.WriteNumber("deaths", a.TotalDeaths);
            w.Num("accuracy", a.Accuracy, 3);
            w.WriteString("marker", p.Marker);
            w.WriteString("paint", p.Paint);
            w.WriteString("accent", p.Accent);

            w.WriteStartArray("markers");
            foreach (Paintball.Core.Weapons.MarkerSpecs m in MarkerCatalog.All)
            {
                w.WriteStartObject();
                w.WriteString("id", m.Id);
                w.WriteString("name", m.DisplayName);
                w.WriteNumber("unlock", AccountStore.MarkerUnlockLevel(m.Id));
                w.WriteBoolean("unlocked", Accounts.CanUseMarker(accountId, m.Id));
                w.Num("rps", m.RoundsPerSecond, 1);
                w.Num("damage", m.BaseDamage, 0);
                w.WriteNumber("mag", m.MagazineSize);
                w.Num("range", m.MaxRange, 0);
                w.Num("spread", m.SpreadDegrees, 2);
                w.Num("reload", m.ReloadSeconds, 1);
                w.Num("velocity", m.MuzzleVelocity, 1);
                w.Num("gravity", m.GravityScale, 2);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("cosmetics");
            foreach (ShopEntry e in Accounts.Shop)
            {
                w.WriteStartObject();
                w.WriteString("id", e.Id);
                w.WriteString("name", e.Name);
                w.WriteString("kind", e.Kind);
                w.WriteString("color", e.Color);
                w.WriteNumber("price", e.Price);
                w.WriteNumber("unlock", e.UnlockLevel);
                w.WriteBoolean("owned", Accounts.Owns(accountId, e.Id));
                w.WriteBoolean("cosmeticOnly", e.CosmeticOnly);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("achievements");
            foreach (var (def, progress, unlocked) in Accounts.AchievementStatus(accountId))
            {
                w.WriteStartObject();
                w.WriteString("id", def.Id);
                w.WriteString("title", def.Title);
                w.WriteNumber("progress", progress);
                w.WriteNumber("target", def.Target);
                w.WriteBoolean("unlocked", unlocked);
                w.WriteNumber("rewardXp", def.RewardXp);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("history");
            foreach (string h in p.History) w.WriteStringValue(h);
            w.WriteEndArray();
        }

        public string LeaderboardJson(string accountId) => Json.Write(w =>
        {
            w.WriteString("t", "leaderboard");
            w.WriteStartArray("rows");
            foreach (LeaderboardRow r in Accounts.Leaderboard(50))
            {
                w.WriteStartObject();
                w.WriteNumber("rank", r.Rank);
                w.WriteString("name", r.Name);
                w.WriteNumber("mmr", r.Mmr);
                w.WriteNumber("level", r.Level);
                w.WriteString("league", r.League);
                w.WriteNumber("division", r.Division);
                w.WriteBoolean("you", r.AccountId == accountId);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }
}
