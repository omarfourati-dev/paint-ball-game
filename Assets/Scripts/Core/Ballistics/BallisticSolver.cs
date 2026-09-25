using System;
using System.Numerics;

namespace Paintball.Core.Ballistics
{
    /// <summary>
    /// Ballistische Berechnungen für Paintball-Projektile (FR-03):
    /// Wurfparabel mit Gravitation, Streuung im Kegel und Drop-Abschätzung.
    /// Engine-unabhängig (System.Numerics), damit Client und Server identisch rechnen (AR-06).
    /// </summary>
    public static class BallisticSolver
    {
        public static readonly Vector3 DefaultGravity = new Vector3(0f, -9.81f, 0f);

        /// <summary>Position eines Projektils zum Zeitpunkt t (Wurfparabel).</summary>
        public static Vector3 PositionAt(Vector3 origin, Vector3 initialVelocity, Vector3 gravity, float time)
            => origin + initialVelocity * time + 0.5f * gravity * time * time;

        /// <summary>Geschwindigkeit eines Projektils zum Zeitpunkt t.</summary>
        public static Vector3 VelocityAt(Vector3 initialVelocity, Vector3 gravity, float time)
            => initialVelocity + gravity * time;

        /// <summary>
        /// Streut eine Richtung gleichmäßig innerhalb eines Kegels (FR-03: Streuung).
        /// </summary>
        /// <param name="forward">Normalisierte Zielrichtung.</param>
        /// <param name="spreadDegrees">Maximaler Streuwinkel in Grad.</param>
        public static Vector3 ApplySpread(Vector3 forward, float spreadDegrees, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            Vector3 f = Vector3.Normalize(forward);
            if (spreadDegrees <= 0f) return f;

            float maxRad = spreadDegrees * MathF.PI / 180f;
            float cosAngle = 1f - (float)random.NextDouble() * (1f - MathF.Cos(maxRad));
            float sinAngle = MathF.Sqrt(MathF.Max(0f, 1f - cosAngle * cosAngle));
            float phi = (float)(random.NextDouble() * 2.0 * Math.PI);

            // Orthonormalbasis um die Blickrichtung aufspannen
            Vector3 helper = MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(helper, f));
            Vector3 up = Vector3.Cross(f, right);

            Vector3 dir = f * cosAngle + (right * MathF.Cos(phi) + up * MathF.Sin(phi)) * sinAngle;
            return Vector3.Normalize(dir);
        }

        /// <summary>Anteil der Streuung, auf dem der Pellet-Ring liegt (Schrot).</summary>
        public const float PelletRingFraction = 0.45f;

        /// <summary>Zufälliges Zittern je Pellet als Anteil der Streuung.</summary>
        public const float PelletJitterFraction = 0.03f;

        /// <summary>
        /// Schrot-Muster: Pellet 0 mittig, die übrigen gleichmäßig auf einem Ring bei 0,45 × Streuung, der Ring zufällig
        /// gedreht, jedes Pellet mit ±3 % Zittern. Alle Pellets liegen im Kegel mit <paramref name="spreadDegrees"/> Öffnungswinkel.
        /// Ein Pellet: wie <see cref="ApplySpread"/> (gleicher Zufallsverbrauch wie bisher).
        /// </summary>
        public static Vector3[] PelletPattern(Vector3 forward, int pellets, float spreadDegrees, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            Vector3 f = Vector3.Normalize(forward);
            int n = Math.Max(1, pellets);
            var dirs = new Vector3[n];
            if (n == 1)
            {
                dirs[0] = ApplySpread(f, spreadDegrees, random);
                return dirs;
            }

            Vector3 helper = MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(helper, f));
            Vector3 up = Vector3.Cross(f, right);
            float ring = spreadDegrees * PelletRingFraction * MathF.PI / 180f;
            float jitter = spreadDegrees * PelletJitterFraction;
            float phase = (float)(random.NextDouble() * 2.0 * Math.PI);

            dirs[0] = ApplySpread(f, jitter, random);
            for (int i = 1; i < n; i++)
            {
                float phi = phase + (i - 1) * 2f * MathF.PI / (n - 1);
                Vector3 d = f * MathF.Cos(ring) + (right * MathF.Cos(phi) + up * MathF.Sin(phi)) * MathF.Sin(ring);
                dirs[i] = ApplySpread(Vector3.Normalize(d), jitter, random);
            }
            return dirs;
        }

        /// <summary>Geschätzte Flugzeit über eine Distanz (Näherung ohne Drop).</summary>
        public static float EstimateFlightTime(float distance, float muzzleVelocity)
            => muzzleVelocity <= 0f ? float.PositiveInfinity : distance / muzzleVelocity;

        /// <summary>Vertikaler Abfall (Drop) nach einer Distanz – taktisches Zielen (FR-03).</summary>
        public static float DropAtDistance(float distance, float muzzleVelocity, float gravityMagnitude = 9.81f)
        {
            float t = EstimateFlightTime(distance, muzzleVelocity);
            if (float.IsPositiveInfinity(t)) return float.PositiveInfinity;
            return 0.5f * gravityMagnitude * t * t;
        }
    }
}
