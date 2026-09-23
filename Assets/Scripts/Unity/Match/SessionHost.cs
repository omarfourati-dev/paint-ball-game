using Paintball.Core.Session;
using UnityEngine;

namespace Paintball.Unity.Match
{
    /// <summary>
    /// Multi-Szenen-Host für die Core-<see cref="SessionManager"/>-Instanz (FR-22):
    /// eine Session lebt über Szenenwechsel hinweg (Lobby → Match → Result).
    /// Netcode-frei; wenn echte Verbindungen kommen (FR-22), wird nur der
    /// Netzwerk-Layer angedockt.
    /// </summary>
    public sealed class SessionHost : MonoBehaviour
    {
        public static SessionHost Instance { get; private set; }

        private static readonly SessionManager CoreSession = new();

        /// <summary>Zugriff auf den Core-Session-Zustand (für Telewriting/Debug-HUD).</summary>
        public static SessionManager Session => CoreSession;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Startet die Lobby-Session, sobald ein Match durch die Lobby beginnt.</summary>
        public void BeginMatch(string hostId, string gameMode, int localTeamId, float now)
        {
            if (CoreSession.State == SessionState.Idle)
                CoreSession.StartSession(hostId, gameMode, now);

            CoreSession.AssignTeam(hostId, localTeamId);
            CoreSession.BeginMatch(now);
        }

        /// <summary>Beendet die aktive Session nach dem Match (Teil des Abschluss-Flows).</summary>
        public void EndMatch(float now)
        {
            if (CoreSession.State == SessionState.InGame || CoreSession.State == SessionState.Results)
                CoreSession.EndGame(now);
            CoreSession.Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}