using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Core.Matchmaking;
using Paintball.Core.Progression;
using Paintball.Core.Session;
using Paintball.Core.Social;
using Paintball.Core.Telemetry;
using Paintball.Net.Accounts;
using Paintball.Net.Protocol;
using Paintball.Net.Simulation;

namespace Paintball.Net.Rooms
{
    public enum RoomState { Lobby, Countdown, Match, Results }

    /// <summary>Mitglied eines Raums: Mensch (mit Session) oder Bot.</summary>
    public sealed class Member
    {
        public int Id;
        public Session Session;
        public string AccountId;
        public string Name;
        public int Team;
        public bool Ready;
        public bool IsBot;
        public string Input = "kbm";
        public bool CrossPlay = true;
        public AppPlatform Platform = AppPlatform.Web;
        public int Mmr = 1000;
        public int Level = 1;
        public string Marker = MarkerCatalog.Standard;
        public string Paint = "#ff3fa4";
        public string Accent = "#ffd23f";
        public int SimId = -1;
        public bool Connected = true;
        public bool Abandoned;
    }

    /// <summary>
    /// Ein Raum = Lobby + (optional) laufendes Match (FR-20/21/24, FR-13, FR-27, FR-31, FR-32).
    /// Läuft ausschließlich auf dem Server-Tick-Thread.
    /// </summary>
    public sealed class Room
    {
        private static readonly string[] BotNames =
        {
            "Klecks", "Spritzer", "Farbfleck", "Pinsel", "Tupfer", "Neon", "Pigment", "Sprühdose",
            "Regenbogen", "Palette", "Kobalt", "Magenta", "Ocker", "Indigo", "Zinnober", "Türkis"
        };

        private readonly GameServer _server;
        private readonly MapCatalog _maps = new();
        private readonly ReportEvaluator _reports = new();
        private ReconnectManager _reconnect;
        private LeaverDetection _leaver;
        private int _nextMemberId = 1;
        private int _botNameIndex;
        private float _nextHousekeeping;
        private float _matchStartedAt;
        private bool _afkArmed;
        private Dictionary<int, HashSet<int>> _visibility = new();

        public string Code { get; }
        public bool IsPrivate { get; }
        public bool IsQuick { get; }
        public string InputPool { get; private set; } = "mixed";
        public MatchSettings Settings { get; }
        public float BotSkill { get; set; } = 0.55f;
        public RoomState State { get; private set; } = RoomState.Lobby;
        public List<Member> Members { get; } = new();
        public int HostId { get; private set; } = -1;
        public GameMatch Match { get; private set; }
        public MatchTelemetry Telemetry { get; } = new();
        public float CreatedAt { get; }
        public float QuickStartAt { get; private set; }
        public float CountdownEnd { get; private set; }
        public float ResultsEnd { get; private set; }

        public MapDefinition MapDef => _maps.GetById(Settings.MapId) ?? _maps.Get(0);
        /// <summary>Höchstzahl Mitglieder je Raum (Event-Paket: 10 gegen 10).</summary>
        public const int MaxRoomPlayers = 20;
        /// <summary>Ab mehr als so vielen Menschen wechselt eine Quick-Lobby auf die große Karte.</summary>
        public const int LargeQuickMatchPlayers = 12;
        public const string LargeMapId = "pizzeria";

        public int MaxPlayers => Math.Min(MaxRoomPlayers, Math.Max(2, MapDef.MaxPlayers));
        /// <summary>Plätze für Beitritte: Quick-Lobbys nehmen bis 20 auf (Kartenwechsel beim Beitritt), sonst die Karte.</summary>
        public int Capacity => IsQuick && State == RoomState.Lobby ? MaxRoomPlayers : MaxPlayers;
        public int HumanCount => Members.Count(m => !m.IsBot);
        public bool IsEmpty => HumanCount == 0;
        public double AverageMmr => Members.Where(m => !m.IsBot).Select(m => (double)m.Mmr).DefaultIfEmpty(1000).Average();

