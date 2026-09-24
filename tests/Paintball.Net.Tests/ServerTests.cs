using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Paintball.Net.Accounts;
using Paintball.Net.Rooms;

namespace Paintball.Net.Tests
{
    /// <summary>Test-Senke: sammelt alle Nachrichten eines Clients.</summary>
    internal sealed class FakeSink : IClientSink
    {
        public readonly List<string> Messages = new();
        public bool Closed;
        public string CloseReason;

        public void Send(string json) { lock (Messages) Messages.Add(json); }
        public void Close(string reason) { Closed = true; CloseReason = reason; }

        public List<JsonElement> OfType(string type)
        {
            lock (Messages)
            {
                var list = new List<JsonElement>();
                foreach (string m in Messages)
                {
                    JsonElement e = JsonDocument.Parse(m).RootElement;
                    if (e.GetProperty("t").GetString() == type) list.Add(e);
                }
                return list;
            }
        }

        public JsonElement? Last(string type)
        {
            List<JsonElement> all = OfType(type);
            return all.Count == 0 ? null : all[all.Count - 1];
        }

        public void Clear() { lock (Messages) Messages.Clear(); }
    }

    /// <summary>Simulierter Browser-Client für Server-Tests.</summary>
    internal sealed class TestClient
    {
        public readonly FakeSink Sink = new();
        public readonly Session Session;
        private readonly GameServer _server;
        private int _seq;

        public string Token;
        public string AccountId;

        public TestClient(GameServer server, string name, string token = null, bool hello = true, string input = "kbm", bool crossPlay = true)
        {
            _server = server;
            Session = server.Connect(Sink, "127.0.0.1");
            if (!hello) return;
            Send(new { t = "hello", name, token, input, crossPlay, platform = "web" });
            server.Tick();
            JsonElement welcome = Sink.Last("welcome").Value;
            Token = welcome.GetProperty("token").GetString();
            AccountId = welcome.GetProperty("account").GetString();
        }

        public void Send(object message) => _server.Receive(Session, JsonSerializer.Serialize(message));
        public void SendRaw(string raw) => _server.Receive(Session, raw);

        public JsonElement Lobby => Sink.Last("lobby").Value;
        public string RoomCode => Lobby.GetProperty("code").GetString();
        public int SimId => Sink.Last("start").Value.GetProperty("you").GetInt32();

        public void Move(float mx, float mz, float yaw = 0f, bool fire = false, float pitch = 0f)
            => Send(new { t = "in", s = ++_seq, mx, mz, y = yaw, p = pitch, ay = yaw, ap = pitch, b = fire ? 1 : 0 });

        public JsonElement LastSnapshot => Sink.Last("s").Value;

        public JsonElement? SnapshotPlayer(int simId)
        {
            foreach (JsonElement p in LastSnapshot.GetProperty("pl").EnumerateArray())
                if (p[0].GetInt32() == simId) return p;
            return null;
        }
    }

