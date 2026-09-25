using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paintball.Net.Accounts;

namespace Paintball.Net.Tests
{
    /// <summary>Hintergrund-Warteschlange für Spielstände: Zusammenfassen, Reihenfolge, Forget, Wiederholung, Flush.</summary>
    internal static class PersistenceQueueTests
    {
        private static readonly int[] FastRetry = { 1, 1, 1 };
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        public static void Register(TestRunner r)
        {
            r.RunAsync("Queue: mehrere Saves eines Spielers werden zusammengefasst", Coalesces);
            r.RunAsync("Queue: Reihenfolge Match vor Save bleibt erhalten", MatchBeforeSave);
            r.RunAsync("Queue: Save vor Match bleibt vor dem Match, späterer Save wird nicht nach vorn gezogen", SaveMatchSaveOrder);
            r.RunAsync("Queue: Fehler werden bis zu 3-mal wiederholt, dann verworfen", RetriesThenDrops);
            r.RunAsync("Queue: Forget verwirft offene Aufträge", ForgetDropsPending);
            r.RunAsync("Queue: Save nach Forget wird wieder geschrieben", SaveAfterForgetIsWritten);
            r.RunAsync("Queue: HasPending und Pending", PendingCounts);
            r.RunAsync("Queue: FlushAsync wartet nur auf vorher eingereihte Aufträge", FlushOnlyAwaitsEarlierJobs);
            r.RunAsync("Queue: viele gleichzeitige Erzeuger, nichts geht verloren", ConcurrentProducers);
            r.Run("Queue: Inline schreibt synchron", InlineIsSynchronous);
            r.Run("Queue: Inline gibt Fehler weiter", InlineRethrows);
            r.RunAsync("Queue: DisposeAsync schreibt Offenes weg", DisposeFlushes);
            r.RunAsync("Queue: nach DisposeAsync wird synchron geschrieben", EnqueueAfterDispose);
            r.RunAsync("Queue: während DisposeAsync eingereihte Aufträge werden noch geschrieben", EnqueueDuringDispose);
            r.RunAsync("Queue: DisposeAsync verwirft nach Zeitlimit den Rest und zählt ihn", DisposeTimeoutDrops);
            r.RunAsync("Queue: Abbruch während einer Wiederholung zählt als Verlust", AbortDuringRetryCounted);
            r.RunAsync("Queue: Wiederholungen ausgeschöpft nach gleichzeitigem Forget markiert keinen Fehler (Fix Runde 2)", RetriesExhaustedAfterForgetSkipsMarkFailed);
        }

        private static PlayerRecord Snap(string id, int xp) => new PlayerRecord { Id = id, Xp = xp };
        private static MatchRecord Match(int kills = 1) => new MatchRecord { Kills = kills };

        private static async Task Coalesces()
        {
            var repo = RecordingRepository.Paused();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("p", 1));
            repo.WaitUntilFirstCallBlocks();            // #1 läuft jetzt
            q.EnqueueSave(Snap("p", 2));
            q.EnqueueSave(Snap("p", 3));
            q.EnqueueSave(Snap("p", 4));
            repo.Release();
            await q.FlushAsync(FlushTimeout);
            var saves = repo.CallsFor("p").Where(c => c.Op == "save").ToList();
            Assert.AreEqual(2, saves.Count, "genau 2 SaveProgress (#1 und zusammengefasst #2 bis #4)");
            Assert.AreEqual(4, saves.Last().Xp, "der letzte Save trägt den neuesten Stand");
        }

        private static async Task MatchBeforeSave()
        {
            var repo = new RecordingRepository();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueMatch("p", Match());
            q.EnqueueSave(Snap("p", 5));
            await q.FlushAsync(FlushTimeout);
            var ops = repo.CallsFor("p").Select(c => c.Op).ToList();
            Assert.AreEqual("match,save", string.Join(",", ops), "AddMatch vor SaveProgress");
        }

