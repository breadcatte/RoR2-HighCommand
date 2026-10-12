using System;

namespace HighCommand.UI
{
    /// <summary>The small pictures on the command card. Drawn in code (the mod ships no image files).</summary>
    internal enum Glyph
    {
        AttackMove, Stop, Hold, Focus, Retreat, Patrol, Rally, Formation,
        Aggressive, Defensive, Passive, Teleporter,
        OrbitalStrike, Medic, WarCry, Mark,
        Count
    }

    /// <summary>
    /// Draws a glyph into a plain alpha array (no engine types, so a small console tool can render a contact sheet
    /// for a quick look). The design grid is 64 x 64 with the origin at the bottom left; shapes are anti-aliased by their
    /// distance to the outline.
    /// </summary>
    internal static class GlyphArt
    {
        /// <summary>Alpha values, row 0 = bottom row, <paramref name="size"/> x <paramref name="size"/>.</summary>
        internal static float[] Render(Glyph glyph, int size)
        {
            var p = new Painter(size);
            switch (glyph)
            {
                case Glyph.AttackMove:
                    // two crossed swords, blades up
                    p.Sword(14f, 8f, 50f, 54f);
                    p.Sword(50f, 8f, 14f, 54f);
                    break;

                case Glyph.Stop:
                    p.Ring(32f, 32f, 25f, 4.5f);
                    p.Rect(21f, 21f, 43f, 43f);
                    break;

                case Glyph.Hold:
                    p.Rect(16f, 12f, 28f, 52f);
                    p.Rect(36f, 12f, 48f, 52f);
                    break;

                case Glyph.Focus:
                    p.Ring(32f, 32f, 17f, 4.5f);
                    p.Disc(32f, 32f, 5f);
                    p.Seg(32f, 4f, 32f, 15f, 4.5f);
                    p.Seg(32f, 49f, 32f, 60f, 4.5f);
                    p.Seg(4f, 32f, 15f, 32f, 4.5f);
                    p.Seg(49f, 32f, 60f, 32f, 4.5f);
                    break;

                case Glyph.Retreat:
                    // a U-turn arrow: out of the fight and back
                    p.Arc(30f, 34f, 14f, 0f, 180f, 6f);
                    p.Seg(44f, 34f, 44f, 12f, 6f);
                    p.Tri(18f, 12f, 4f, 28f, 32f, 28f);
                    break;

                case Glyph.Patrol:
                    p.Seg(16f, 32f, 48f, 32f, 6f);
                    p.Tri(4f, 32f, 20f, 18f, 20f, 46f);
                    p.Tri(60f, 32f, 44f, 18f, 44f, 46f);
                    break;

                case Glyph.Rally:
                    p.Seg(16f, 6f, 16f, 58f, 5f);
                    p.Tri(16f, 58f, 56f, 44f, 16f, 30f);
                    p.Disc(16f, 7f, 5f);
                    break;

                case Glyph.Formation:
                    p.Disc(32f, 49f, 6f);
                    p.Disc(18f, 32f, 6f);
                    p.Disc(46f, 32f, 6f);
                    p.Disc(8f, 15f, 6f);
                    p.Disc(32f, 15f, 6f);
                    p.Disc(56f, 15f, 6f);
                    break;

                case Glyph.Aggressive:
                    // three chevrons pointing up: press forward
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 8f + i * 17f;
                        p.Seg(10f, y, 32f, y + 17f, 7f);
                        p.Seg(54f, y, 32f, y + 17f, 7f);
                    }
                    break;

                case Glyph.Defensive:
                    {
                        float[] shield = { 10f, 52f, 54f, 52f, 54f, 30f, 32f, 5f, 10f, 30f };
                        p.Poly(shield);
                        p.Erase(shield, 0.68f, 32f, 32f);
                        p.Poly(Scale(shield, 0.42f, 32f, 31f));
                        break;
                    }

                case Glyph.Passive:
                    p.Ring(32f, 32f, 25f, 4.5f);
                    p.Rect(18f, 28f, 46f, 36f);
                    break;

                case Glyph.Teleporter:
                    p.Ring(32f, 32f, 26f, 4.5f);
                    p.Ring(32f, 32f, 15f, 4.5f);
                    p.Disc(32f, 32f, 4f);
                    break;

                case Glyph.OrbitalStrike:
                    p.Seg(32f, 62f, 32f, 34f, 6f);
                    p.Tri(32f, 18f, 19f, 36f, 45f, 36f);
                    p.Ring(32f, 11f, 9f, 3.5f);
                    break;

                case Glyph.Medic:
                    p.Rect(25f, 8f, 39f, 56f);
                    p.Rect(8f, 25f, 56f, 39f);
                    break;

                case Glyph.WarCry:
                    p.Rect(4f, 24f, 20f, 40f);
                    p.Poly(new[] { 16f, 26f, 40f, 8f, 40f, 56f, 16f, 38f });
                    p.Arc(38f, 32f, 15f, -40f, 40f, 4.5f);
                    p.Arc(38f, 32f, 25f, -40f, 40f, 4.5f);
                    break;

                case Glyph.Mark:
                    p.Poly(new[] { 32f, 60f, 56f, 32f, 32f, 4f, 8f, 32f });
                    p.Erase(new[] { 32f, 60f, 56f, 32f, 32f, 4f, 8f, 32f }, 0.62f, 32f, 32f);
                    p.Disc(32f, 32f, 7f);
                    break;
            }
            return p.Alpha;
        }

