using UnityEngine;

namespace Paintball.Unity.Decals
{
    /// <summary>
    /// Ermöglicht das Auftragen von Paintball-Decals auf Oberflächen (FR-04).
    /// Decals bleiben sichtbar, damit Treffer auch aus Distanz erkennbar bleiben.
    /// </summary>
    public sealed class PaintSurface : MonoBehaviour
    {
        [Header("Decal Settings")]
        [SerializeField] private Material _decalMaterial;
        [SerializeField] private float _decalSize = 0.15f;
        [SerializeField] private int _maxDecals = 50;
        [SerializeField] private float _decalLifetime = 30f;

        private readonly System.Collections.Generic.Queue<GameObject> _decals = new();

        public void SpawnDecal(Vector3 worldPosition, Vector3 normal, Color color)
        {
            if (_decalMaterial == null) return;

            if (_decals.Count >= _maxDecals)
            {
                GameObject oldest = _decals.Dequeue();
                if (oldest != null) Destroy(oldest);
            }

            GameObject decalObj = new GameObject("PaintDecal");
            decalObj.transform.position = worldPosition + normal * 0.01f;
            decalObj.transform.rotation = Quaternion.LookRotation(-normal);
            decalObj.transform.localScale = Vector3.one * _decalSize;

            var renderer = decalObj.AddComponent<MeshRenderer>();
            var filter = decalObj.AddComponent<MeshFilter>();

            Mesh quad = CreateQuad();
            filter.mesh = quad;

            renderer.material = new Material(_decalMaterial);
            renderer.material.color = color;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _decals.Enqueue(decalObj);
            Destroy(decalObj, _decalLifetime);
        }

        public void SpawnDecal(Vector3 contact, Color color)
        {
            SpawnDecal(transform.position + contact, transform.up, color);
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "DecalQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
