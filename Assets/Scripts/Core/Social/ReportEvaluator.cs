using System.Collections.Generic;

namespace Paintball.Core.Social
{
    /// <summary>Meldungsgründe mit echten Kategorien (Moderation).</summary>
    public enum ReportReason
    {
        Toxicity,
        Cheating,
        Afk,
        Bug
    }

    /// <summary>Ergebnis einer Meldung.</summary>
    public readonly struct ReportDecision
    {
        public bool Accepted { get; }
        public ReportReason Reason { get; }

        public ReportDecision(bool accepted, ReportReason reason)
        {
            Accepted = accepted;
            Reason = reason;
        }
    }

    /// <summary>
    /// Meldewesen (FR-52.4): nimmt Meldungen mit begründetem Grund an, protokolliert
    /// sie nach gemeldetem Spieler und blockt Wiederholungs-Spam derselben Reporter auf
    /// denselben Spieler (max. 2 Meldungen pro Paar). Echt und testbar, ohne Unity.
    /// </summary>
    public sealed class ReportEvaluator
    {
        private const int MaxReportsPerPair = 2;

        private readonly Dictionary<string, int> _byReporter = new();

        public ReportDecision Submit(int reporterId, int reportedId, ReportReason reason, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail) || detail.Length < 5)
                return new ReportDecision(false, reason);

            string pair = reporterId + ">" + reportedId;
            if (_byReporter.TryGetValue(pair, out int count) && count >= MaxReportsPerPair)
                return new ReportDecision(false, reason);

            _byReporter[pair] = count + 1;
            return new ReportDecision(true, reason);
        }

        public int CountFor(int reportedId)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> entry in _byReporter)
            {
                if (entry.Key.EndsWith(">" + reportedId))
                    total += entry.Value;
            }
            return total;
        }
    }
}