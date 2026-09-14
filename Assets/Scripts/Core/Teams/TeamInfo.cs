namespace Paintball.Core.Teams
{
    /// <summary>
    /// Team-Definition inklusive Paintball-Farbe (FR-36).
    /// Farben als Float-RGB, damit die Kernlogik engine-unabhängig bleibt.
    /// </summary>
    public sealed class TeamInfo
    {
        public int TeamId { get; }
        public string Name { get; }
        public float ColorR { get; }
        public float ColorG { get; }
        public float ColorB { get; }

        public TeamInfo(int teamId, string name, float r, float g, float b)
        {
            TeamId = teamId;
            Name = name;
            ColorR = r;
            ColorG = g;
            ColorB = b;
        }

        public static readonly TeamInfo Red = new TeamInfo(0, "Rot", 0.95f, 0.25f, 0.25f);
        public static readonly TeamInfo Blue = new TeamInfo(1, "Blau", 0.25f, 0.45f, 0.95f);

        public static TeamInfo Default(int teamId) => teamId == 0 ? Red : Blue;
    }
}
