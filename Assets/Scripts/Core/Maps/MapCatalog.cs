using System;
using System.Collections.Generic;

namespace Paintball.Core.Maps
{
    /// <summary>Symmetrie-Art eines Karten-Designs (FR-55).</summary>
    public enum MapSymmetry
    {
        Symmetric,
        Asymmetric
    }

    /// <summary>Deckungs-/Hindernisblock einer Karte (FR-54).</summary>
    public sealed class MapCoverBlock
    {
        public float X;
        public float Y;
        public float Z;
        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;
        public bool IsDynamic;      // FR-56: bewegliche Deckung
        public bool IsResupply;     // FR-54: Nachschubpunkt an diesem Block
        /// <summary>Materialart für die Darstellung (z. B. crate, container, brick, vat) – rein visuell, AR-03.</summary>
        public string Kind = string.Empty;
    }

    /// <summary>Spawn-Zone einer Karte (FR-54).</summary>
    public sealed class MapSpawnZone
    {
        public int TeamId;
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>Statische Beschreibung einer Karte (FR-53–FR-56).</summary>
    public sealed class MapDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public MapSymmetry Symmetry;
        public float SizeX;
        public float SizeZ;
        public int MaxPlayers;
        /// <summary>Power-Ups erlaubt (Arcade-Karten); echte Turnierfelder ohne (FR-09 abschaltbar).</summary>
        public bool AllowPowerUps = true;
        public List<MapCoverBlock> Covers = new();
        public List<MapSpawnZone> Spawns = new();

        /// <summary>FR-55: Symmetrie-Fairness – beide Teams gleich viele Spawns.</summary>
        public bool IsSpawnFair()
        {
            if (Symmetry == MapSymmetry.Asymmetric) return true;
            int team0 = 0, team1 = 0;
            foreach (var s in Spawns)
            {
                if (s.TeamId == 0) team0++;
                else if (s.TeamId == 1) team1++;
            }
            return team0 > 0 && team0 == team1;
        }
    }

    /// <summary>
    /// Karten-Katalog (FR-53–FR-56): drei Launch-Karten (Lagerhaus, Wald, Arena)
    /// mit Deckung, Hindernissen, Nachschubpunkten, Spawn-Zonen und dynamischen
    /// Elementen, und die Event-Karte Pizzeria. Reine Datenlogik (Unity-frei) –
    /// Grundlage für SceneBuilder und symmetrische/asymmetrische Kartendesigns.
    /// </summary>
    public sealed class MapCatalog
    {
        private readonly MapDefinition[] _maps;

        public int Count => _maps.Length;
        public IReadOnlyList<MapDefinition> All => _maps;

        public MapCatalog()
        {
            _maps = new[] { CreateWarehouse(), CreateForest(), CreateArena(), CreateSpeedball(), CreatePizzeria() };
        }

        public MapDefinition Get(int index)
        {
            if (_maps.Length == 0) return null;
            int i = ((index % _maps.Length) + _maps.Length) % _maps.Length;
            return _maps[i];
        }

        public MapDefinition GetById(string id)
        {
            foreach (var map in _maps)
                if (string.Equals(map.Id, id, StringComparison.OrdinalIgnoreCase))
                    return map;
            return null;
        }

        /// <summary>Nächste Karte in Rotation (für Lobby/Moduswechsel).</summary>
        public MapDefinition Next(int currentIndex)
        {
            return Get(currentIndex + 1);
        }