        private static async Task SaveMatchSaveOrder()
        {
            var repo = RecordingRepository.Paused();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("a", 1));                // hält den Worker fest
            repo.WaitUntilFirstCallBlocks();
            q.EnqueueSave(Snap("p", 1));
            q.EnqueueMatch("p", Match());
            q.EnqueueSave(Snap("p", 2));
            q.EnqueueSave(Snap("p", 3));
            repo.Release();
            await q.FlushAsync(FlushTimeout);
            string log = string.Join(",", repo.CallsFor("p").Select(c => c.Op == "save" ? "save" + c.Xp : c.Op));
            Assert.AreEqual("save1,match,save3", log, "Save 2/3 dürfen nicht vor das Match rutschen");
        }

        private static async Task RetriesThenDrops()
        {
            var ok = new RecordingRepository { FailTimes = 2 };
            await using (var q = PersistenceQueue.Background(ok, FastRetry))
            {
                q.EnqueueSave(Snap("p", 7));
                await q.FlushAsync(FlushTimeout);
                Assert.AreEqual(1, ok.CallsFor("p").Count(c => c.Op == "save"), "nach 2 Fehlern geschrieben");
                Assert.AreEqual(0L, q.Failures, "kein endgültiger Fehler");
                Assert.IsTrue(q.LastErrorAt == null, "kein LastErrorAt");
            }

            var bad = new RecordingRepository { FailTimes = 5 };
            await using (var q = PersistenceQueue.Background(bad, FastRetry))
            {
                q.EnqueueSave(Snap("p", 7));
                await q.FlushAsync(FlushTimeout);
                Assert.AreEqual(0, bad.CallsFor("p").Count, "verworfen");
                Assert.AreEqual(4, bad.Attempts, "1 Versuch + 3 Wiederholungen");
                Assert.AreEqual(1L, q.Failures, "ein endgültiger Fehler");
                Assert.IsTrue(q.LastErrorAt != null, "LastErrorAt gesetzt");
                Assert.AreEqual(0, q.Pending, "nichts mehr offen");
            }
        }

        private static async Task ForgetDropsPending()
        {
            var repo = RecordingRepository.Paused();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("a", 1));
            repo.WaitUntilFirstCallBlocks();
            q.EnqueueSave(Snap("b", 1));
            q.EnqueueMatch("b", Match());
            q.Forget("b");
            Assert.IsTrue(q.HasPending("b"), "Aufträge von B liegen noch in der Schlange (werden leer durchlaufen)");
            repo.Release();
            await q.FlushAsync(FlushTimeout);
            Assert.AreEqual(0, repo.CallsFor("b").Count, "für B gibt es keinen Aufruf");
            Assert.AreEqual(1, repo.CallsFor("a").Count, "A wurde geschrieben");
            Assert.IsFalse(q.HasPending("b"), "B ist danach nicht mehr offen");
            Assert.AreEqual(0, q.Pending, "nichts mehr offen");
        }

        private static async Task SaveAfterForgetIsWritten()
        {
            var repo = RecordingRepository.Paused();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("a", 1));
            repo.WaitUntilFirstCallBlocks();
            q.EnqueueSave(Snap("b", 1));
            q.Forget("b");
            q.EnqueueSave(Snap("b", 9));                // noch während der alte Job in der Schlange steht
            repo.Release();
            await q.FlushAsync(FlushTimeout);
            string log = string.Join(",", repo.CallsFor("b").Select(c => c.Op + c.Xp));
            Assert.AreEqual("save9", log, "nur der Save nach Forget wird geschrieben");

            q.Forget("b");                              // nichts offen: darf keinen Zustand hinterlassen
            q.EnqueueSave(Snap("b", 10));
            await q.FlushAsync(FlushTimeout);
            Assert.AreEqual(10, repo.CallsFor("b").Last().Xp, "auch ein späterer Save wird geschrieben");
            Assert.AreEqual(0, q.Pending, "nichts mehr offen");
        }

        private static async Task PendingCounts()
        {
            var repo = new RecordingRepository { DelayMs = 100 };
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            Assert.IsFalse(q.HasPending("p"), "anfangs nichts offen");
            q.EnqueueSave(Snap("p", 1));
            Assert.IsTrue(q.HasPending("p"), "HasPending während der Auftrag offen ist");
            Assert.IsTrue(q.Pending >= 1, "Pending >= 1");
            Assert.IsFalse(q.HasPending("x"), "anderer Spieler nicht offen");
            await q.FlushAsync(FlushTimeout);
            Assert.IsFalse(q.HasPending("p"), "nach Flush nicht mehr offen");
            Assert.AreEqual(0, q.Pending, "nach Flush 0");
        }

        private static async Task FlushOnlyAwaitsEarlierJobs()
        {
            var repo = new RecordingRepository { DelayMs = 150 };
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("first", 1));
            Task flush = q.FlushAsync(FlushTimeout);
            for (int i = 0; i < 5; i++) q.EnqueueSave(Snap("later" + i, 1));
            await flush;
            Assert.AreEqual(1, repo.CallsFor("first").Count, "vorher eingereihter Auftrag ist geschrieben");
            Assert.IsTrue(q.Pending >= 3, $"später eingereihte Aufträge wurden nicht abgewartet (Pending {q.Pending})");
            await q.FlushAsync(FlushTimeout);
            Assert.AreEqual(0, q.Pending, "zweiter Flush leert alles");
        }

        private static async Task ConcurrentProducers()
        {
            var repo = new RecordingRepository();
            await using var q = PersistenceQueue.Background(repo, FastRetry);
            const int threads = 8, perThread = 300;
            var start = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                start.Wait();
                for (int i = 1; i <= perThread; i++)
                {
                    string id = "p" + (i % 10);          // Spieler werden von allen Threads geteilt
                    q.EnqueueMatch(id, Match());
                    q.EnqueueSave(Snap(id, t * 100000 + i));
                    if (i % 50 == 0) _ = q.FlushAsync(FlushTimeout);
                }
            })).ToArray();
            start.Set();
            await Task.WhenAll(tasks);
            await q.FlushAsync(FlushTimeout);
            Assert.AreEqual(0, q.Pending, "alles geschrieben");
            Assert.AreEqual(threads * perThread, repo.Calls.Count(c => c.Op == "match"), "jedes Match genau einmal");
            for (int p = 0; p < 10; p++)
            {
                var calls = repo.CallsFor("p" + p);
                Assert.AreEqual(threads * perThread / 10, calls.Count(c => c.Op == "match"), $"p{p}: alle Matches");
                Assert.IsTrue(calls.Count(c => c.Op == "save") >= 1, $"p{p}: mindestens ein Save");
                for (int t = 0; t < threads; t++)
                {
                    // Reihenfolge je Erzeuger bleibt erhalten: Saves eines Threads erscheinen mit steigendem Xp.
                    var xs = calls.Where(c => c.Op == "save" && c.Xp / 100000 == t).Select(c => c.Xp).ToList();
                    for (int k = 1; k < xs.Count; k++)
                        Assert.IsTrue(xs[k] > xs[k - 1], $"p{p}/Thread {t}: Saves in Reihenfolge");
                }
                Assert.IsFalse(q.HasPending("p" + p), $"p{p}: nicht mehr offen");
            }
            Assert.AreEqual(0L, q.Failures, "keine Fehler");
        }

        private static void InlineIsSynchronous()
        {
            var repo = new RecordingRepository();
            var q = PersistenceQueue.Inline(repo);
            q.EnqueueSave(Snap("p", 3));
            Assert.AreEqual(1, repo.CallsFor("p").Count, "direkt nach EnqueueSave protokolliert");
            q.EnqueueMatch("p", Match());
            Assert.AreEqual(2, repo.CallsFor("p").Count, "direkt nach EnqueueMatch protokolliert");
            Assert.AreEqual(0, q.Pending, "Inline hat nie Offenes");
            Assert.IsTrue(q.FlushAsync(FlushTimeout).IsCompleted, "Flush sofort fertig");
        }

        private static void InlineRethrows()
        {
            var repo = new RecordingRepository { FailTimes = 1 };
            var q = PersistenceQueue.Inline(repo);
            bool thrown = false;
            try { q.EnqueueSave(Snap("p", 3)); }
            catch (InvalidOperationException) { thrown = true; }
            Assert.IsTrue(thrown, "Ausnahme kommt beim Aufrufer an");
            Assert.AreEqual(1L, q.Failures, "Fehler gezählt");
            Assert.IsTrue(q.LastErrorAt != null, "LastErrorAt gesetzt");
        }

        private static async Task DisposeFlushes()
        {
            var repo = new RecordingRepository();
            var q = PersistenceQueue.Background(repo, FastRetry);
            for (int i = 0; i < 50; i++) q.EnqueueSave(Snap("p" + i, i));
            await q.DisposeAsync();
            Assert.AreEqual(50, repo.Calls.Count(c => c.Op == "save"), "50 SaveProgress-Aufrufe");
            Assert.AreEqual(0, q.Pending, "nichts offen");
        }

        private static async Task EnqueueAfterDispose()
        {
            var repo = new RecordingRepository();
            var q = PersistenceQueue.Background(repo, FastRetry);
            await q.DisposeAsync();
            q.EnqueueSave(Snap("p", 1));
            q.EnqueueMatch("p", Match());
            Assert.AreEqual("save,match", string.Join(",", repo.CallsFor("p").Select(c => c.Op)), "synchron geschrieben");
            Assert.AreEqual(0, q.Pending, "nichts offen");
            await q.DisposeAsync();                     // zweiter Aufruf ist harmlos
        }

        private static async Task EnqueueDuringDispose()
        {
            var repo = RecordingRepository.Paused();
            repo.HoldPlayer("b");                       // B-Aufrufe hängen, bis wir sie freigeben
            var q = PersistenceQueue.Background(repo, FastRetry);
            q.EnqueueSave(Snap("a", 1));
            repo.WaitUntilFirstCallBlocks();
            Task dispose = q.DisposeAsync().AsTask();   // läuft bis zum Flush-Warten synchron
            Assert.IsFalse(dispose.IsCompleted, "Dispose wartet auf den blockierten Auftrag");
            q.EnqueueMatch("b", Match());               // Erzeuger arbeitet während des Herunterfahrens weiter
            q.EnqueueSave(Snap("b", 2));
            repo.Release();                             // A fertig: der erste Flush von Dispose ist erfüllt
            repo.WaitUntilHeldPlayerBlocks();           // Worker steckt im Match von B, der Save von B ist noch offen
            await Task.Delay(100);                      // Dispose hat Zeit, (fälschlich) abzubrechen
            repo.ReleaseHeldPlayer();
            await dispose;
            Assert.AreEqual("match,save", string.Join(",", repo.CallsFor("b").Select(c => c.Op)), "B wurde in Reihenfolge geschrieben");
            Assert.AreEqual(0, q.DroppedOnShutdown, "nichts verworfen");
            Assert.AreEqual(0L, q.Failures, "keine Fehler");
            Assert.AreEqual(0, q.Pending, "nichts offen");
        }

        private static async Task DisposeTimeoutDrops()
        {
            var repo = RecordingRepository.Paused();
            var q = PersistenceQueue.Background(repo, FastRetry);
            q.DisposeFlushTimeout = TimeSpan.FromMilliseconds(100);
            q.EnqueueSave(Snap("a", 1));
            repo.WaitUntilFirstCallBlocks();
            q.EnqueueSave(Snap("b", 1));
            Task dispose = q.DisposeAsync().AsTask();
            await Task.Delay(300);                      // Zeitlimit ist abgelaufen, A hängt noch
            Assert.IsFalse(dispose.IsCompleted, "Dispose wartet auf den laufenden Schreibaufruf");
            repo.Release();
            await dispose;
            Assert.AreEqual(1, repo.CallsFor("a").Count, "laufender Aufruf durfte zu Ende laufen");
            Assert.AreEqual(0, repo.CallsFor("b").Count, "B wurde verworfen");
            Assert.AreEqual(1, q.DroppedOnShutdown, "ein verworfener Auftrag");
            Assert.AreEqual(1L, q.Failures, "als Fehler gezählt");
            Assert.AreEqual(0, q.Pending, "nichts offen");
        }

        private static async Task AbortDuringRetryCounted()
        {
            var repo = new RecordingRepository { FailTimes = 100 };
            var q = PersistenceQueue.Background(repo, new[] { 5000, 5000, 5000 });
            q.DisposeFlushTimeout = TimeSpan.FromMilliseconds(100);
            q.EnqueueSave(Snap("a", 1));
            await q.DisposeAsync();                     // bricht die Wartezeit vor der Wiederholung ab
            Assert.AreEqual(1, q.DroppedOnShutdown, "Abbruch in der Wiederholung gezählt");
            Assert.AreEqual(1L, q.Failures, "als Fehler gezählt");
            Assert.AreEqual(0, q.Pending, "nichts offen");
        }

        /// <summary>
        /// Controller-Review Task 6, Fix Runde 2: die Wiederholungen-ausgeschöpft-Verzweigung markierte bislang immer,
        /// selbst wenn ein gleichzeitiger Forget (Konto gelöscht) den Job längst nicht mehr wollte – anders als die
        /// übrigen Verwerf-Zweige, die vorher prüfen, ob der Job noch zur aktuellen Epoche gehört. Ohne Wiederholungen
        /// (leeres Delay-Array) ist bereits der erste, blockierte Versuch der "letzte"; Forget läuft während er noch im
        /// Repository steckt, danach wirft das Repository.
        /// </summary>
        private static async Task RetriesExhaustedAfterForgetSkipsMarkFailed()
        {
            var repo = RecordingRepository.Paused();
            repo.FailTimes = 1;   // der einzige Versuch (kein Retry-Budget) schlägt fehl, sobald er weiterlaufen darf
            await using var q = PersistenceQueue.Background(repo, Array.Empty<int>());
            q.EnqueueSave(Snap("p", 1));
            repo.WaitUntilFirstCallBlocks();   // Versuch steckt im Repository
            q.Forget("p");                     // Konto gelöscht, während der letzte Versuch noch läuft
            repo.Release();                    // jetzt wirft das Repository
            await q.FlushAsync(FlushTimeout);
            Assert.IsFalse(q.HasFailed("p"), "kein verwaister Fehlmarkierungs-Eintrag nach Forget");
        }
    }

    /// <summary>
    /// Umhüllt <see cref="InMemoryPlayerRepository"/> und protokolliert SaveProgress/AddMatch threadsicher.
    /// <see cref="FailTimes"/>: die ersten N Schreibaufrufe werfen; <see cref="DelayMs"/>: jeder Schreibaufruf wartet.
    /// </summary>
    internal sealed class RecordingRepository : IPlayerRepository
    {
        public readonly struct Call
        {
            public Call(string op, string playerId, int xp) { Op = op; PlayerId = playerId; Xp = xp; }
            public string Op { get; }
            public string PlayerId { get; }
            public int Xp { get; }
        }

        private readonly IPlayerRepository _inner = new InMemoryPlayerRepository();
        private readonly object _lock = new();
        private readonly List<Call> _calls = new();
        private int _attempts;
        public int FailTimes;
        public int DelayMs;
        private ManualResetEventSlim _pause;                 // blockiert nur den ersten Schreibaufruf
        private readonly ManualResetEventSlim _firstBlocked = new(false);
        private int _entered;

        /// <summary>Der erste Schreibaufruf bleibt stehen, bis <see cref="Release"/> gerufen wird.</summary>
        public static RecordingRepository Paused() => new RecordingRepository { _pause = new ManualResetEventSlim(false) };

        public void WaitUntilFirstCallBlocks()
        {
            if (!_firstBlocked.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("erster Schreibaufruf kam nicht an");
        }

        public void Release() => _pause.Set();

        private string _holdId;
        private readonly ManualResetEventSlim _hold = new(false), _holdEntered = new(false);

        /// <summary>Alle Schreibaufrufe dieses Spielers warten, bis <see cref="ReleaseHeldPlayer"/> gerufen wird.</summary>
        public void HoldPlayer(string playerId) => _holdId = playerId;

        public void WaitUntilHeldPlayerBlocks()
        {
            if (!_holdEntered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Aufruf des gehaltenen Spielers kam nicht an");
        }

        public void ReleaseHeldPlayer() => _hold.Set();

        public int Attempts { get { lock (_lock) return _attempts; } }
        public List<Call> Calls { get { lock (_lock) return _calls.ToList(); } }
        public List<Call> CallsFor(string playerId) { lock (_lock) return _calls.Where(c => c.PlayerId == playerId).ToList(); }

        private void Write(string op, string playerId, int xp)
        {
            if (_pause != null && Interlocked.Increment(ref _entered) == 1)
            {
                _firstBlocked.Set();
                _pause.Wait(TimeSpan.FromSeconds(10));
            }
            if (_holdId != null && playerId == _holdId)
            {
                _holdEntered.Set();
                _hold.Wait(TimeSpan.FromSeconds(10));
            }
            if (DelayMs > 0) Thread.Sleep(DelayMs);
            lock (_lock)
            {
                _attempts++;
                if (_attempts <= FailTimes) throw new InvalidOperationException("Datenbank nicht erreichbar (Test)");
                _calls.Add(new Call(op, playerId, xp));
            }
        }

        public void SaveProgress(PlayerRecord player) { Write("save", player.Id, player.Xp); _inner.SaveProgress(player); }
        public void AddMatch(string playerId, MatchRecord match) { Write("match", playerId, 0); _inner.AddMatch(playerId, match); }

        public PlayerRecord FindBySub(string googleSub) => _inner.FindBySub(googleSub);
        public PlayerRecord Get(string playerId) => _inner.Get(playerId);
        public PlayerRecord Create(string googleSub, string email) => _inner.Create(googleSub, email);
        public void RecordLogin(string playerId, string email, DateTime when) => _inner.RecordLogin(playerId, email, when);
        public NameResult TrySetName(string playerId, string name) => _inner.TrySetName(playerId, name);
        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit) => _inner.RecentMatches(playerId, limit);
        public IReadOnlyList<PlayerRecord> TopByMmr(int limit) => _inner.TopByMmr(limit);
        public int Count() => _inner.Count();
        public bool Delete(string playerId) => _inner.Delete(playerId);
        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt) => _inner.CreateSession(tokenHash, playerId, expiresAt);
        public string PlayerIdForSession(string tokenHash, DateTime now) => _inner.PlayerIdForSession(tokenHash, now);
        public void DeleteSession(string tokenHash) => _inner.DeleteSession(tokenHash);
        public void DeleteSessionsOf(string playerId) => _inner.DeleteSessionsOf(playerId);
        public int DeleteExpiredSessions(DateTime now) => _inner.DeleteExpiredSessions(now);
    }
}
