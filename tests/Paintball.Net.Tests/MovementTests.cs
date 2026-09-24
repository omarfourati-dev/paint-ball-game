using System.Numerics;
using Paintball.Core.Maps;
using Paintball.Net.Simulation;

namespace Paintball.Net.Tests
{
    /// <summary>Bewegung & Kollision (FR-02, FR-11): identisch auf Server und Client.</summary>
    internal static class MovementTests
    {
        private const float Dt = 1f / 30f;

        public static void Register(TestRunner r)
        {
            r.Run("Bewegung: Laufen, Sprinten, Ducken mit festen Geschwindigkeiten (FR-02)", Speeds);
            r.Run("Bewegung: Blickrichtung (Yaw) bestimmt Laufrichtung", YawDirection);
            r.Run("Bewegung: Eingabevektor wird normalisiert (Anti-Speedhack, NFR-10)", InputClamped);
            r.Run("Bewegung: Springen nur am Boden, Schwerkraft landet wieder (FR-02)", JumpAndLand);
            r.Run("Kollision: Spieler läuft nicht durch Deckung (FR-54)", CollidesWithCover);
            r.Run("Kollision: Kartenrand begrenzt Bewegung", ClampsToBounds);
            r.Run("Kollision: Auf Deckung landen möglich", LandsOnTopOfBox);
            r.Run("Kollision: Aufstehen unter Hindernis blockiert", CannotStandUnderCeiling);
            r.Run("Welt: Karte aus MapCatalog erzeugt Hindernisse und dynamische Deckung (FR-56)", WorldFromCatalog);
            r.Run("Welt: Raycast trifft Box und Boden mit Normalen", RaycastHitsBoxAndGround);
        }

        private static World EmptyWorld() => new World(50f, 50f);

        private static MoveState Idle() => new MoveState { Position = Vector3.Zero, OnGround = true };

        private static MoveState Walk(World world, MoveState s, MoveInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++) s = Movement.Step(s, input, Dt, world);
            return s;
        }

        private static void Speeds()
        {
            World w = EmptyWorld();
            var walk = Walk(w, Idle(), new MoveInput { MoveZ = 1f }, 30);
            Assert.AreClose(Movement.WalkSpeed, walk.Position.Z, 0.01f, "1s Laufen = WalkSpeed Meter");

            var sprint = Walk(w, Idle(), new MoveInput { MoveZ = 1f, Sprint = true }, 30);
            Assert.AreClose(Movement.SprintSpeed, sprint.Position.Z, 0.01f, "1s Sprinten = SprintSpeed Meter");

            var crouch = Walk(w, Idle(), new MoveInput { MoveZ = 1f, Crouch = true, Sprint = true }, 30);
            Assert.AreClose(Movement.CrouchSpeed, crouch.Position.Z, 0.01f, "Ducken ignoriert Sprint");
            Assert.IsTrue(crouch.Crouched, "Ducken-Zustand gesetzt");
            Assert.IsTrue(Movement.HeightOf(crouch) < Movement.HeightOf(walk), "Geduckt ist kleiner");

            var boosted = Idle();
            for (int i = 0; i < 30; i++) boosted = Movement.Step(boosted, new MoveInput { MoveZ = 1f }, Dt, w, 1.4f);
            Assert.AreClose(Movement.WalkSpeed * 1.4f, boosted.Position.Z, 0.02f, "Speed-Power-Up multipliziert (FR-09)");
        }

        private static void YawDirection()
        {
            World w = EmptyWorld();
            float yaw = System.MathF.PI / 2f; // Blick nach +X
            var s = Walk(w, Idle(), new MoveInput { MoveZ = 1f, Yaw = yaw }, 30);
            Assert.AreClose(Movement.WalkSpeed, s.Position.X, 0.01f, "Vorwärts bei Yaw 90° = +X");
            Assert.AreClose(0f, s.Position.Z, 0.01f, "Kein Z-Anteil");

            var strafe = Walk(w, Idle(), new MoveInput { MoveX = 1f, Yaw = 0f }, 30);
            Assert.AreClose(-Movement.WalkSpeed, strafe.Position.X, 0.01f, "Rechts bei Yaw 0 = -X (rechtshändig)");
        }

        private static void InputClamped()
        {
            World w = EmptyWorld();
            var s = Walk(w, Idle(), new MoveInput { MoveX = 5f, MoveZ = 5f }, 30);
            float dist = new Vector2(s.Position.X, s.Position.Z).Length();
            Assert.AreClose(Movement.WalkSpeed, dist, 0.02f, "Diagonal/übergroße Eingabe nicht schneller");

            var nan = Walk(w, Idle(), new MoveInput { MoveX = float.NaN, MoveZ = float.PositiveInfinity, Yaw = float.NaN }, 5);
            Assert.IsTrue(float.IsFinite(nan.Position.X) && float.IsFinite(nan.Position.Z), "NaN-Eingaben werden verworfen");
        }