        private static MapDefinition CreateWarehouse()
        {
            var map = new MapDefinition
            {
                Id = "warehouse",
                DisplayName = "Lagerhaus",
                Description = "Symmetrisches Lager mit Containern und Nachschubstation.",
                Symmetry = MapSymmetry.Symmetric,
                SizeX = 80f,
                SizeZ = 80f,
                MaxPlayers = 12
            };
            AddWall(map, 0);
            AddWall(map, 90);
            AddWall(map, 180);
            AddWall(map, 270);

            map.Covers.AddRange(new[]
            {
                new MapCoverBlock { X = -10f, Y = 0.8f, Z = -5f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = 10f, Y = 0.8f, Z = 5f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = -15f, Y = 0.8f, Z = 12f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = 15f, Y = 0.8f, Z = -12f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = -20f, Y = 0.75f, Z = 0f, ScaleX = 3f, ScaleY = 1.5f, ScaleZ = 0.5f },
                new MapCoverBlock { X = 20f, Y = 0.75f, Z = 0f, ScaleX = 3f, ScaleY = 1.5f, ScaleZ = 0.5f },
                new MapCoverBlock { X = 0f, Y = 0.7f, Z = -22f, ScaleX = 4f, ScaleY = 1.4f, ScaleZ = 1f, IsDynamic = true }
            });
            map.Covers.Add(new MapCoverBlock { X = -12f, Y = 0.5f, Z = -12f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });

            for (int i = 0; i < 4; i++)
            {
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -35f + i * 3f, Y = 0.5f, Z = -35f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 35f - i * 3f, Y = 0.5f, Z = 35f });
            }
            return map;
        }

        private static MapDefinition CreateForest()
        {
            var map = new MapDefinition
            {
                Id = "forest",
                DisplayName = "Wald",
                Description = "Lockerer Wald mit Baumstämmen als asymmetrischer Deckung.",
                Symmetry = MapSymmetry.Asymmetric,
                SizeX = 90f,
                SizeZ = 70f,
                MaxPlayers = 10
            };
            map.Covers.AddRange(new[]
            {
                new MapCoverBlock { X = -8f, Y = 1f, Z = -4f, ScaleX = 0.8f, ScaleY = 2f, ScaleZ = 0.8f },
                new MapCoverBlock { X = 6f, Y = 1f, Z = 6f, ScaleX = 0.8f, ScaleY = 2f, ScaleZ = 0.8f },
                new MapCoverBlock { X = 18f, Y = 1f, Z = -10f, ScaleX = 0.8f, ScaleY = 2f, ScaleZ = 0.8f },
                new MapCoverBlock { X = -20f, Y = 1f, Z = 14f, ScaleX = 0.8f, ScaleY = 2f, ScaleZ = 0.8f },
                new MapCoverBlock { X = 2f, Y = 0.7f, Z = -16f, ScaleX = 1.5f, ScaleY = 1.4f, ScaleZ = 0.6f },
                new MapCoverBlock { X = -6f, Y = 0.7f, Z = 18f, ScaleX = 1.5f, ScaleY = 1.4f, ScaleZ = 0.6f, IsDynamic = true }
            });
            map.Covers.Add(new MapCoverBlock { X = 10f, Y = 0.5f, Z = -20f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });
            map.Covers.Add(new MapCoverBlock { X = -22f, Y = 0.5f, Z = 8f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });

            for (int i = 0; i < 5; i++)
            {
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -38f + i * 2f, Y = 0.5f, Z = -28f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 38f - i * 2f, Y = 0.5f, Z = 28f });
            }
            return map;
        }

        private static MapDefinition CreateArena()
        {
            var map = new MapDefinition
            {
                Id = "arena",
                DisplayName = "Arena",
                Description = "Symmetrische Arena mit zentraler Brücke und beweglicher Deckung.",
                Symmetry = MapSymmetry.Symmetric,
                SizeX = 60f,
                SizeZ = 60f,
                MaxPlayers = 8
            };
            AddWall(map, 0);
            AddWall(map, 90);
            AddWall(map, 180);
            AddWall(map, 270);

            map.Covers.AddRange(new[]
            {
                new MapCoverBlock { X = 0f, Y = 1.2f, Z = 0f, ScaleX = 8f, ScaleY = 0.4f, ScaleZ = 3f },
                new MapCoverBlock { X = -14f, Y = 0.8f, Z = 14f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = 14f, Y = 0.8f, Z = -14f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = -14f, Y = 0.8f, Z = -14f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = 14f, Y = 0.8f, Z = 14f, ScaleX = 2f, ScaleY = 1.6f, ScaleZ = 2f },
                new MapCoverBlock { X = 0f, Y = 0.7f, Z = -20f, ScaleX = 3f, ScaleY = 1.4f, ScaleZ = 0.5f, IsDynamic = true }
            });
            map.Covers.Add(new MapCoverBlock { X = -22f, Y = 0.5f, Z = 0f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });
            map.Covers.Add(new MapCoverBlock { X = 22f, Y = 0.5f, Z = 0f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });

            for (int i = 0; i < 4; i++)
            {
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -25f + i * 3f, Y = 0.5f, Z = -25f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 25f - i * 3f, Y = 0.5f, Z = 25f });
            }
            return map;
        }

        /// <summary>
        /// Reales Speedball-Turnierfeld nach NXL-/Millennium-Standard (150 × 120 ft, 45,72 × 36,58 m):
        /// Kunstrasen, Netze, aufblasbare Standard-Bunker (Snake, Dorito, Temple, Can, Cake, Brick,
        /// Tombstone, Maya als „X“). Wie echte Layouts an der Mittellinie gespiegelt – die Snake liegt
        /// für beide Teams an derselben Seitenlinie. Keine beweglichen Bunker, Nachladen nur an der
        /// eigenen Start-Box. Entwurf: Design-Canvas „Kartenplan“ (scratch speedball.py).
        /// </summary>
        private static MapDefinition CreateSpeedball()
        {
            var map = new MapDefinition
            {
                Id = "speedball",
                DisplayName = "Turnierfeld",
                Description = "Echtes Speedball-Turnierfeld (NXL-Standard, 150 × 120 ft) mit aufblasbaren Bunkern.",
                Symmetry = MapSymmetry.Symmetric,
                SizeX = 36.58f,
                SizeZ = 45.72f,
                MaxPlayers = 10,
                AllowPowerUps = false
            };
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 1.5f, Z = -23.11f, ScaleX = 37.58f, ScaleY = 3f, ScaleZ = 0.5f, Kind = "net" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 1.5f, Z = 23.11f, ScaleX = 37.58f, ScaleY = 3f, ScaleZ = 0.5f, Kind = "net" });
            map.Covers.Add(new MapCoverBlock { X = -18.54f, Y = 1.5f, Z = 0f, ScaleX = 0.5f, ScaleY = 3f, ScaleZ = 46.72f, Kind = "net" });
            map.Covers.Add(new MapCoverBlock { X = 18.54f, Y = 1.5f, Z = 0f, ScaleX = 0.5f, ScaleY = 3f, ScaleZ = 46.72f, Kind = "net" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 1.2f, Z = 0f, ScaleX = 3f, ScaleY = 2.4f, ScaleZ = 3f, Kind = "maya" });
            map.Covers.Add(new MapCoverBlock { X = -9.5f, Y = 0.6f, Z = 0f, ScaleX = 1.2f, ScaleY = 1.2f, ScaleZ = 3f, Kind = "brick" });
            map.Covers.Add(new MapCoverBlock { X = 10f, Y = 0.75f, Z = 0f, ScaleX = 1.5f, ScaleY = 1.5f, ScaleZ = 1.5f, Kind = "can" });
            map.Covers.Add(new MapCoverBlock { X = -15.2f, Y = 0.55f, Z = -5.5f, ScaleX = 1.2f, ScaleY = 1.1f, ScaleZ = 7f, Kind = "snake" });
            map.Covers.Add(new MapCoverBlock { X = -15.2f, Y = 0.75f, Z = -13f, ScaleX = 1.5f, ScaleY = 1.5f, ScaleZ = 1.5f, Kind = "can" });
            map.Covers.Add(new MapCoverBlock { X = -10f, Y = 0.9f, Z = -8.5f, ScaleX = 2.6f, ScaleY = 1.8f, ScaleZ = 1.3f, Kind = "temple" });
            map.Covers.Add(new MapCoverBlock { X = -6f, Y = 0.6f, Z = -13.5f, ScaleX = 3f, ScaleY = 1.2f, ScaleZ = 1.2f, Kind = "brick" });
            map.Covers.Add(new MapCoverBlock { X = -4f, Y = 0.6f, Z = -3f, ScaleX = 3f, ScaleY = 1.2f, ScaleZ = 1.2f, Kind = "brick" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 0.65f, Z = -16.5f, ScaleX = 2.2f, ScaleY = 1.3f, ScaleZ = 2.2f, Kind = "cake" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 0.75f, Z = -9f, ScaleX = 1.5f, ScaleY = 1.5f, ScaleZ = 1.5f, Kind = "can" });
            map.Covers.Add(new MapCoverBlock { X = 5.5f, Y = 0.8f, Z = -12f, ScaleX = 1.2f, ScaleY = 1.6f, ScaleZ = 0.7f, Kind = "tombstone" });
            map.Covers.Add(new MapCoverBlock { X = 9f, Y = 0.45f, Z = -16f, ScaleX = 1.6f, ScaleY = 0.9f, ScaleZ = 1.2f, Kind = "minidorito" });
            map.Covers.Add(new MapCoverBlock { X = 12.5f, Y = 0.65f, Z = -8f, ScaleX = 2f, ScaleY = 1.3f, ScaleZ = 1.6f, Kind = "dorito" });
            map.Covers.Add(new MapCoverBlock { X = 15.5f, Y = 0.65f, Z = -3f, ScaleX = 2f, ScaleY = 1.3f, ScaleZ = 1.6f, Kind = "dorito" });
            map.Covers.Add(new MapCoverBlock { X = 15.5f, Y = 0.45f, Z = -14.5f, ScaleX = 1.6f, ScaleY = 0.9f, ScaleZ = 1.2f, Kind = "minidorito" });
            map.Covers.Add(new MapCoverBlock { X = 6.5f, Y = 0.9f, Z = -4.5f, ScaleX = 2.6f, ScaleY = 1.8f, ScaleZ = 1.3f, Kind = "temple" });
            map.Covers.Add(new MapCoverBlock { X = -6.5f, Y = 0.45f, Z = -21.9f, ScaleX = 1.2f, ScaleY = 0.9f, ScaleZ = 0.8f, Kind = "podrack", IsResupply = true });
            map.Covers.Add(new MapCoverBlock { X = 6.5f, Y = 0.45f, Z = -21.9f, ScaleX = 1.2f, ScaleY = 0.9f, ScaleZ = 0.8f, Kind = "podrack", IsResupply = true });
            map.Covers.Add(new MapCoverBlock { X = -15.2f, Y = 0.55f, Z = 5.5f, ScaleX = 1.2f, ScaleY = 1.1f, ScaleZ = 7f, Kind = "snake" });
            map.Covers.Add(new MapCoverBlock { X = -15.2f, Y = 0.75f, Z = 13f, ScaleX = 1.5f, ScaleY = 1.5f, ScaleZ = 1.5f, Kind = "can" });
            map.Covers.Add(new MapCoverBlock { X = -10f, Y = 0.9f, Z = 8.5f, ScaleX = 2.6f, ScaleY = 1.8f, ScaleZ = 1.3f, Kind = "temple" });
            map.Covers.Add(new MapCoverBlock { X = -6f, Y = 0.6f, Z = 13.5f, ScaleX = 3f, ScaleY = 1.2f, ScaleZ = 1.2f, Kind = "brick" });
            map.Covers.Add(new MapCoverBlock { X = -4f, Y = 0.6f, Z = 3f, ScaleX = 3f, ScaleY = 1.2f, ScaleZ = 1.2f, Kind = "brick" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 0.65f, Z = 16.5f, ScaleX = 2.2f, ScaleY = 1.3f, ScaleZ = 2.2f, Kind = "cake" });
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 0.75f, Z = 9f, ScaleX = 1.5f, ScaleY = 1.5f, ScaleZ = 1.5f, Kind = "can" });
            map.Covers.Add(new MapCoverBlock { X = 5.5f, Y = 0.8f, Z = 12f, ScaleX = 1.2f, ScaleY = 1.6f, ScaleZ = 0.7f, Kind = "tombstone" });
            map.Covers.Add(new MapCoverBlock { X = 9f, Y = 0.45f, Z = 16f, ScaleX = 1.6f, ScaleY = 0.9f, ScaleZ = 1.2f, Kind = "minidorito" });
            map.Covers.Add(new MapCoverBlock { X = 12.5f, Y = 0.65f, Z = 8f, ScaleX = 2f, ScaleY = 1.3f, ScaleZ = 1.6f, Kind = "dorito" });
            map.Covers.Add(new MapCoverBlock { X = 15.5f, Y = 0.65f, Z = 3f, ScaleX = 2f, ScaleY = 1.3f, ScaleZ = 1.6f, Kind = "dorito" });
            map.Covers.Add(new MapCoverBlock { X = 15.5f, Y = 0.45f, Z = 14.5f, ScaleX = 1.6f, ScaleY = 0.9f, ScaleZ = 1.2f, Kind = "minidorito" });
            map.Covers.Add(new MapCoverBlock { X = 6.5f, Y = 0.9f, Z = 4.5f, ScaleX = 2.6f, ScaleY = 1.8f, ScaleZ = 1.3f, Kind = "temple" });
            map.Covers.Add(new MapCoverBlock { X = -6.5f, Y = 0.45f, Z = 21.9f, ScaleX = 1.2f, ScaleY = 0.9f, ScaleZ = 0.8f, Kind = "podrack", IsResupply = true });
            map.Covers.Add(new MapCoverBlock { X = 6.5f, Y = 0.45f, Z = 21.9f, ScaleX = 1.2f, ScaleY = 0.9f, ScaleZ = 0.8f, Kind = "podrack", IsResupply = true });
            // Reifenstapel (je 5 echte Autoreifen, Ø 0,6 m) – klassische Deckung im Hocken, gespiegelt
            foreach (float side in new[] { -1f, 1f })
            {
                map.Covers.Add(new MapCoverBlock { X = 3f, Y = 0.42f, Z = side * 7f, ScaleX = 1.25f, ScaleY = 0.84f, ScaleZ = 0.62f, Kind = "tires" });
                map.Covers.Add(new MapCoverBlock { X = -11f, Y = 0.42f, Z = side * 17.5f, ScaleX = 1.25f, ScaleY = 0.84f, ScaleZ = 0.62f, Kind = "tires" });
                map.Covers.Add(new MapCoverBlock { X = 12f, Y = 0.42f, Z = side * 19.5f, ScaleX = 1.9f, ScaleY = 0.84f, ScaleZ = 0.62f, Kind = "tires" });
            }
            map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -4f, Y = 0f, Z = -21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -2f, Y = 0f, Z = -21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = 0f, Y = 0f, Z = -21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = 2f, Y = 0f, Z = -21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = 4f, Y = 0f, Z = -21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = -4f, Y = 0f, Z = 21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = -2f, Y = 0f, Z = 21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 0f, Y = 0f, Z = 21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 2f, Y = 0f, Z = 21.3f });
            map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 4f, Y = 0f, Z = 21.3f });

            return map;
        }

        /// <summary>
        /// Event-Karte „Pizzeria“ (KERAVONOS-Pizza-Event): 40 × 50 m, an der Mittellinie (Z = 0) gespiegelt, 10 gegen 10.
        /// Die Teams starten an den Schmalseiten (Z = ±21,5). Deckung: drei Holzöfen (blickdicht), Theken (hüfthoch),
        /// Tische (niedrig), Mehlsäcke, Kühlschränke; Pizzakarton-Stapel sind der Nachschub. Rand aus Backsteinwänden.
        /// </summary>
        private static MapDefinition CreatePizzeria()
        {
            var map = new MapDefinition
            {
                Id = "pizzeria",
                DisplayName = "Pizzeria",
                Description = "Pizzeria mit Holzöfen, Theken und Pizzakartons – gebaut für 10 gegen 10.",
                Symmetry = MapSymmetry.Symmetric,
                SizeX = 40f,
                SizeZ = 50f,
                MaxPlayers = 20,
                AllowPowerUps = true
            };

            void Add(float x, float z, float sx, float sy, float sz, string kind, bool resupply = false)
                => map.Covers.Add(new MapCoverBlock { X = x, Y = sy / 2f, Z = z, ScaleX = sx, ScaleY = sy, ScaleZ = sz, Kind = kind, IsResupply = resupply });
            void Mirrored(float x, float z, float sx, float sy, float sz, string kind, bool resupply = false)
            {
                Add(x, -z, sx, sy, sz, kind, resupply);
                Add(x, z, sx, sy, sz, kind, resupply);
            }

            // Backsteinwände als Rand, vollständig innerhalb der Karte
            Mirrored(0f, 24.5f, 40f, 4f, 1f, "boundary");
            Add(-19.5f, 0f, 1f, 4f, 48f, "boundary");
            Add(19.5f, 0f, 1f, 4f, 48f, "boundary");
            // Holzöfen mit Glut: einer in der Mitte, zwei an den Seiten (blickdicht)
            Add(0f, 0f, 4f, 2.6f, 4f, "oven");
            Add(-14f, 0f, 3f, 2.6f, 3f, "oven");
            Add(14f, 0f, 3f, 2.6f, 3f, "oven");
            // Theken (hüfthoch, Deckung im Hocken)
            Mirrored(-7f, 9f, 6f, 1.1f, 1.2f, "counter");
            Mirrored(7f, 5f, 1.2f, 1.1f, 5f, "counter");
            // Tische mit Karodecke (niedrig)
            Mirrored(-12f, 14f, 2f, 0.8f, 2f, "table");
            Mirrored(4f, 13f, 2f, 0.8f, 2f, "table");
            Mirrored(-3f, 16f, 1.6f, 0.8f, 1.6f, "table");
            // Mehlsäcke
            Mirrored(12f, 15f, 1.6f, 0.9f, 1f, "flour");
            Mirrored(-15f, 7f, 1f, 0.9f, 1.6f, "flour");
            // Kühlschränke (hoch und schmal): an der Wand und als Sichtschutz vor den Startlinien
            Mirrored(18.4f, 9f, 1f, 2.2f, 0.8f, "fridge");
            Mirrored(-4.5f, 18.5f, 0.8f, 2.2f, 0.8f, "fridge");
            Mirrored(4.5f, 18.5f, 0.8f, 2.2f, 0.8f, "fridge");
            // Pizzakarton-Stapel = Nachschub, je zwei neben jeder Startlinie
            Mirrored(-13f, 21.5f, 1.2f, 1f, 1.2f, "pizzabox", resupply: true);
            Mirrored(13f, 21.5f, 1.2f, 1f, 1.2f, "pizzabox", resupply: true);

            for (int i = 0; i < 10; i++)
            {
                float x = -9f + i * 2f;
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = x, Y = 0f, Z = -21.5f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = x, Y = 0f, Z = 21.5f });
            }
            return map;
        }

        private static void AddWall(MapDefinition map, int angleDeg)
        {
            float rad = angleDeg * MathF.PI / 180f;
            float cx = MathF.Cos(rad);
            float cz = MathF.Sin(rad);
            float half = map.SizeX / 2f;
            float length = map.SizeZ;
            map.Covers.Add(new MapCoverBlock
            {
                X = cx < 0f ? -half : (cx > 0f ? half : 0f),
                Z = cz < 0f ? -half : (cz > 0f ? half : 0f),
                Y = 2f,
                ScaleX = Math.Abs(cx) > 0.1f ? 1f : length,
                ScaleY = 4f,
                ScaleZ = Math.Abs(cz) > 0.1f ? 1f : length,
                Kind = "boundary"
            });
        }
    }
}