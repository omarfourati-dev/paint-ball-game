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
    /// Elementen. Reine Datenlogik (Unity-frei) – Grundlage für SceneBuilder und
    /// symmetrische/asymmetrische Kartendesigns.
    /// </summary>
    public sealed class MapCatalog
    {
        private readonly MapDefinition[] _maps;

        public int Count => _maps.Length;
        public IReadOnlyList<MapDefinition> All => _maps;

        public MapCatalog()
        {
            _maps = new[] { CreateWarehouse(), CreateForest(), CreateArena() };
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
                ScaleZ = Math.Abs(cz) > 0.1f ? 1f : length
            });
        }
    }
}