        private static void JumpAndLand()
        {
            World w = EmptyWorld();
            var s = Movement.Step(Idle(), new MoveInput { Jump = true }, Dt, w);
            Assert.IsFalse(s.OnGround, "Nach Sprung in der Luft");
            float peak = 0f;
            bool doubleJumped = false;
            for (int i = 0; i < 60; i++)
            {
                float before = s.VelocityY;
                s = Movement.Step(s, new MoveInput { Jump = true }, Dt, w);
                if (!s.OnGround && s.VelocityY > before + 0.01f) doubleJumped = true;
                peak = System.MathF.Max(peak, s.Position.Y);
                if (s.OnGround) break;
            }
            Assert.IsFalse(doubleJumped, "Kein Doppelsprung in der Luft");
            Assert.IsTrue(peak > 1.2f && peak < 2.0f, $"Sprunghöhe plausibel ({peak})");
            Assert.IsTrue(s.OnGround, "Wieder gelandet");
            Assert.AreClose(0f, s.Position.Y, 0.001f, "Auf Bodenhöhe");
        }

        private static void CollidesWithCover()
        {
            World w = EmptyWorld();
            w.AddBox(new Aabb(new Vector3(-2f, 0f, 3f), new Vector3(2f, 1.6f, 4f)));
            var s = Walk(w, Idle(), new MoveInput { MoveZ = 1f }, 60);
            Assert.IsTrue(s.Position.Z <= 3f - Movement.Radius + 0.01f, $"Stoppt vor Deckung (z={s.Position.Z})");
            Assert.IsTrue(s.Position.Z > 2f, "Läuft bis an die Deckung heran");
        }

        private static void ClampsToBounds()
        {
            var w = new World(10f, 10f);
            var s = Walk(w, Idle(), new MoveInput { MoveX = 1f }, 120); // rechts = -X bei Yaw 0
            Assert.IsTrue(s.Position.X >= -10f + Movement.Radius - 0.001f, "Kartenrand hält Spieler");
            Assert.IsTrue(s.Position.X < -9f, "Läuft bis zum Rand");
        }

        private static void LandsOnTopOfBox()
        {
            World w = EmptyWorld();
            w.AddBox(new Aabb(new Vector3(-1f, 0f, -1f), new Vector3(1f, 1f, 1f)));
            var s = new MoveState { Position = new Vector3(0f, 3f, 0f), OnGround = false };
            for (int i = 0; i < 60; i++) s = Movement.Step(s, new MoveInput(), Dt, w);
            Assert.IsTrue(s.OnGround, "Steht auf der Box");
            Assert.AreClose(1f, s.Position.Y, 0.001f, "Auf Boxoberkante");
        }

        private static void CannotStandUnderCeiling()
        {
            World w = EmptyWorld();
            w.AddBox(new Aabb(new Vector3(-1f, 1.3f, -1f), new Vector3(1f, 2f, 1f)));
            var s = new MoveState { Position = Vector3.Zero, OnGround = true, Crouched = true };
            s = Movement.Step(s, new MoveInput { Crouch = false }, Dt, w);
            Assert.IsTrue(s.Crouched, "Bleibt geduckt unter Decke");
        }

        private static void WorldFromCatalog()
        {
            var catalog = new MapCatalog();
            MapDefinition warehouse = catalog.GetById("warehouse");
            World w0 = World.FromMap(warehouse, 0f);
            Assert.AreEqual(warehouse.Covers.Count, w0.Boxes.Count, "Jede Deckung wird zur Box");
            Assert.AreClose(warehouse.SizeX / 2f, w0.HalfX, 0.001f, "Kartengröße übernommen");

            World w2 = World.FromMap(warehouse, 2f);
            int dyn = warehouse.Covers.FindIndex(c => c.IsDynamic);
            Assert.IsTrue(dyn >= 0, "Lagerhaus hat dynamische Deckung");
            Assert.IsTrue(Vector3.Distance(w0.Boxes[dyn].Center, w2.Boxes[dyn].Center) > 0.5f, "Dynamische Deckung bewegt sich über Zeit");
            Assert.AreEqual(World.FromMap(warehouse, 2f).Boxes[dyn].Center, w2.Boxes[dyn].Center, "Deterministisch (Client kann gleiche Position rechnen)");
        }

        private static void RaycastHitsBoxAndGround()
        {
            World w = EmptyWorld();
            w.AddBox(new Aabb(new Vector3(-1f, 0f, 5f), new Vector3(1f, 2f, 6f)));
            Assert.IsTrue(w.Raycast(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), 50f, out RayHit hit), "Trifft Box");
            Assert.AreClose(5f, hit.Distance, 0.001f, "Distanz bis Box");
            Assert.AreEqual(new Vector3(0f, 0f, -1f), hit.Normal, "Normale zeigt zum Schützen");

            Assert.IsTrue(w.Raycast(new Vector3(0f, 1f, 0f), Vector3.Normalize(new Vector3(0f, -1f, -1f)), 50f, out RayHit g), "Trifft Boden");
            Assert.AreEqual(Vector3.UnitY, g.Normal, "Bodennormale nach oben");
            Assert.IsFalse(w.Raycast(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), 50f, out _), "Nach oben: kein Treffer");
        }
    }
}
