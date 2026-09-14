namespace Paintball.Core.Combat
{
    /// <summary>Beziehung zwischen Schütze und Ziel – Team- und Selbsttreffer bleiben unterscheidbar (FR-10).</summary>
    public enum TeamRelation
    {
        Self,
        Ally,
        Enemy
    }
}
