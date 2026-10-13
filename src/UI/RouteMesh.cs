using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// One piece of mesh for all order lines of a frame: soft strips that bend along a way with the colour (and its
    /// fading) given per point, the rings round the targets included. Pooled images per piece would cost far more than
    /// these few hundred vertices rebuilt each frame. Points are given in screen pixels.
    /// </summary>
    internal sealed class RouteMesh : MaskableGraphic
    {
        private readonly List<UIVertex> verts = new List<UIVertex>(1024);
        private readonly List<int> indices = new List<int>(2048);
        private Texture texture;
        private bool dirty;

        internal static RouteMesh Create(string name, Transform parent, Texture texture)
        {
            RectTransform rt = UiKit.NewRect(name, parent);
            UiKit.Stretch(rt);
            RouteMesh mesh = rt.gameObject.AddComponent<RouteMesh>();
            mesh.texture = texture;
            mesh.raycastTarget = false;
            mesh.color = Color.white;
            return mesh;
        }

        public override Texture mainTexture => texture ? texture : s_WhiteTexture;

        internal int Vertices => verts.Count;

        internal void Begin()
        {
            if (verts.Count > 0) dirty = true;
            verts.Clear();
            indices.Clear();
        }

        internal void End()
        {
            if (dirty || verts.Count > 0) SetVerticesDirty();
            dirty = false;
        }

        // screen pixels -> this graphic's own space
        private Vector2 Local(Vector2 screen) => rectTransform.InverseTransformPoint(new Vector3(screen.x, screen.y, 0f));

        /// <summary>
        /// A soft strip through the points (screen pixels), <paramref name="width"/> in canvas units, coloured point by
        /// point, the width optionally scaled point by point. Corners are joined by mitres (clamped at sharp turns).
        /// </summary>
        internal void Strip(List<Vector2> points, List<Color32> colors, int start, int count, float width, List<float> widths = null)
        {
            if (count < 2) return;
            float half = width * 0.5f;
            int baseIndex = verts.Count;
            for (int k = 0; k < count; k++)
            {
                int i = start + k;
                Vector2 p = Local(points[i]);
                Vector2 prev = k > 0 ? Local(points[i - 1]) : p;
                Vector2 next = k < count - 1 ? Local(points[i + 1]) : p;
                Vector2 dIn = (p - prev).normalized;
                Vector2 dOut = (next - p).normalized;
                if (k == 0) dIn = dOut;
                if (k == count - 1) dOut = dIn;
                Vector2 tangent = (dIn + dOut);
                if (tangent.sqrMagnitude < 1e-6f) tangent = dOut;
                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                Vector2 nIn = new Vector2(-dIn.y, dIn.x);
                float miter = Mathf.Min(2f, 1f / Mathf.Max(0.35f, Vector2.Dot(normal, nIn)));

                Color32 c32 = colors[i];
                float h = widths != null ? half * widths[i] : half;
                verts.Add(Vertex(p + normal * (h * miter), c32, new Vector2(0.5f, 1f)));
                verts.Add(Vertex(p - normal * (h * miter), c32, new Vector2(0.5f, 0f)));
            }
            for (int k = 0; k < count - 1; k++)
            {
                int a = baseIndex + k * 2;
                indices.Add(a); indices.Add(a + 2); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(a + 2); indices.Add(a + 3);
            }
        }

        private static UIVertex Vertex(Vector2 position, Color32 color, Vector2 uv)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = new Vector3(position.x, position.y, 0f);
            v.color = color;
            v.uv0 = uv;
            return v;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (verts.Count > 0) vh.AddUIVertexStream(verts, indices);
        }
    }
}
