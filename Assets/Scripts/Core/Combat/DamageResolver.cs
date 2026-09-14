namespace Paintball.Core.Combat
{
    /// <summary>
    /// Löst Treffer autoritativ auf: Trefferzonen-Multiplikatoren (FR-05),
    /// Team-Regeln ohne Friendly Fire (FR-10, NFR-15) und Eliminierungen.
    /// </summary>
    public sealed class DamageResolver
    {
        private readonly bool _friendlyFire;

        public DamageResolver(bool friendlyFire = false)
        {
            _friendlyFire = friendlyFire;
        }

        public static TeamRelation GetRelation(int shooterPlayerId, int shooterTeamId, int targetPlayerId, int targetTeamId)
        {
            if (shooterPlayerId == targetPlayerId) return TeamRelation.Self;
            return shooterTeamId == targetTeamId ? TeamRelation.Ally : TeamRelation.Enemy;
        }

        public HitResult Resolve(
            TeamRelation relation,
            HitZone zone,
            HitPointPool target,
            float baseDamage,
            float headMultiplier = 2f,
            float limbMultiplier = 0.75f,
            float damageMultiplier = 1f)
        {
            if (relation == TeamRelation.Self)
                return new HitResult(HitOutcome.SelfHitIgnored, 0f, target.CurrentHitPoints, zone);

            if (relation == TeamRelation.Ally && !_friendlyFire)
                return new HitResult(HitOutcome.AllyHitBlocked, 0f, target.CurrentHitPoints, zone);

            if (target.IsEliminated)
                return new HitResult(HitOutcome.AlreadyEliminated, 0f, 0f, zone);

            float zoneMultiplier = zone switch
            {
                HitZone.Head => headMultiplier,
                HitZone.Limbs => limbMultiplier,
                _ => 1f
            };

            float damage = baseDamage * zoneMultiplier * damageMultiplier;
            bool eliminated = target.ApplyDamage(damage);

            return new HitResult(
                eliminated ? HitOutcome.EnemyEliminated : HitOutcome.EnemyHit,
                damage,
                target.CurrentHitPoints,
                zone);
        }
    }
}
