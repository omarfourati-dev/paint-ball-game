using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Paintball.Net.Accounts
{
    /// <summary>
    /// Schreibt Spielstände im Hintergrund und hält so Datenbank-Latenz vom Spieltakt fern (Spec 2.1).
    /// <list type="bullet">
    /// <item>Ein einziger Worker arbeitet die Aufträge in Einreihungsreihenfolge ab – die Reihenfolge pro Spieler bleibt erhalten.</item>
    /// <item>Saves werden zusammengefasst: Ist der letzte eingereihte Auftrag eines Spielers ein noch nicht begonnener Save,
    ///   ersetzt ein neuer Save nur dessen Stand. Nach einem Match wird nicht mehr in einen älteren Save zusammengefasst,
    ///   damit kein Stand vor ein früher eingereihtes Match rutscht.</item>
    /// <item>Fehler: bis zu 3 Wiederholungen (500/2000/5000 ms), danach verworfen, <see cref="Failures"/> zählt, geloggt wird nur der Ausnahmetyp.</item>
    /// <item><see cref="FlushAsync"/> wartet auf alle Aufträge, die VOR dem Aufruf eingereiht wurden – nicht auf später eingereihte.</item>
    /// </list>
    /// Übergebene <see cref="PlayerRecord"/>-Snapshots gehören danach der Warteschlange und dürfen nicht mehr verändert werden.
    /// </summary>
    public sealed class PersistenceQueue : IAsyncDisposable
    {
        private static readonly int[] DefaultRetryDelaysMs = { 500, 2000, 5000 };
        /// <summary>Zeitbudget von DisposeAsync für das Wegschreiben (Tests setzen es kürzer).</summary>
        internal TimeSpan DisposeFlushTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Beim Herunterfahren verworfene Aufträge (auch in <see cref="Failures"/> enthalten).</summary>
        internal int DroppedOnShutdown => Volatile.Read(ref _droppedOnShutdown);

        private sealed class Job
        {
            public string PlayerId;
            public long Seq;             // globale Einreihungsnummer (= Reihenfolge im Channel)
            public long Epoch;           // Forget-Generation des Spielers beim Einreihen
            public PlayerRecord Snapshot; // Save (unter _gate ersetzbar, solange der Job nicht begonnen hat)
            public MatchRecord Match;     // Match
        }

        /// <summary>Zustand eines Spielers mit offenen Aufträgen; wird entfernt, sobald nichts mehr offen ist.</summary>
        private sealed class PlayerState
        {
            public int Pending;
            public long Epoch;           // Forget erhöht: ältere Jobs laufen leer
            public Job QueuedSave;       // zusammenfassbarer Save (nur wenn er der letzte, noch nicht begonnene Job ist)
        }

        private readonly IPlayerRepository _repo;
        private readonly bool _inline;
        private readonly int[] _retryDelaysMs;
        private readonly object _gate = new();
        private readonly Dictionary<string, PlayerState> _players = new();
        /// <summary>
        /// Spieler, deren letzter Schreibversuch verworfen wurde (Wiederholungen ausgeschöpft, Herunterfahren-Zeitlimit
        /// oder ein interner Fehler) – überlebt das Entfernen aus <see cref="_players"/>, sobald nichts mehr offen ist.
        /// Der nächste erfolgreiche Schreibauftrag desselben Spielers löscht die Markierung (Task 6 Fix Runde 1:
        /// <see cref="AccountStore.Evict"/> darf so keinen ungespeicherten Fortschritt verlieren).
        /// </summary>
        private readonly HashSet<string> _failedPlayers = new();
        private readonly Queue<(long Target, TaskCompletionSource Done)> _flushWaiters = new();
        private readonly Channel<Job> _channel = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _abort = new();
        private readonly Task _worker;
        private long _lastSeq;           // zuletzt vergebene Einreihungsnummer
        private int _pending;
        private long _failures;
        private DateTime? _lastErrorAt;
        private bool _closed;            // nach DisposeAsync: Channel geschlossen, Aufträge werden synchron geschrieben
        private int _disposeStarted;
        private int _droppedOnShutdown;

        private PersistenceQueue(IPlayerRepository repo, bool inline, int[] retryDelaysMs)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _inline = inline;
            _retryDelaysMs = (int[])(retryDelaysMs ?? DefaultRetryDelaysMs).Clone();
            _worker = inline ? Task.CompletedTask : Task.Run(WorkAsync);
        }

        /// <summary>Schreibt sofort und synchron; Ausnahmen kommen beim Aufrufer an (Tests, Standard).</summary>
        public static PersistenceQueue Inline(IPlayerRepository repo) => new(repo, true, null);

        /// <summary>Ein Worker-Task schreibt im Hintergrund (Produktion).</summary>
        public static PersistenceQueue Background(IPlayerRepository repo) => new(repo, false, null);

        /// <summary>Wie <see cref="Background(IPlayerRepository)"/>, mit eigenen Wartezeiten zwischen den Wiederholungen (Tests).</summary>
        internal static PersistenceQueue Background(IPlayerRepository repo, int[] retryDelaysMs) => new(repo, false, retryDelaysMs);

        /// <summary>Anzahl noch nicht abgeschlossener Aufträge (zusammengefasste Saves zählen einmal).</summary>
        public int Pending { get { lock (_gate) return _pending; } }
        public long Failures => Interlocked.Read(ref _failures);
        public DateTime? LastErrorAt { get { lock (_gate) return _lastErrorAt; } }

        public bool HasPending(string playerId)
        {
            if (playerId == null) return false;
            lock (_gate) return _players.ContainsKey(playerId);
        }

        /// <summary>War der letzte Schreibversuch dieses Spielers erfolglos (Wiederholungen ausgeschöpft oder verworfen)?
        /// Ein erfolgreicher Schreibauftrag danach löscht die Markierung wieder.</summary>
        public bool HasFailed(string playerId)
        {
            if (playerId == null) return false;
            lock (_gate) return _failedPlayers.Contains(playerId);
        }

        public void EnqueueSave(PlayerRecord snapshot)
        {
            if (snapshot?.Id == null) throw new ArgumentException("Snapshot mit Spieler-Id erwartet", nameof(snapshot));
            if (_inline) { ExecuteInline(snapshot.Id, () => _repo.SaveProgress(snapshot)); return; }
            lock (_gate)
            {
                if (!_closed)
                {
                    PlayerState state = StateOf(snapshot.Id);
                    if (state.QueuedSave != null) { state.QueuedSave.Snapshot = snapshot; return; }   // neuester Stand gewinnt
                    state.QueuedSave = EnqueueLocked(state, new Job { PlayerId = snapshot.Id, Snapshot = snapshot });
                    return;
                }
            }
            ExecuteAfterClose(snapshot.Id, () => _repo.SaveProgress(snapshot));
        }

        public void EnqueueMatch(string playerId, MatchRecord match)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (_inline) { ExecuteInline(playerId, () => _repo.AddMatch(playerId, match)); return; }
            lock (_gate)
            {
                if (!_closed)
                {
                    PlayerState state = StateOf(playerId);
                    EnqueueLocked(state, new Job { PlayerId = playerId, Match = match });
                    state.QueuedSave = null;   // spätere Saves gehören hinter dieses Match
                    return;
                }
            }
            ExecuteAfterClose(playerId, () => _repo.AddMatch(playerId, match));
        }

        /// <summary>
        /// Verwirft alle bis jetzt eingereihten, noch nicht begonnenen Aufträge des Spielers (Konto gelöscht).
        /// Ein gerade laufender Schreibaufruf läuft zu Ende (das Repository macht daraus ein No-op), wird aber nicht wiederholt.
        /// Später eingereihte Aufträge desselben Spielers werden normal geschrieben.
        /// </summary>
        public void Forget(string playerId)
        {
            if (playerId == null || _inline) return;
            lock (_gate)
            {
                _failedPlayers.Remove(playerId);   // Konto gelöscht: eine frühere Fehlmarkierung wäre irreführend
                if (!_players.TryGetValue(playerId, out PlayerState state)) return;   // nichts offen, kein Zustand nötig
                state.Epoch++;
                state.QueuedSave = null;
            }
        }

        /// <summary>
        /// Wartet, bis alle vor diesem Aufruf eingereihten Aufträge abgeschlossen (geschrieben, verworfen oder vergessen) sind,
        /// höchstens <paramref name="timeout"/>. Später eingereihte Aufträge werden nicht abgewartet; ein Save, der in einen
        /// abzuwartenden Job zusammengefasst wurde, ist mit diesem Job erledigt. Wirft bei Zeitüberschreitung nicht.
        /// </summary>
        public async Task FlushAsync(TimeSpan timeout)
        {
            if (_inline) return;
            TaskCompletionSource done;
            lock (_gate)
            {
                if (_pending == 0) return;
                done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _flushWaiters.Enqueue((_lastSeq, done));
            }
            try { await done.Task.WaitAsync(timeout).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }

        /// <summary>
        /// Schreibt alles Offene weg (Budget <see cref="DisposeFlushTimeout"/>, Standard 10 s), schließt dann den Channel und wartet
        /// auf den Worker. Während des Budgets werden weiter Aufträge angenommen und mitgeschrieben; geschlossen wird atomar in dem
        /// Moment, in dem nichts mehr offen ist. Nur wenn das Budget abläuft, wird der Rest verworfen (gezählt in <see cref="Failures"/>).
        /// Mehrfacher Aufruf ist harmlos.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1) { await _worker.ConfigureAwait(false); return; }
            if (_inline) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();   // monoton, unabhängig von Uhrzeit-Sprüngen
            bool abort;
            while (true)
            {
                TimeSpan remaining = DisposeFlushTimeout - clock.Elapsed;
                if (remaining > TimeSpan.Zero) await FlushAsync(remaining).ConfigureAwait(false);
                lock (_gate)
                {
                    // Schließen nur, wenn wirklich nichts offen ist oder das Budget verbraucht ist – sonst weiter flushen.
                    if (_pending == 0 || clock.Elapsed >= DisposeFlushTimeout)
                    {
                        _closed = true;
                        _channel.Writer.TryComplete();
                        abort = _pending > 0;
                        break;
                    }
                }
            }
            if (abort) _abort.Cancel();   // außerhalb von _gate: Abbruch-Callbacks laufen synchron
            await _worker.ConfigureAwait(false);
            _abort.Dispose();
            int dropped = Volatile.Read(ref _droppedOnShutdown);
            if (dropped > 0) Console.Error.WriteLine($"[Persistenz] Beim Herunterfahren {dropped} Schreibaufträge verworfen (Zeitlimit)");
        }

        // ---- intern ----

        private PlayerState StateOf(string playerId)   // unter _gate
        {
            if (!_players.TryGetValue(playerId, out PlayerState state)) _players[playerId] = state = new PlayerState();
            return state;
        }

        private Job EnqueueLocked(PlayerState state, Job job)   // unter _gate: Channel-Reihenfolge == Seq-Reihenfolge
        {
            job.Seq = ++_lastSeq;
            job.Epoch = state.Epoch;
            state.Pending++;
            _pending++;
            _channel.Writer.TryWrite(job);   // unbeschränkt und nur unter _gate geschlossen: gelingt immer
            return job;
        }

        private async Task WorkAsync()
        {
            await foreach (Job job in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    Action write = null;
                    bool dropped = false;
                    lock (_gate)
                    {
                        PlayerState state = _players[job.PlayerId];
                        if (state.QueuedSave == job) state.QueuedSave = null;   // ab jetzt nicht mehr zusammenfassbar
                        if (_abort.IsCancellationRequested) dropped = true;
                        else if (job.Epoch == state.Epoch)
                        {
                            PlayerRecord snap = job.Snapshot;
                            MatchRecord match = job.Match;
                            write = snap != null ? () => _repo.SaveProgress(snap) : () => _repo.AddMatch(job.PlayerId, match);
                        }
                    }
                    if (dropped) { CountShutdownDrop(); MarkFailed(job.PlayerId); }
                    else if (write != null) await ExecuteWithRetryAsync(job, write).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RecordFailure("[Persistenz] Interner Fehler im Worker: ", ex);
                    MarkFailed(job.PlayerId);
                }
                finally
                {
                    lock (_gate) Complete(job);
                }
            }
        }

        private void Complete(Job job)   // unter _gate
        {
            PlayerState state = _players[job.PlayerId];
            if (--state.Pending == 0) _players.Remove(job.PlayerId);   // kein Zustand bleibt zurück
            _pending--;
            // Der Worker schließt Jobs in Seq-Reihenfolge ab: alle Wartenden bis job.Seq sind fertig.
            while (_flushWaiters.Count > 0 && _flushWaiters.Peek().Target <= job.Seq) _flushWaiters.Dequeue().Done.TrySetResult();
        }

        private bool StillWanted(Job job)   // nur Forget; Abbruch prüft der Aufrufer getrennt, weil er als Verlust zählt
        {
            lock (_gate) return _players.TryGetValue(job.PlayerId, out PlayerState s) && s.Epoch == job.Epoch;
        }

        private async Task ExecuteWithRetryAsync(Job job, Action write)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { write(); ClearFailed(job.PlayerId); return; }
                catch (Exception ex)
                {
                    if (attempt >= _retryDelaysMs.Length)
                    {
                        RecordFailure("[Persistenz] Schreibauftrag nach Wiederholungen verworfen: ", ex);
                        // Nur markieren, wenn der Job noch gewollt ist: ein gleichzeitiges Forget/Delete (Epoche erhöht,
                        // während dieser Versuch noch lief) soll keinen verwaisten Fehlmarkierungs-Eintrag hinterlassen
                        // (Controller-Review Task 6, Fix Runde 2).
                        if (StillWanted(job)) MarkFailed(job.PlayerId);
                        return;
                    }
                }
                try { await Task.Delay(_retryDelaysMs[attempt], _abort.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { CountShutdownDrop(); MarkFailed(job.PlayerId); return; }
                if (_abort.IsCancellationRequested) { CountShutdownDrop(); MarkFailed(job.PlayerId); return; }   // Herunterfahren: Verlust, nie stumm
                if (!StillWanted(job)) return;   // inzwischen vergessen (Konto gelöscht): gewollt, kein Fehler
            }
        }

        private void ExecuteInline(string playerId, Action write)
        {
            try { write(); ClearFailed(playerId); }
            catch (Exception ex)
            {
                RecordFailure("[Persistenz] Schreibauftrag fehlgeschlagen: ", ex);
                MarkFailed(playerId);
                throw;
            }
        }

        /// <summary>
        /// Nach DisposeAsync: synchron schreiben, Fehler zählen, aber nicht werfen (Aufrufer erwarten Hintergrund-Semantik).
        /// Wartet vorher auf den Worker, damit kein Schreibaufruf neben einem noch laufenden Worker-Aufruf liegt (Reihenfolge pro Spieler).
        /// </summary>
        private void ExecuteAfterClose(string playerId, Action write)
        {
            if (!_worker.IsCompleted)
            {
                try { _worker.Wait(); } catch (AggregateException) { }   // Worker fängt selbst alles; nur zur Sicherheit
            }
            try { write(); ClearFailed(playerId); }
            catch (Exception ex)
            {
                RecordFailure("[Persistenz] Schreibauftrag nach dem Herunterfahren fehlgeschlagen: ", ex);
                MarkFailed(playerId);
            }
        }

        /// <summary>Beim Herunterfahren nach Zeitlimit verworfen: zählt als Fehler, geloggt wird einmal gesammelt in DisposeAsync.</summary>
        private void CountShutdownDrop()
        {
            Interlocked.Increment(ref _droppedOnShutdown);
            Interlocked.Increment(ref _failures);
            lock (_gate) _lastErrorAt = DateTime.UtcNow;
        }

        private void RecordFailure(string prefix, Exception ex)
        {
            Interlocked.Increment(ref _failures);
            lock (_gate) _lastErrorAt = DateTime.UtcNow;
            Console.Error.WriteLine(prefix + ex.GetType().Name);   // nur der Typ: Meldungen können Verbindungsdaten enthalten
        }

        private void MarkFailed(string playerId)
        {
            if (playerId != null) lock (_gate) _failedPlayers.Add(playerId);
        }

        private void ClearFailed(string playerId)
        {
            if (playerId != null) lock (_gate) _failedPlayers.Remove(playerId);
        }
    }
}