        private static float[] Scale(float[] pts, float k, float cx, float cy)
        {
            var r = new float[pts.Length];
            for (int i = 0; i < pts.Length; i += 2)
            {
                r[i] = cx + (pts[i] - cx) * k;
                r[i + 1] = cy + (pts[i + 1] - cy) * k;
            }
            return r;
        }

        private sealed class Painter
        {
            private readonly int n;
            private readonly float k; // design units -> pixels
            internal readonly float[] Alpha;

            internal Painter(int size)
            {
                n = size;
                k = size / 64f;
                Alpha = new float[size * size];
            }

            private delegate float Cover(float x, float y);

            private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

            // coverage functions get the pixel centre in design units
            private void Draw(Cover cover, bool erase = false)
            {
                for (int py = 0; py < n; py++)
                    for (int px = 0; px < n; px++)
                    {
                        float c = Clamp01(cover((px + 0.5f) / k, (py + 0.5f) / k));
                        if (c <= 0f) continue;
                        int i = py * n + px;
                        if (erase) Alpha[i] *= 1f - c;
                        else if (c > Alpha[i]) Alpha[i] = c;
                    }
            }

            // distances are in design units; one pixel is 1 / k of them
            private float Edge(float signedDistance) => Clamp01(signedDistance * k + 0.5f);

            internal void Disc(float cx, float cy, float r)
            {
                Draw((x, y) => Edge(r - (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy))));
            }

            internal void Ring(float cx, float cy, float r, float w)
            {
                Draw((x, y) => Edge(w * 0.5f - Math.Abs((float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r)));
            }

            internal void Arc(float cx, float cy, float r, float fromDeg, float toDeg, float w)
            {
                float mid = (fromDeg + toDeg) * 0.5f;
                float half = (toDeg - fromDeg) * 0.5f;
                Draw((x, y) =>
                {
                    float dx = x - cx, dy = y - cy;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                    float diff = Math.Abs(((a - mid + 540f) % 360f) - 180f);
                    float ring = w * 0.5f - Math.Abs(d - r);
                    // the ends are cut straight: the angular margin in design units
                    float cap = (half - diff) * (float)Math.PI / 180f * Math.Max(d, 1f);
                    return Edge(Math.Min(ring, cap));
                });
            }

            internal void Seg(float ax, float ay, float bx, float by, float w)
            {
                float dx = bx - ax, dy = by - ay;
                float len2 = Math.Max(1e-4f, dx * dx + dy * dy);
                Draw((x, y) =>
                {
                    float t = Clamp01(((x - ax) * dx + (y - ay) * dy) / len2);
                    float qx = ax + dx * t - x, qy = ay + dy * t - y;
                    return Edge(w * 0.5f - (float)Math.Sqrt(qx * qx + qy * qy));
                });
            }

            internal void Rect(float x0, float y0, float x1, float y1)
            {
                Draw((x, y) => Edge(Math.Min(Math.Min(x - x0, x1 - x), Math.Min(y - y0, y1 - y))));
            }

            internal void Tri(float ax, float ay, float bx, float by, float cx, float cy)
            {
                Poly(new[] { ax, ay, bx, by, cx, cy });
            }

            internal void Poly(float[] pts) => Draw(PolyCover(pts, 1f, 0f, 0f));

            /// <summary>Removes a convex polygon scaled about (cx, cy).</summary>
            internal void Erase(float[] pts, float scale, float cx, float cy) => Draw(PolyCover(pts, scale, cx, cy), erase: true);

            private Cover PolyCover(float[] pts, float scale, float cx, float cy)
            {
                int count = pts.Length / 2;
                var px = new float[count];
                var py = new float[count];
                for (int i = 0; i < count; i++)
                {
                    px[i] = scale == 1f ? pts[2 * i] : cx + (pts[2 * i] - cx) * scale;
                    py[i] = scale == 1f ? pts[2 * i + 1] : cy + (pts[2 * i + 1] - cy) * scale;
                }
                float area = 0f;
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    area += px[i] * py[j] - px[j] * py[i];
                }
                float side = area >= 0f ? 1f : -1f; // counter-clockwise: the inside is on the left

                return (x, y) =>
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        int j = (i + 1) % count;
                        float ex = px[j] - px[i], ey = py[j] - py[i];
                        float len = Math.Max(1e-4f, (float)Math.Sqrt(ex * ex + ey * ey));
                        float d = side * (ex * (y - py[i]) - ey * (x - px[i])) / len;
                        if (d < best) best = d;
                    }
                    return Edge(best);
                };
            }

            /// <summary>A blade from the hilt to the point with a cross guard and a pommel.</summary>
            internal void Sword(float hx, float hy, float tx, float ty)
            {
                float dx = tx - hx, dy = ty - hy;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                float ux = dx / len, uy = dy / len;     // along the blade
                float nx = -uy, ny = ux;                // across it
                Seg(hx + ux * 8f, hy + uy * 8f, tx - ux * 4f, ty - uy * 4f, 6.5f);
                Tri(tx, ty, tx - ux * 12f + nx * 3.2f, ty - uy * 12f + ny * 3.2f, tx - ux * 12f - nx * 3.2f, ty - uy * 12f - ny * 3.2f);
                float gx = hx + ux * 14f, gy = hy + uy * 14f;
                Seg(gx - nx * 9f, gy - ny * 9f, gx + nx * 9f, gy + ny * 9f, 4.5f);
                Disc(hx + ux * 3f, hy + uy * 3f, 3.6f);
            }
        }
    }
}
