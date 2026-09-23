using System.Globalization;
using System.Text;

namespace Paintball.Core.Economy
{
    /// <summary>Währungen des Spiels (M-02, M-03).</summary>
    public enum CurrencyType
    {
        Soft,
        Premium
    }

    /// <summary>
    /// Brieftasche eines Spielers (M-01 bis M-03): verwaltet Soft- und Premium-Währung
    /// mit autoritativen Buchungen (Earn/Spend). Keine Unity-Abhängigkeit –
    /// serverseitig validiert (NFR-11, FR-45/46).
    /// </summary>
    public sealed class PlayerWallet
    {
        private int _soft;
        private int _premium;

        public int SoftBalance => _soft;
        public int PremiumBalance => _premium;

        /// <summary>Einzahlung auf das Konto. Gibt den neuen Saldo zurück.</summary>
        public int Earn(CurrencyType type, int amount)
        {
            if (amount < 0)
                throw new System.ArgumentOutOfRangeException(nameof(amount), "Keine negativen Zuflüsse.");

            if (type == CurrencyType.Premium) _premium += amount;
            else _soft += amount;

            return Balance(type);
        }

        public int Balance(CurrencyType type)
        {
            return type == CurrencyType.Premium ? _premium : _soft;
        }

        /// <summary>
        /// Belastet eine Währung nur, wenn genügend Guthaben vorhanden ist.
        /// Gibt true zurück, wenn die Buchung ausgeführt wurde.
        /// </summary>
        public bool TrySpend(CurrencyType type, int amount)
        {
            if (amount < 0)
                throw new System.ArgumentOutOfRangeException(nameof(amount), "Keine negativen Abflüsse.");

            if (type == CurrencyType.Premium)
            {
                if (_premium < amount) return false;
                _premium -= amount;
                return true;
            }

            if (_soft < amount) return false;
            _soft -= amount;
            return true;
        }

        /// <summary>Transfers zwischen Spielern (z. B. Geschenke). Kein Pay-to-Win (M-04).</summary>
        public void Transfer(CurrencyType type, PlayerWallet target, int amount)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target));
            if (TrySpend(type, amount))
                target.Earn(type, amount);
        }

        /// <summary>Portabler Saldo-Export für die Datei-Persistenz (keine PlayerPrefs).</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("soft=").Append(_soft.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("premium=").Append(_premium.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Stellt die Brieftasche aus einem Serialize-String wieder her. Tolerant bei Korruption.</summary>
        public static PlayerWallet Deserialize(string data)
        {
            var wallet = new PlayerWallet();
            if (string.IsNullOrEmpty(data)) return wallet;

            foreach (string raw in data.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) && amount > 0)
                {
                    if (key == "premium") wallet._premium = amount;
                    else if (key == "soft") wallet._soft = amount;
                }
            }

            return wallet;
        }
    }
}