        internal Room(GameServer server, string code, MatchSettings settings, bool isPrivate, bool isQuick, float now)
        {
            _server = server;
            Code = code;
            Settings = settings;
            IsPrivate = isPrivate;
            IsQuick = isQuick;
            CreatedAt = now;
            QuickStartAt = now + server.Options.QuickMatchWaitSeconds;
        }

        public Member FindMemberBySim(int simId) => Members.FirstOrDefault(m => m.SimId == simId);
        public Member FindMember(int memberId) => Members.FirstOrDefault(m => m.Id == memberId);
        public Member FindByAccount(string accountId) => Members.FirstOrDefault(m => !m.IsBot && m.AccountId == accountId);

        // ---------------- Mitglieder ----------------

        /// <summary>Cross-Play-Kompatibilität (FR-28, PA-05, NFR-15).</summary>
        public bool Accepts(Session s)
        {
            if (Members.Count >= Capacity || State == RoomState.Results) return false;
            string pool = s.CrossPlay ? "mixed" : s.Input;
            if (HumanCount > 0 && pool != InputPool) return false;
            var policy = new CrossPlayPolicy { Enabled = s.CrossPlay };
            foreach (Member m in Members)
                if (!m.IsBot && !policy.AllowsCrossPlay(s.Platform, m.Platform)) return false;
            return true;
        }

        internal Member AddHuman(Session s, float now)
        {
            if (HumanCount == 0) InputPool = s.CrossPlay ? "mixed" : s.Input;
            PlayerAccount account = _server.Accounts.GetAccount(s.AccountId);
            PlayerProfile profile = _server.Accounts.GetProfile(s.AccountId);
            var m = new Member
            {
                Id = _nextMemberId++,
                Session = s,
                AccountId = s.AccountId,
                Name = s.Name,
                Input = s.Input,
                CrossPlay = s.CrossPlay,
                Platform = s.Platform,
                Mmr = account?.Mmr ?? 1000,
                Level = account?.Level ?? 1,
                Marker = profile?.Marker ?? MarkerCatalog.Standard,
                Paint = AccountStore.ColorOf(profile?.Paint) ?? "#ff3fa4",
                Accent = AccountStore.ColorOf(profile?.Accent) ?? "#ffd23f",
                Team = SmallerTeam()
            };
            Members.Add(m);
            s.Room = this;
            s.Member = m;
            s.QueueSince = now;
            if (HostId < 0 || FindMember(HostId) == null || FindMember(HostId).IsBot) HostId = m.Id;
            // Event: mehr als 12 Menschen (oder Rotationskarte zu klein) → Pizzeria
            if (IsQuick && State == RoomState.Lobby && (HumanCount > LargeQuickMatchPlayers || HumanCount > MapDef.MaxPlayers)
                && _maps.GetById(LargeMapId) != null)
                Settings.MapId = LargeMapId;
            if (IsQuick && HumanCount == Capacity) QuickStartAt = now;

            // Späteinstieg in laufendes Quick-Match: Bot-Slot übernehmen (FR-31 Ersatz)
            if (State == RoomState.Match && Match != null)
            {
                Member bot = Members.FirstOrDefault(b => b.IsBot && b.SimId >= 0 && GameModes.IsTeamMode(Settings.Mode) && b.Team == m.Team)
                          ?? Members.FirstOrDefault(b => b.IsBot && b.SimId >= 0);
                if (bot != null)
                {
                    SimPlayer sim = Match.Find(bot.SimId);
                    m.SimId = bot.SimId;
                    m.Team = sim.Team;
                    sim.IsBot = false;
                    sim.Name = m.Name;
                    sim.AccountId = m.AccountId;
                    sim.Connected = true;
                    Members.Remove(bot);
                    _leaver?.RegisterPlayer(m.AccountId, now);
                    SendStart(m);
                    BroadcastRoster();
                }
                else
                {
                    AddSimFor(m);
                    _leaver?.RegisterPlayer(m.AccountId, now);
                    SendStart(m);
                    BroadcastRoster();
                }
            }
            return m;
        }