    internal static class ServerTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Server: hello → welcome mit Token, Nachrichten vor Login abgelehnt (FR-48)", HelloWelcome);
            r.Run("Server: Kaputte/zu große/unbekannte Nachrichten crashen nicht (NFR-10)", MalformedMessages);
            r.Run("Server: Flood-Schutz trennt Spammer (NFR-13)", RateLimit);
            r.Run("Lobby: Privater Raum mit Einladungscode, Beitritt, Fehler bei falschem Code (FR-20/FR-24)", PrivateRoomInvite);
            r.Run("Lobby: Teamwahl, Ready-Gating, Host startet mit Countdown (FR-24/UI-04)", ReadyGatingAndStart);
            r.Run("Lobby: Host-Migration beim Verlassen", HostMigration);
            r.Run("Lobby: Host konfiguriert Regeln/Karte/Bots (FR-21)", HostConfiguresRules);
            r.Run("Matchmaking: Quick-Match bündelt Spieler, füllt Bots auf, faire Teams (FR-13/FR-22/NFR-15)", QuickMatchFillsBots);
            r.Run("Matchmaking: Cross-Play aus trennt Touch und Maus (FR-28/PA-05)", CrossPlayOffSeparates);
            r.Run("Matchmaking: Warteschlangenstatus wird gemeldet (NFR-24)", QueueStatusReported);
            r.Run("Training: sofortiger Start gegen Bots, keine XP/MMR (FR-19)", TrainingStartsWithBots);
            r.Run("Match: Eingaben bewegen Spieler, Snapshot bestätigt Sequenz (FR-25/FR-26)", InputsMoveAndAck);
            r.Run("Match: Gegner hinter Wand nicht im Snapshot (Anti-Wallhack, NFR-13)", InterestManagementHidesEnemies);
            r.Run("Match: Ende vergibt XP/MMR/Münzen nur nach validiertem Abschluss (FR-32/FR-40)", MatchEndRewards);
            r.Run("Match: Ergebnis → zurück in Lobby, Rematch schnell (NFR-02)", ReturnToLobbyAfterResults);
            r.Run("Reconnect: KI übernimmt, Rückkehr mit Token innerhalb Frist (FR-27/NFR-09)", ReconnectWithinGrace);
            r.Run("Reconnect: Frist abgelaufen → Leaver, Bot ersetzt, keine Belohnung (FR-31)", LeaverAfterGrace);
            r.Run("AFK: Inaktive Spieler werden entfernt und durch Bot ersetzt (FR-31)", AfkKick);
            r.Run("Sozial: Quick-Chat nur ans eigene Team, nur vordefinierte Phrasen (FR-51)", QuickChatTeamOnly);
            r.Run("Sozial: Emotes und Ping-Markierung (FR-51)", EmotesAndPings);
            r.Run("Sozial: Melden mit Repeat-Schutz, Blockieren (FR-52)", ReportPlayer);
            r.Run("Loadout: Marker/Kosmetik nur wenn freigeschaltet (FR-35/FR-41)", LoadoutValidated);
            r.Run("Ping: pong mit Serverzeit für RTT/Uhrensync (FR-29)", PingPong);
        }

        internal static GameServer NewServer(Action<ServerOptions> configure = null)
        {
            var options = new ServerOptions { QuickMatchWaitSeconds = 2f, ResultsSeconds = 2f, LobbyCountdownSeconds = 1f };
            configure?.Invoke(options);
            return new GameServer(options, new AccountStore(AccountTests.TempDir()));
        }

        internal static void TickFor(GameServer s, float seconds)
        {
            int ticks = (int)MathF.Ceiling(seconds * GameServer.TickRate);
            for (int i = 0; i < ticks; i++) s.Tick();
        }

        internal static void TickUntil(GameServer s, Func<bool> condition, float maxSeconds = 20f)
        {
            int ticks = (int)(maxSeconds * GameServer.TickRate);
            for (int i = 0; i < ticks && !condition(); i++) s.Tick();
            Assert.IsTrue(condition(), "Bedingung nicht erreicht");
        }

        internal static (GameServer server, TestClient host, TestClient guest) TwoInPrivateRoom(string mode = "tdm", string map = "arena")
        {
            GameServer server = NewServer();
            var host = new TestClient(server, "Host");
            host.Send(new { t = "create", mode, map, @private = true });
            server.Tick();
            var guest = new TestClient(server, "Gast");
            guest.Send(new { t = "join", code = host.RoomCode });
            server.Tick();
            return (server, host, guest);
        }

        internal static void StartMatch(GameServer server, TestClient host, params TestClient[] others)
        {
            foreach (TestClient c in others) c.Send(new { t = "ready", ready = true });
            host.Send(new { t = "ready", ready = true });
            server.Tick();
            host.Send(new { t = "start" });
            TickUntil(server, () => host.Sink.Last("start") != null);
            TickUntil(server, () => host.Sink.Last("s")?.GetProperty("ph").GetString() == "running");
        }

        private static int MemberCount(JsonElement lobby) => lobby.GetProperty("members").GetArrayLength();

        // ---------------- Tests ----------------

        private static void HelloWelcome()
        {
            GameServer server = NewServer();
            var anon = new TestClient(server, "x", hello: false);
            anon.Send(new { t = "quick", mode = "tdm" });
            server.Tick();
            Assert.AreEqual("not_authenticated", anon.Sink.Last("error").Value.GetProperty("code").GetString(), "Erst anmelden");

            var c = new TestClient(server, "Omar");
            JsonElement welcome = c.Sink.Last("welcome").Value;
            Assert.AreEqual("Omar", welcome.GetProperty("name").GetString(), "Name");
            Assert.IsTrue(welcome.GetProperty("profile").GetProperty("level").GetInt32() >= 1, "Profil mitgeliefert");

            var again = new TestClient(server, "Omar", c.Token);
            Assert.AreEqual(c.AccountId, again.AccountId, "Gleiches Konto per Token (FR-49)");
            Assert.IsTrue(c.Sink.Closed, "Alte Sitzung desselben Kontos wird ersetzt");
        }

        private static void MalformedMessages()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Omar");
            c.SendRaw("{kaputt");
            c.SendRaw("[]");
            c.SendRaw("{\"t\":\"gibtsnicht\"}");
            c.SendRaw("{\"t\":\"in\",\"s\":1,\"mx\":\"NaN\",\"mz\":1e40}");
            c.SendRaw("{\"t\":\"join\",\"code\":" + new string('9', 5000) + "}");
            c.SendRaw(new string('x', 10000));
            server.Tick();
            Assert.IsTrue(c.Sink.OfType("error").Count >= 3, "Fehler werden gemeldet");
            Assert.IsFalse(c.Sink.Closed, "Verbindung bleibt bestehen");
            c.Send(new { t = "ping", c = 1.0 });
            server.Tick();
            Assert.IsTrue(c.Sink.Last("pong") != null, "Server funktioniert weiter");
        }

        private static void RateLimit()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Spammer");
            for (int second = 0; second < 5 && !c.Sink.Closed; second++)
            {
                for (int i = 0; i < 400; i++) c.Send(new { t = "ping", c = 1.0 });
                TickFor(server, 1f);
            }
            Assert.IsTrue(c.Sink.Closed, "Dauerhafter Flood trennt die Verbindung");
            Assert.AreEqual("rate_limited", c.Sink.CloseReason, "Grund angegeben");
        }

        private static void PrivateRoomInvite()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            string code = host.RoomCode;
            Assert.AreEqual(6, code.Length, "6-stelliger Code");
            Assert.IsTrue(host.Lobby.GetProperty("private").GetBoolean(), "Privat");
            Assert.AreEqual(2, MemberCount(host.Lobby), "Host sieht Gast");
            Assert.AreEqual(2, MemberCount(guest.Lobby), "Gast sieht Host");
            Assert.AreEqual(code, guest.RoomCode, "Gleicher Raum");

            var stranger = new TestClient(server, "Fremd");
            stranger.Send(new { t = "join", code = "ZZZZZZ" });
            server.Tick();
            Assert.AreEqual("room_not_found", stranger.Sink.Last("error").Value.GetProperty("code").GetString(), "Falscher Code");
            stranger.Send(new { t = "join", code = code.ToLowerInvariant() });
            server.Tick();
            Assert.AreEqual(3, MemberCount(host.Lobby), "Code unabhängig von Groß-/Kleinschreibung");
        }

        private static void ReadyGatingAndStart()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            guest.Send(new { t = "team", team = 1 });
            server.Tick();
            JsonElement guestEntry = host.Lobby.GetProperty("members").EnumerateArray().First(m => m.GetProperty("name").GetString() == "Gast");
            Assert.AreEqual(1, guestEntry.GetProperty("team").GetInt32(), "Teamwahl sichtbar");

            host.Send(new { t = "ready", ready = true });
            host.Send(new { t = "start" });
            server.Tick();
            Assert.AreEqual("not_all_ready", host.Sink.Last("error").Value.GetProperty("code").GetString(), "Start erst wenn alle bereit");

            guest.Send(new { t = "start" });
            server.Tick();
            Assert.AreEqual("not_host", guest.Sink.Last("error").Value.GetProperty("code").GetString(), "Nur Host startet");

            guest.Send(new { t = "ready", ready = true });
            server.Tick();
            host.Send(new { t = "start" });
            server.Tick();
            Assert.AreEqual("countdown", host.Lobby.GetProperty("state").GetString(), "Lobby-Countdown");
            TickUntil(server, () => guest.Sink.Last("start") != null);
            JsonElement start = guest.Sink.Last("start").Value;
            Assert.AreEqual("arena", start.GetProperty("map").GetString(), "Karte übermittelt");
            Assert.AreEqual(1, start.GetProperty("team").GetInt32(), "Gewähltes Team im Match");
            TickFor(server, 0.2f);
            Assert.IsTrue(guest.Sink.OfType("s").Count > 0, "Snapshots laufen");
        }

        private static void HostMigration()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            host.Send(new { t = "leave" });
            server.Tick();
            JsonElement lobby = guest.Lobby;
            Assert.AreEqual(1, MemberCount(lobby), "Host weg");
            Assert.AreEqual(lobby.GetProperty("you").GetInt32(), lobby.GetProperty("host").GetInt32(), "Gast ist neuer Host");
            Assert.IsTrue(host.Sink.Last("left") != null, "Host erhält Bestätigung");
        }

        private static void HostConfiguresRules()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            host.Send(new { t = "config", mode = "ctf", map = "forest", timeLimit = 120, targetScore = 2, friendlyFire = true, powerUps = false });
            host.Send(new { t = "bot", add = true });
            host.Send(new { t = "bot", add = true });
            server.Tick();
            JsonElement lobby = guest.Lobby;
            Assert.AreEqual("ctf", lobby.GetProperty("mode").GetString(), "Modus geändert");
            Assert.AreEqual("forest", lobby.GetProperty("map").GetString(), "Karte geändert");
            Assert.AreEqual(120, lobby.GetProperty("rules").GetProperty("timeLimit").GetInt32(), "Zeitlimit");
            Assert.IsTrue(lobby.GetProperty("rules").GetProperty("friendlyFire").GetBoolean(), "Friendly Fire");
            Assert.AreEqual(4, MemberCount(lobby), "2 Bots hinzugefügt");

            guest.Send(new { t = "config", mode = "tdm" });
            server.Tick();
            Assert.AreEqual("not_host", guest.Sink.Last("error").Value.GetProperty("code").GetString(), "Gast darf nicht konfigurieren");

            host.Send(new { t = "config", timeLimit = 999999, targetScore = -5 });
            server.Tick();
            JsonElement rules = guest.Lobby.GetProperty("rules");
            Assert.IsTrue(rules.GetProperty("timeLimit").GetInt32() <= 3600, "Zeitlimit geklemmt (CustomGameRules)");
            Assert.IsTrue(rules.GetProperty("targetScore").GetInt32() >= 1, "Zielpunkte geklemmt");
        }

        private static void QuickMatchFillsBots()
        {
            GameServer server = NewServer();
            var a = new TestClient(server, "A");
            var b = new TestClient(server, "B");
            a.Send(new { t = "quick", mode = "tdm" });
            b.Send(new { t = "quick", mode = "tdm" });
            server.Tick();
            Assert.AreEqual(a.RoomCode, b.RoomCode, "Beide im selben Quick-Match");
            Assert.IsFalse(a.Lobby.GetProperty("private").GetBoolean(), "Öffentlich");
            TickUntil(server, () => a.Sink.Last("start") != null);
            JsonElement start = a.Sink.Last("start").Value;
            JsonElement[] players = start.GetProperty("players").EnumerateArray().ToArray();
            Assert.IsTrue(players.Length >= 8, $"Mindestens 8 Spieler (FR-22), waren {players.Length}");
            int t0 = players.Count(p => p.GetProperty("team").GetInt32() == 0);
            int t1 = players.Count(p => p.GetProperty("team").GetInt32() == 1);
            Assert.IsTrue(Math.Abs(t0 - t1) <= 1, $"Ausgeglichene Teams ({t0}:{t1})");
            Assert.IsTrue(players.Count(p => p.GetProperty("bot").GetBoolean()) >= 6, "Bots aufgefüllt");
        }

        private static void CrossPlayOffSeparates()
        {
            GameServer server = NewServer(o => o.QuickMatchWaitSeconds = 30f);
            var mouse = new TestClient(server, "Maus", input: "kbm", crossPlay: false);
            var touch = new TestClient(server, "Touch", input: "touch", crossPlay: false);
            var mixed = new TestClient(server, "Egal", input: "touch", crossPlay: true);
            mouse.Send(new { t = "quick", mode = "tdm" });
            touch.Send(new { t = "quick", mode = "tdm" });
            server.Tick();
            Assert.IsTrue(mouse.RoomCode != touch.RoomCode, "Getrennte Pools ohne Cross-Play");
            mixed.Send(new { t = "quick", mode = "tdm" });
            server.Tick();
            Assert.IsTrue(mixed.RoomCode != mouse.RoomCode, "Cross-Play-Spieler nicht in Maus-only-Raum");
        }

        private static void QueueStatusReported()
        {
            GameServer server = NewServer(o => o.QuickMatchWaitSeconds = 5f);
            var a = new TestClient(server, "A");
            a.Send(new { t = "quick", mode = "ffa" });
            TickFor(server, 1.5f);
            JsonElement q = a.Sink.Last("queue").Value;
            Assert.IsTrue(q.GetProperty("waited").GetDouble() >= 1f, "Wartezeit");
            Assert.IsTrue(q.GetProperty("startsIn").GetDouble() > 0f, "Start-Countdown");
            Assert.AreEqual(1, q.GetProperty("humans").GetInt32(), "Anzahl Menschen");
        }

        private static void TrainingStartsWithBots()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Neuling");
            c.Send(new { t = "create", mode = "training", map = "warehouse", bots = 5, skill = 0.3 });
            TickUntil(server, () => c.Sink.Last("start") != null, 5f);
            JsonElement start = c.Sink.Last("start").Value;
            Assert.AreEqual(6, start.GetProperty("players").GetArrayLength(), "Spieler + 5 Bots");
            Assert.IsFalse(start.GetProperty("ranked").GetBoolean(), "Training ist nicht gewertet");
        }

        private static void InputsMoveAndAck()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            StartMatch(server, host, guest);
            int me = host.SimId;
            double z0 = host.SnapshotPlayer(me).Value[3].GetDouble();
            double x0 = host.SnapshotPlayer(me).Value[1].GetDouble();
            double yaw = Math.Atan2(-x0, -z0);
            for (int i = 0; i < 15; i++) { host.Move(0f, 1f, (float)yaw); server.Tick(); }
            server.Tick();
            JsonElement snap = host.LastSnapshot;
            Assert.AreEqual(15, snap.GetProperty("ack").GetInt32(), "Sequenz bestätigt");
            JsonElement p = host.SnapshotPlayer(me).Value;
            double moved = Math.Sqrt(Math.Pow(p[1].GetDouble() - x0, 2) + Math.Pow(p[3].GetDouble() - z0, 2));
            Assert.IsTrue(moved > 2.0, $"Spieler bewegt ({moved:0.00} m)");
            Assert.IsTrue(snap.GetProperty("me").GetProperty("am").GetInt32() > 0, "Eigene Munition im Snapshot");
        }

        private static void InterestManagementHidesEnemies()
        {
            var (server, host, guest) = TwoInPrivateRoom("tdm", "warehouse");
            guest.Send(new { t = "team", team = 1 });
            server.Tick();
            StartMatch(server, host, guest);
            Room room = server.FindRoom(host.RoomCode);
            var match = room.Match;
            int hostId = host.SimId, guestId = guest.SimId;
            // Hohe Wand zwischen die beiden setzen (Container mit 1,6 m verdecken stehende Köpfe bewusst nicht)
            match.World.AddBox(new Paintball.Net.Simulation.Aabb(new System.Numerics.Vector3(-13f, 0f, -5.5f), new System.Numerics.Vector3(-7f, 4f, -4.5f)));
            match.Teleport(hostId, new System.Numerics.Vector3(-10f, 0f, -8f), 0f);
            match.Teleport(guestId, new System.Numerics.Vector3(-10f, 0f, -2f), 0f);
            server.Tick();
            Assert.IsTrue(host.SnapshotPlayer(guestId) == null, "Gegner hinter Container unsichtbar");
            match.Teleport(guestId, new System.Numerics.Vector3(-4f, 0f, -8f), 0f);
            server.Tick();
            Assert.IsTrue(host.SnapshotPlayer(guestId) != null, "Gegner mit Sichtlinie sichtbar");
            Assert.IsTrue(host.SnapshotPlayer(hostId) != null, "Eigener Spieler immer sichtbar");
        }

        private static void MatchEndRewards()
        {
            GameServer server = NewServer();
            var host = new TestClient(server, "Host");
            host.Send(new { t = "create", mode = "tdm", map = "arena", @private = true, timeLimit = 30 });
            server.Tick();
            var guest = new TestClient(server, "Gast");
            guest.Send(new { t = "join", code = host.RoomCode });
            guest.Send(new { t = "team", team = 1 });
            server.Tick();
            StartMatch(server, host, guest);
            int xpBefore = server.Accounts.GetAccount(host.AccountId).TotalXp;
            // Beide bleiben aktiv (kein AFK), Match läuft per Zeitlimit aus
            TickUntil(server, () =>
            {
                host.Move(0f, 0f); guest.Move(0f, 0f);
                return host.Sink.Last("end") != null;
            }, 40f);
            JsonElement end = host.Sink.Last("end").Value;
            JsonElement you = end.GetProperty("you");
            Assert.IsTrue(you.GetProperty("rewarded").GetBoolean(), "Belohnung nach validiertem Abschluss");
            Assert.IsTrue(you.GetProperty("xp").GetInt32() > 0, "XP gewonnen");
            Assert.IsTrue(end.GetProperty("table").GetArrayLength() == 2, "Ergebnistabelle");
            Assert.IsTrue(server.Accounts.GetAccount(host.AccountId).TotalXp > xpBefore, "XP im Konto persistiert");
            Assert.IsTrue(server.Accounts.GetProfile(host.AccountId).Coins > 0, "Münzen gutgeschrieben");
        }

        private static void ReturnToLobbyAfterResults()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            host.Send(new { t = "config", timeLimit = 30 });
            server.Tick();
            StartMatch(server, host, guest);
            TickUntil(server, () => { host.Move(0, 0); guest.Move(0, 0); return host.Sink.Last("end") != null; }, 40f);
            Assert.AreEqual("results", host.Lobby.GetProperty("state").GetString(), "Ergebnisphase");
            TickUntil(server, () => host.Lobby.GetProperty("state").GetString() == "lobby", 5f);
            Assert.AreEqual(2, MemberCount(host.Lobby), "Gruppe bleibt zusammen (FR-30)");
        }

        private static void ReconnectWithinGrace()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            StartMatch(server, host, guest);
            int guestSim = guest.SimId;
            server.Disconnect(guest.Session);
            TickFor(server, 1f);
            Room room = server.FindRoom(host.RoomCode);
            var sim = room.Match.Find(guestSim);
            Assert.IsFalse(sim.Connected, "Als getrennt markiert");
            Assert.IsTrue(room.FindMemberBySim(guestSim).Connected == false, "Slot bleibt reserviert");

            var back = new TestClient(server, "Gast", guest.Token);
            server.Tick();
            Assert.AreEqual(guestSim, back.SimId, "Gleicher Spieler nach Reconnect");
            Assert.IsTrue(sim.Connected, "Wieder verbunden");
            TickFor(server, 0.2f);
            Assert.IsTrue(back.Sink.OfType("s").Count > 0, "Snapshots nach Reconnect");
        }

        private static void LeaverAfterGrace()
        {
            GameServer server = NewServer(o => o.ReconnectGraceSeconds = 2f);
            var host = new TestClient(server, "Host");
            host.Send(new { t = "create", mode = "tdm", map = "arena", @private = true, timeLimit = 30 });
            server.Tick();
            var guest = new TestClient(server, "Gast");
            guest.Send(new { t = "join", code = host.RoomCode });
            server.Tick();
            StartMatch(server, host, guest);
            int guestSim = guest.SimId;
            server.Disconnect(guest.Session);
            TickFor(server, 3f);
            Room room = server.FindRoom(host.RoomCode);
            Assert.IsTrue(room.Match.Find(guestSim).IsBot, "Bot ersetzt Leaver (Ersatzsuche)");
            var back = new TestClient(server, "Gast", guest.Token);
            server.Tick();
            Assert.IsTrue(back.Sink.Last("start") == null, "Kein Rückweg nach Fristablauf");
            int xpBefore = server.Accounts.GetAccount(guest.AccountId).TotalXp;
            TickUntil(server, () => { host.Move(0, 0); return host.Sink.Last("end") != null; }, 40f);
            Assert.AreEqual(xpBefore, server.Accounts.GetAccount(guest.AccountId).TotalXp, "Leaver erhält keine XP");
        }

        private static void AfkKick()
        {
            GameServer server = NewServer(o => o.AfkTimeoutSeconds = 3f);
            var (s2, host, guest) = (server, new TestClient(server, "Host"), (TestClient)null);
            host.Send(new { t = "create", mode = "tdm", map = "arena", @private = true });
            server.Tick();
            guest = new TestClient(server, "Schläfer");
            guest.Send(new { t = "join", code = host.RoomCode });
            server.Tick();
            StartMatch(server, host, guest);
            int guestSim = guest.SimId;
            TickFor(server, 1f);
            TickUntil(server, () => { host.Move(0.5f, 0f); return guest.Sink.Last("kicked") != null; }, 8f);
            Assert.AreEqual("afk", guest.Sink.Last("kicked").Value.GetProperty("reason").GetString(), "Grund AFK");
            Room room = server.FindRoom(host.RoomCode);
            Assert.IsTrue(room.Match.Find(guestSim).IsBot, "Bot übernimmt Slot");
            Assert.IsTrue(host.Sink.Last("kicked") == null, "Aktiver Spieler bleibt");
        }

        private static void QuickChatTeamOnly()
        {
            GameServer server = NewServer();
            var a = new TestClient(server, "A");
            a.Send(new { t = "create", mode = "tdm", map = "arena", @private = true });
            server.Tick();
            var mate = new TestClient(server, "Mate");
            var enemy = new TestClient(server, "Enemy");
            mate.Send(new { t = "join", code = a.RoomCode });
            mate.Send(new { t = "team", team = 0 });
            enemy.Send(new { t = "join", code = a.RoomCode });
            enemy.Send(new { t = "team", team = 1 });
            server.Tick();
            StartMatch(server, a, mate, enemy);
            a.Send(new { t = "chat", id = 2 });
            server.Tick();
            Assert.IsTrue(mate.Sink.Last("chat") != null, "Teamkamerad erhält Nachricht");
            Assert.AreEqual(2, mate.Sink.Last("chat").Value.GetProperty("id").GetInt32(), "Phrasen-ID");
            Assert.IsTrue(enemy.Sink.Last("chat") == null, "Gegner hört nicht mit");
            a.Send(new { t = "chat", id = 999 });
            server.Tick();
            Assert.AreEqual("invalid_chat", a.Sink.Last("error").Value.GetProperty("code").GetString(), "Nur gültige Phrasen");
            for (int i = 0; i < 10; i++) a.Send(new { t = "chat", id = 1 });
            server.Tick();
            Assert.IsTrue(mate.Sink.OfType("chat").Count <= 4, "Chat-Spam gedrosselt");
        }

        private static void EmotesAndPings()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            guest.Send(new { t = "team", team = 0 });
            server.Tick();
            StartMatch(server, host, guest);
            host.Send(new { t = "emote", id = 1 });
            host.Send(new { t = "mark", x = 3.0, y = 0.0, z = 4.0 });
            server.Tick();
            Assert.IsTrue(guest.Sink.Last("emote") != null, "Emote sichtbar");
            JsonElement mark = guest.Sink.Last("mark").Value;
            Assert.AreEqual(3.0, mark.GetProperty("x").GetDouble(), "Ping-Position (Team)");
        }

        private static void ReportPlayer()
        {
            var (server, host, guest) = TwoInPrivateRoom();
            StartMatch(server, host, guest);
            host.Send(new { t = "report", player = guest.SimId, reason = "toxicity" });
            server.Tick();
            Assert.IsTrue(host.Sink.Last("reported").Value.GetProperty("accepted").GetBoolean(), "Meldung angenommen");
            host.Send(new { t = "report", player = guest.SimId, reason = "toxicity" });
            host.Send(new { t = "report", player = guest.SimId, reason = "toxicity" });
            server.Tick();
            Assert.IsFalse(host.Sink.Last("reported").Value.GetProperty("accepted").GetBoolean(), "Repeat-Schutz (max. 2 pro Paar, Core ReportEvaluator)");
        }

        private static void LoadoutValidated()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Neu");
            c.Send(new { t = "loadout", marker = "precision" });
            server.Tick();
            Assert.AreEqual("locked", c.Sink.Last("error").Value.GetProperty("code").GetString(), "Präzision noch gesperrt");
            server.Accounts.GetAccount(c.AccountId).AddXp(5000);
            c.Send(new { t = "loadout", marker = "precision", paint = "paint_cyan" });
            server.Tick();
            JsonElement profile = c.Sink.Last("profile").Value;
            Assert.AreEqual("precision", profile.GetProperty("marker").GetString(), "Marker ausgerüstet");
            Assert.AreEqual("paint_cyan", profile.GetProperty("paint").GetString(), "Farbe ausgerüstet");
        }

        private static void PingPong()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "P");
            c.Send(new { t = "ping", c = 1234.5 });
            server.Tick();
            JsonElement pong = c.Sink.Last("pong").Value;
            Assert.AreEqual(1234.5, pong.GetProperty("c").GetDouble(), "Client-Zeit gespiegelt");
            Assert.IsTrue(pong.TryGetProperty("st", out _), "Serverzeit");
        }
    }
}
