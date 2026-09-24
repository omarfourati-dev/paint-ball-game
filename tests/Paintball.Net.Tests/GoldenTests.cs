using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Paintball.Core.Maps;
using Paintball.Net.Simulation;

namespace Paintball.Net.Tests
{
    /// <summary>
    /// Sprachübergreifende Golden-Datei (FR-26): Der Browser-Client (web/js/movement.js)
    /// muss dieselben Bewegungen berechnen wie der Server. Die Datei wird vom C#-Server
    /// erzeugt (PB_WRITE_GOLDEN=1) und von tests/web/movement.test.mjs geprüft.
    /// </summary>
    internal static class GoldenTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Golden: Bewegungs-Referenzdatei für Client-Prediction ist aktuell (FR-26)", GoldenUpToDate);
        }

        internal static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "anforderung.md"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden");
        }

        private sealed class Scenario
        {
            public string Name;
            public Vector3 Start;
            public float StartTime;
            public Func<int, MoveInput> Input;
            public Func<int, float> Speed = _ => 1f;
            public int Steps = 90;
        }

        private static List<Scenario> Scenarios() => new()
        {
            new Scenario { Name = "walk-forward", Start = new Vector3(-30f, 0f, -30f), Input = i => new MoveInput { MoveZ = 1f, Yaw = 0.6f } },
            new Scenario { Name = "sprint-turning", Start = new Vector3(-30f, 0f, -30f), Input = i => new MoveInput { MoveZ = 1f, Sprint = true, Yaw = 0.02f * i } },
            new Scenario { Name = "strafe-into-container", Start = new Vector3(-13f, 0f, -5f), Input = i => new MoveInput { MoveX = -1f, Yaw = 0f } },
            new Scenario { Name = "jump-onto-crate", Start = new Vector3(-12f, 0f, -14f), Input = i => new MoveInput { MoveZ = 1f, Jump = i % 20 == 0, Yaw = 0f } },
            new Scenario { Name = "crouch-walk-diagonal", Start = new Vector3(5f, 0f, 5f), Input = i => new MoveInput { MoveX = 0.7f, MoveZ = 0.7f, Crouch = i < 60, Yaw = -1.2f } },
            new Scenario { Name = "wall-slide", Start = new Vector3(37f, 0f, 0f), Input = i => new MoveInput { MoveZ = 1f, MoveX = -0.3f, Yaw = 1.2f } },
            new Scenario { Name = "walk-off-crate", Start = new Vector3(-12f, 1f, -12.2f), Input = i => new MoveInput { MoveZ = i < 40 ? 0f : 1f, Yaw = 0f } },
            new Scenario { Name = "dynamic-cover", Start = new Vector3(2f, 0f, -25f), StartTime = 1f, Input = i => new MoveInput { MoveZ = 1f, Yaw = 0f } },
            new Scenario { Name = "speed-boost", Start = new Vector3(-20f, 0f, 20f), Input = i => new MoveInput { MoveZ = 1f, Yaw = 2.5f }, Speed = i => i < 30 ? 1.4f : 1f },
            new Scenario { Name = "dash", Start = new Vector3(20f, 0f, -20f), Input = i => new MoveInput { MoveZ = 1f, Yaw = -2.2f }, Speed = i => i < 8 ? GameMatch.DashMultiplier : 1f }
        };

        public static string Build()
        {
            MapDefinition map = new MapCatalog().GetById("warehouse");
            var sb = new StringBuilder();
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("map", map.Id);
                w.WriteNumber("dt", GameMatch.TickDt);
                w.WriteNumber("halfX", map.SizeX / 2f);
                w.WriteNumber("halfZ", map.SizeZ / 2f);
                w.WriteStartArray("covers");
                foreach (MapCoverBlock c in map.Covers)
                {
                    w.WriteStartArray();
                    foreach (float v in new[] { c.X, c.Y, c.Z, c.ScaleX, c.ScaleY, c.ScaleZ }) w.WriteNumberValue(v);
                    w.WriteNumberValue(c.IsDynamic ? 1 : 0);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteStartArray("scenarios");
                foreach (Scenario sc in Scenarios())
                {
                    w.WriteStartObject();
                    w.WriteString("name", sc.Name);
                    w.WriteNumber("startTime", sc.StartTime);
                    w.WriteStartArray("start"); w.WriteNumberValue(sc.Start.X); w.WriteNumberValue(sc.Start.Y); w.WriteNumberValue(sc.Start.Z); w.WriteEndArray();
                    w.WriteStartArray("steps");
                    var state = new MoveState { Position = sc.Start, OnGround = true };
                    World world = World.FromMap(map, sc.StartTime);
                    for (int i = 0; i < sc.Steps; i++)
                    {
                        float time = sc.StartTime + (i + 1) * GameMatch.TickDt;
                        world.UpdateDynamic(map, time);
                        MoveInput input = sc.Input(i);
                        float speed = sc.Speed(i);
                        state = Movement.Step(state, input, GameMatch.TickDt, world, speed);
                        w.WriteStartObject();
                        w.WriteNumber("mx", input.MoveX); w.WriteNumber("mz", input.MoveZ); w.WriteNumber("yaw", input.Yaw);
                        w.WriteBoolean("sprint", input.Sprint); w.WriteBoolean("crouch", input.Crouch); w.WriteBoolean("jump", input.Jump);
                        w.WriteNumber("speed", speed);
                        w.WriteNumber("time", time);
                        w.WriteStartArray("pos");
                        w.WriteNumberValue(Math.Round(state.Position.X, 4)); w.WriteNumberValue(Math.Round(state.Position.Y, 4)); w.WriteNumberValue(Math.Round(state.Position.Z, 4));
                        w.WriteEndArray();
                        w.WriteNumber("vy", Math.Round(state.VelocityY, 4));
                        w.WriteBoolean("ground", state.OnGround);
                        w.WriteBoolean("crouched", state.Crouched);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n";
        }

        private static void GoldenUpToDate()
        {
            string path = Path.Combine(RepoRoot(), "tests", "web", "fixtures", "movement-golden.json");
            string expected = Build();
            if (Environment.GetEnvironmentVariable("PB_WRITE_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, expected);
            }
            Assert.IsTrue(File.Exists(path), "Golden-Datei fehlt – mit PB_WRITE_GOLDEN=1 erzeugen");
            Assert.AreEqual(expected, File.ReadAllText(path).Replace("\r\n", "\n"), "Golden-Datei veraltet – Server-Bewegung geändert? PB_WRITE_GOLDEN=1 ausführen");
        }
    }
}