        internal Member AddBot()
        {
            if (Members.Count >= MaxPlayers) return null;
            var bot = new Member
            {
                Id = _nextMemberId++,
                IsBot = true,
                Ready = true,
                Name = "Bot " + BotNames[_botNameIndex++ % BotNames.Length],
                Mmr = 800 + (int)(BotSkill * 400f),
                Team = SmallerTeam(),
                Marker = new[] { MarkerCatalog.Standard, MarkerCatalog.Rapid, MarkerCatalog.Precision }[_botNameIndex % 3],
                Paint = new[] { "#22d3ee", "#a3e635", "#fb923c", "#a855f7" }[_botNameIndex % 4],
                Accent = "#f8fafc"
            };
            Members.Add(bot);
            return bot;
        }

        internal bool RemoveBot()
        {
            Member bot = Members.LastOrDefault(m => m.IsBot);
            if (bot == null) return false;
            Members.Remove(bot);
            return true;
        }

        private int SmallerTeam()
        {
            int t0 = Members.Count(m => m.Team == 0), t1 = Members.Count(m => m.Team == 1);
            return t1 < t0 ? 1 : 0;
        }

        /// <summary>Entfernt ein Mitglied; im laufenden Match übernimmt ein Bot den Slot (FR-31).</summary>
        internal void Remove(Member m, bool abandoned)
        {
            if (!Members.Remove(m)) return;
            if (m.Session != null && m.Session.Room == this)
            {
                m.Session.Room = null;
                m.Session.Member = null;
            }

            if (State == RoomState.Match && Match != null && m.SimId >= 0)
            {
                SimPlayer sim = Match.Find(m.SimId);
                if (sim != null)
                {
                    sim.IsBot = true;
                    sim.Connected = true;
                    sim.Name = "Bot " + BotNames[_botNameIndex++ % BotNames.Length];
                    Members.Add(new Member { Id = _nextMemberId++, IsBot = true, Ready = true, Name = sim.Name, Team = m.Team, SimId = m.SimId, Mmr = 1000 });
                }
                if (abandoned && m.AccountId != null) _leaver?.MarkAbandoned(m.AccountId, _server.Time);
                BroadcastRoster();
            }

            if (m.Id == HostId)
            {
                Member next = Members.FirstOrDefault(x => !x.IsBot);
                HostId = next?.Id ?? -1;
            }
            BroadcastLobby();
        }

        // ---------------- Lobby ----------------

        internal void BroadcastLobby()
        {
            foreach (Member m in Members)
                if (!m.IsBot && m.Connected) m.Session?.Send(LobbyJson(m));
        }

        internal string LobbyJson(Member viewer) => Json.Write(w =>
        {
            w.WriteString("t", "lobby");
            w.WriteString("code", Code);
            w.WriteString("mode", GameModes.Id(Settings.Mode));
            w.WriteString("map", Settings.MapId);
            w.WriteBoolean("private", IsPrivate);
            w.WriteBoolean("quick", IsQuick);
            w.WriteNumber("host", HostId);
            w.WriteNumber("you", viewer?.Id ?? -1);
            w.WriteString("state", State.ToString().ToLowerInvariant());
            w.Num("countdown", State == RoomState.Countdown ? Math.Max(0f, CountdownEnd - _server.Time) : 0f, 1);
            w.WriteNumber("maxPlayers", Capacity);
            w.WriteStartObject("rules");
            w.WriteNumber("timeLimit", (int)Settings.TimeLimitSeconds);
            w.WriteNumber("targetScore", Settings.TargetScore);
            w.WriteBoolean("friendlyFire", Settings.FriendlyFire);
            w.WriteBoolean("powerUps", Settings.PowerUpsEnabled);
            w.Num("skill", BotSkill);
            w.WriteEndObject();
            w.WriteStartArray("members");
            foreach (Member m in Members)
            {
                w.WriteStartObject();
                w.WriteNumber("id", m.Id);
                w.WriteString("name", m.Name);
                w.WriteNumber("team", m.Team);
                w.WriteBoolean("ready", m.Ready || m.IsBot);
                w.WriteBoolean("bot", m.IsBot);
                w.WriteNumber("level", m.Level);
                w.WriteString("league", Paintball.Core.Ranking.SeasonRanker.GetRankName(m.Mmr));
                w.WriteBoolean("host", m.Id == HostId);
                w.WriteBoolean("connected", m.Connected);
                w.WriteString("input", m.Input);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        internal bool AllReady => Members.Where(m => !m.IsBot).All(m => m.Ready);

        internal void BeginCountdown(float now)
        {
            State = RoomState.Countdown;
            CountdownEnd = now + _server.Options.LobbyCountdownSeconds;
            BroadcastLobby();
        }

        // ---------------- Tick ----------------

        internal void Tick(float now)
        {
            switch (State)
            {
                case RoomState.Lobby:
                    if (IsQuick && HumanCount > 0)
                    {
                        if (now >= _nextHousekeeping)
                        {
                            _nextHousekeeping = now + 1f;
                            foreach (Member m in Members.Where(x => !x.IsBot)) m.Session?.Send(QueueJson(m, now));
                        }
                        if (now >= QuickStartAt) StartMatch(now);
                    }
                    break;

                case RoomState.Countdown:
                    if (now >= CountdownEnd) StartMatch(now);
                    break;

                case RoomState.Match:
                    TickMatch(now);
                    break;

                case RoomState.Results:
                    if (now >= ResultsEnd) ReturnToLobby(now);
                    break;
            }
        }

        private string QueueJson(Member m, float now) => Json.Write(w =>
        {
            w.WriteString("t", "queue");
            w.Num("waited", now - (m.Session?.QueueSince ?? now), 1);
            w.Num("startsIn", Math.Max(0f, QuickStartAt - now), 1);
            w.WriteNumber("humans", HumanCount);
            w.WriteNumber("max", Capacity);
            w.WriteString("mode", GameModes.Id(Settings.Mode));
        });

        // ---------------- Match-Start ----------------

        internal void StartMatch(float now)
        {
            MatchSettings settings = Settings.Clone();
            bool training = settings.Mode == GameMode.Training;
            settings.Ranked = !training;
            bool teamMode = GameModes.IsTeamMode(settings.Mode);

            if (IsQuick)
            {
                int target = Math.Min(MaxPlayers, Math.Max(teamMode ? _server.Options.QuickMatchMinTeamPlayers : _server.Options.QuickMatchMinFfaPlayers, HumanCount));
                if (teamMode && target % 2 == 1 && target < MaxPlayers) target++;
                while (Members.Count < target && AddBot() != null) { }
                if (teamMode) BalanceTeams();
            }
            else if (teamMode)
            {
                // Bots gleichen Teamgrößen aus, Menschen behalten ihre Wahl
                foreach (Member bot in Members.Where(m => m.IsBot)) bot.Team = -1;
                foreach (Member bot in Members.Where(m => m.IsBot))
                {
                    int t0 = Members.Count(m => m.Team == 0), t1 = Members.Count(m => m.Team == 1);
                    bot.Team = t1 < t0 ? 1 : 0;
                }
            }

            int seed = Environment.TickCount ^ Code.GetHashCode();
            Match = new GameMatch(settings, MapDef, seed);
            Match.BotThink = new BotController(seed, BotSkill).Think;
            foreach (Member m in Members) AddSimFor(m);

            _reconnect = new ReconnectManager(_server.Options.ReconnectGraceSeconds);
            _leaver = new LeaverDetection(_server.Options.AfkTimeoutSeconds, 0f);
            _afkArmed = false;
            foreach (Member m in Members.Where(x => !x.IsBot)) _leaver.RegisterPlayer(m.AccountId, now);

            Match.Start();
            _matchStartedAt = now;
            State = RoomState.Match;
            foreach (Member m in Members.Where(x => !x.IsBot && x.Connected)) SendStart(m);
            BroadcastLobby();
            _server.Metrics.MatchesStarted++;
        }

        private void AddSimFor(Member m)
        {
            SimPlayer sim = Match.AddPlayer(m.Name, m.Team, m.IsBot, m.Marker, m.AccountId);
            sim.PaintColor = m.Paint;
            sim.AccentColor = m.Accent;
            sim.Connected = m.IsBot || m.Connected;
            m.SimId = sim.Id;
            if (!GameModes.IsTeamMode(Settings.Mode)) m.Team = sim.Team;
        }

        /// <summary>Faire Teams nach MMR (NFR-15, Core TeamBalancer).</summary>
        private void BalanceTeams()
        {
            var mmr = Members.ToDictionary(m => m.Id.ToString(), m => m.Mmr);
            TeamBalancer.TeamAssignment assignment = new TeamBalancer().Balance(mmr);
            foreach (Member m in Members) m.Team = assignment.Team0.Contains(m.Id.ToString()) ? 0 : 1;
        }

        internal void SendStart(Member m)
        {
            if (m.Session == null || Match == null) return;
            m.Session.Send(Json.Write(w =>
            {
                w.WriteString("t", "start");
                w.WriteNumber("you", m.SimId);
                w.WriteNumber("team", Match.Find(m.SimId)?.Team ?? m.Team);
                w.WriteString("mode", GameModes.Id(Settings.Mode));
                w.WriteString("map", Settings.MapId);
                w.WriteBoolean("teamMode", Match.IsTeamMode);
                w.WriteBoolean("ranked", Match.Settings.Ranked);
                w.WriteNumber("tick", GameMatch.TickRate);
                w.Num("time", Match.Time, 3);
                w.WriteStartObject("rules");
                w.WriteNumber("timeLimit", (int)Match.Settings.TimeLimitSeconds);
                w.WriteNumber("targetScore", Match.Settings.TargetScore);
                w.WriteNumber("roundsToWin", Match.Settings.RoundsToWin);
                w.WriteBoolean("friendlyFire", Match.Settings.FriendlyFire);
                w.WriteEndObject();
                WriteRoster(w);
                w.WriteStartArray("pickups");
                foreach (PickupState p in Match.Pickups)
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", p.Id);
                    w.WriteString("type", p.Type.ToString());
                    w.Vec("p", p.Position);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("flags");
                foreach (FlagState f in Match.Flags)
                {
                    w.WriteStartObject();
                    w.WriteNumber("team", f.Team);
                    w.Vec("home", f.Home);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartObject("zone");
                w.Vec("c", Match.Zone.Center);
                w.Num("r", Match.Zone.Radius);
                w.WriteEndObject();
            }));
        }

        private void WriteRoster(System.Text.Json.Utf8JsonWriter w)
        {
            w.WriteStartArray("players");
            foreach (SimPlayer p in Match.Players)
            {
                w.WriteStartObject();
                w.WriteNumber("id", p.Id);
                w.WriteString("name", p.Name);
                w.WriteNumber("team", p.Team);
                w.WriteBoolean("bot", p.IsBot);
                w.WriteString("paint", p.PaintColor);
                w.WriteString("accent", p.AccentColor);
                w.WriteString("marker", p.Specs.Id);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        internal void BroadcastRoster()
        {
            if (Match == null) return;
            string json = Json.Write(w => { w.WriteString("t", "roster"); WriteRoster(w); });
            foreach (Member m in Members) if (!m.IsBot && m.Connected) m.Session?.Send(json);
        }

        // ---------------- Match-Lauf ----------------

        internal void HandleInput(Member m, PlayerInputFrame frame, float now)
        {
            if (State != RoomState.Match || Match == null || m.SimId < 0) return;
            Match.EnqueueInput(m.SimId, frame);
            if (frame.HasActivity) _leaver?.RegisterActivity(m.AccountId, now);
        }

        internal void RegisterActivity(Member m, float now)
        {
            if (State == RoomState.Match && !m.IsBot) _leaver?.RegisterActivity(m.AccountId, now);
        }

        private void TickMatch(float now)
        {
            Match.Tick();
            List<MatchEvent> events = Match.DrainEvents();

            if (now >= _nextHousekeeping)
            {
                _nextHousekeeping = now + 1f;
                Housekeeping(now);
            }

            ComputeVisibility();
            string eventsJson = events.Count > 0 ? SnapshotBuilder.Events(events) : null;
            foreach (Member m in Members)
            {
                if (m.IsBot || !m.Connected || m.Session == null) continue;
                if (eventsJson != null) m.Session.Send(eventsJson);
                foreach (MatchEvent e in events)
                    if (e is NoticeEvent n && n.PlayerId == m.SimId)
                        m.Session.Send(Json.Write(w => { w.WriteString("t", "notice"); w.WriteString("key", n.Key); }));
                m.Session.Send(SnapshotBuilder.Snapshot(Match, m.SimId, Visible(m.SimId), Telemetry));
            }

            if (Match.Phase == MatchPhase.Finished) FinishMatch(now);
        }

        private void Housekeeping(float now)
        {
            // Telemetrie (FR-29): RTT/Verlust der Clients
            foreach (Member m in Members.Where(x => !x.IsBot && x.Connected && x.Session != null && x.SimId >= 0))
            {
                if (m.Session.RttMs > 0) Telemetry.RecordPing(m.SimId, m.Session.RttMs);
                Telemetry.RecordPacketLoss(m.SimId, m.Session.PacketLoss);
            }

            // Reconnect-Frist abgelaufen → Leaver (FR-27/FR-31)
            _reconnect?.PruneExpired(now);
            foreach (Member m in Members.Where(x => !x.IsBot && !x.Connected).ToList())
            {
                if (_reconnect != null && _reconnect.HasReservedSlot(m.AccountId)) continue;
                m.Abandoned = true;
                Remove(m, abandoned: true);
            }

            // AFK (FR-31) – die Uhr läuft erst ab Matchbeginn (im Countdown kann niemand handeln)
            if (_leaver != null && Match.Phase == MatchPhase.Running && !_afkArmed)
            {
                _afkArmed = true;
                foreach (Member m in Members.Where(x => !x.IsBot)) _leaver.RegisterActivity(m.AccountId, now);
            }
            if (_leaver != null && Match.Phase == MatchPhase.Running)
            {
                foreach (string accountId in _leaver.GetAfkPlayers(now))
                {
                    Member m = FindByAccount(accountId);
                    if (m == null || !m.Connected) continue;
                    m.Session?.Send(Json.Write(w => { w.WriteString("t", "kicked"); w.WriteString("reason", "afk"); }));
                    m.Abandoned = true;
                    Remove(m, abandoned: true);
                    _leaver.RegisterActivity(accountId, now); // nicht erneut melden
                    _server.Metrics.AfkKicks++;
                }
            }
        }

        /// <summary>Verbindungsabbruch im Match: KI übernimmt, Slot bleibt reserviert (FR-27).</summary>
        internal void OnDisconnected(Member m, float now)
        {
            if (State == RoomState.Match && Match != null && m.SimId >= 0)
            {
                m.Connected = false;
                m.Session = null;
                SimPlayer sim = Match.Find(m.SimId);
                if (sim != null) sim.Connected = false;
                _reconnect.OnDisconnect(m.AccountId, m.Name, m.Team, now);
                BroadcastLobby();
                return;
            }
            Remove(m, abandoned: false);
        }

        /// <summary>Rückkehr innerhalb der Grace-Frist (FR-27, NFR-09).</summary>
        internal bool TryReconnect(Session s, float now)
        {
            Member m = FindByAccount(s.AccountId);
            if (m == null || m.Connected || _reconnect == null || !_reconnect.TryReconnect(s.AccountId, now, out _)) return false;
            m.Connected = true;
            m.Session = s;
            s.Room = this;
            s.Member = m;
            SimPlayer sim = Match?.Find(m.SimId);
            if (sim != null) sim.Connected = true;
            _leaver?.RegisterActivity(m.AccountId, now);
            s.Send(LobbyJson(m));
            SendStart(m);
            _server.Metrics.Reconnects++;
            return true;
        }

        // ---------------- Sichtbarkeit (Anti-Wallhack, NFR-13) ----------------

        private void ComputeVisibility()
        {
            _visibility = new Dictionary<int, HashSet<int>>();
            bool running = Match.Phase == MatchPhase.Running;
            foreach (SimPlayer viewer in Match.Players)
            {
                if (_visibility.ContainsKey(viewer.Team) && Match.IsTeamMode) continue;
                var set = new HashSet<int>();
                var eyes = new List<Vector3>();
                bool radar = false;
                foreach (SimPlayer mate in Match.Players)
                {
                    if (mate.Team != viewer.Team) continue;
                    set.Add(mate.Id);
                    if (mate.Alive) eyes.Add(Match.EyeOf(mate));
                    radar |= mate.PowerUps.IsActive(Paintball.Core.PowerUps.PowerUpType.RadarPulse, Match.Time);
                }
                foreach (SimPlayer enemy in Match.Players)
                {
                    if (enemy.Team == viewer.Team) continue;
                    if (!running || radar || !enemy.Alive || Match.Time - enemy.LastShotTime < 1f || IsCarrier(enemy) || SeenBy(eyes, enemy))
                        set.Add(enemy.Id);
                }
                _visibility[Match.IsTeamMode ? viewer.Team : viewer.Id] = set;
            }
        }

        private bool IsCarrier(SimPlayer p) => Match.Flags[0].CarrierId == p.Id || Match.Flags[1].CarrierId == p.Id;

        private bool SeenBy(List<Vector3> eyes, SimPlayer enemy)
        {
            float h = Movement.HeightOf(enemy.Move);
            Vector3 head = enemy.Position + new Vector3(0f, h * 0.92f, 0f);
            Vector3 chest = enemy.Position + new Vector3(0f, h * 0.6f, 0f);
            foreach (Vector3 eye in eyes)
            {
                if (Vector3.Distance(eye, chest) < 3.5f) return true;
                if (Match.World.HasLineOfSight(eye, head) || Match.World.HasLineOfSight(eye, chest)) return true;
            }
            return false;
        }

        private HashSet<int> Visible(int viewerSimId)
        {
            SimPlayer viewer = Match.Find(viewerSimId);
            if (viewer == null) return new HashSet<int>();
            return _visibility.TryGetValue(Match.IsTeamMode ? viewer.Team : viewer.Id, out HashSet<int> s) ? s : new HashSet<int>();
        }

        // ---------------- Match-Ende & Belohnung (FR-32, FR-40) ----------------

        private void FinishMatch(float now)
        {
            State = RoomState.Results;
            ResultsEnd = now + _server.Options.ResultsSeconds;
            int? winner = Match.WinnerTeam;
            OutcomeSummary table = new MatchOutcomeEvaluator(Match.Stats, winner ?? -1).Evaluate();
            double minutes = Math.Max(Match.RunningSeconds, GameMatch.TickDt) / 60.0;

            foreach (Member m in Members.Where(x => !x.IsBot).ToList())
            {
                SimPlayer sim = Match.Find(m.SimId);
                var personal = new PersonalResult();
                if (sim != null && Match.Settings.Ranked && !m.Abandoned)
                {
                    try
                    {
                        PlayerAccount account = _server.Accounts.GetAccount(m.AccountId);
                        int levelBefore = account?.Level ?? 1;
                        var service = new MatchCompletionService(Match.Stats, account, winner ?? -1, sim.Id, sim.Team);
                        MatchCompletionResult result = service.Complete(minutes, m.Abandoned, draw: !winner.HasValue);
                        personal.Rewarded = result.RewardsGranted;
                        personal.Reason = result.Reason;
                        personal.Xp = result.XpGained;
                        personal.MmrChange = result.MmrChange;
                        if (result.RewardsGranted && account != null)
                        {
                            PlayerMatchStats st = Match.Stats.GetStats(sim.Id);
                            RewardResult reward = _server.Accounts.ApplyMatch(m.AccountId, new MatchSummary
                            {
                                Mode = GameModes.Id(Settings.Mode), Map = Settings.MapId, Won = winner.HasValue && sim.Team == winner.Value,
                                Kills = st.Eliminations, Deaths = st.Deaths, Objective = st.ObjectiveScore,
                                XpGained = result.XpGained, MmrChange = result.MmrChange
                            });
                            personal.Coins = reward.CoinsEarned;
                            personal.Achievements = reward.NewAchievements;
                            personal.Xp += reward.AchievementXp;
                            m.Mmr = account.Mmr;
                            m.Level = account.Level;
                            personal.LevelUp = account.Level > levelBefore;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Speicherfehler (z. B. Datenbank weg) darf das Matchende der übrigen Mitglieder nicht abbrechen.
                        // Nur den Typ loggen: die Meldung kann Verbindungsdetails enthalten.
                        Console.Error.WriteLine($"[Room] Matchergebnis nicht gespeichert: {ex.GetType().Name}");
                    }
                }
                else if (!Match.Settings.Ranked)
                {
                    personal.Reason = "training";
                }
                m.Session?.Send(SnapshotBuilder.End(Match, table, sim, personal, _server.Accounts.GetAccount(m.AccountId)));
                if (m.Session != null) m.Session.Send(_server.ProfileJson(m.AccountId));
            }

            foreach (Member m in Members.Where(x => !x.IsBot && !x.Connected).ToList()) Remove(m, abandoned: false);
            _server.Metrics.MatchesFinished++;
            BroadcastLobby();
        }

        private void ReturnToLobby(float now)
        {
            Match = null;
            _reconnect = null;
            _leaver = null;
            State = RoomState.Lobby;
            foreach (Member m in Members) { m.SimId = -1; m.Ready = false; }
            if (IsQuick)
            {
                Members.RemoveAll(m => m.IsBot);
                QuickStartAt = now + _server.Options.QuickMatchWaitSeconds;
                foreach (Member m in Members) if (m.Session != null) m.Session.QueueSince = now;
            }
            BroadcastLobby();
        }

        // ---------------- Soziales (FR-51/52) ----------------

        internal IEnumerable<Member> TeamOf(Member sender)
        {
            if (State != RoomState.Match || Match == null) return Members;
            SimPlayer s = Match.Find(sender.SimId);
            if (s == null) return Members;
            return Members.Where(m => { SimPlayer o = Match.Find(m.SimId); return o != null && o.Team == s.Team; });
        }

        internal bool Report(Member reporter, int reportedSimId, ReportReason reason)
        {
            Member target = FindMemberBySim(reportedSimId);
            if (target == null || target == reporter) return false;
            ReportDecision decision = _reports.Submit(reporter.SimId, reportedSimId, reason, "match-report:" + reason);
            if (decision.Accepted && reason == ReportReason.Cheating) Telemetry.RecordSuspiciousAction(reportedSimId);
            return decision.Accepted;
        }
    }

    public sealed class PersonalResult
    {
        public bool Rewarded;
        public string Reason = string.Empty;
        public int Xp;
        public int MmrChange;
        public int Coins;
        public bool LevelUp;
        public List<string> Achievements = new();
    }
}
