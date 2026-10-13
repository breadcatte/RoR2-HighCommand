using System;

namespace HighCommand.UI
{
    /// <summary>The small pictures on the command card. Drawn in code (the mod ships no image files).</summary>
    internal enum Glyph
    {
        AttackMove, Stop, Hold, Focus, Retreat, Patrol, Rally, Formation,
        Aggressive, Defensive, Passive, Teleporter,
        OrbitalStrike, Medic, WarCry, Mark,
        Move, Attack, Follow, Interact, Select, ChevronDown,
        Coin, Person, Clock, Chest, Cancel,
        SeekEnemies, SeekChests,
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
                    // a satellite fires a beam down onto a target mark on the ground
                    p.Rect(28f, 53f, 36f, 62f);
                    p.Rect(11f, 55f, 25f, 60f);
                    p.Rect(39f, 55f, 53f, 60f);
                    p.Poly(new[] { 30f, 51f, 34f, 51f, 36.5f, 16f, 27.5f, 16f });
                    p.EllipseRing(32f, 14f, 25f, 8.5f, 4f);
                    p.Ellipse(32f, 14f, 10f, 3.6f);
                    p.Seg(21f, 22f, 14f, 30f, 3.5f);
                    p.Seg(43f, 22f, 50f, 30f, 3.5f);
                    break;

                case Glyph.Medic:
                    // a first aid kit: a handle, the case (cut like the mod's panels) and a cross cut out of it
                    p.Seg(23f, 44f, 23f, 54f, 5f);
                    p.Seg(41f, 44f, 41f, 54f, 5f);
                    p.Seg(23f, 54f, 41f, 54f, 5f);
                    {
                        float[] kit = { 13f, 46f, 4f, 37f, 4f, 6f, 51f, 6f, 60f, 15f, 60f, 46f };
                        p.Poly(kit);
                    }
                    p.Erase(new[] { 27f, 13f, 37f, 13f, 37f, 39f, 27f, 39f }, 1f, 32f, 32f);
                    p.Erase(new[] { 19f, 21f, 45f, 21f, 45f, 31f, 19f, 31f }, 1f, 32f, 32f);
                    break;

                case Glyph.WarCry:
                    // a war horn, its bell to the upper right, and the call coming out of it
                    {
                        const float bx = 38f, by = 30f;              // the middle of the bell's mouth
                        const float ux = 0.5f, uy = 0.866f;          // the way it blows (60 degrees)
                        const float vx = 0.866f, vy = -0.5f;         // across the mouth
                        p.Horn(6f, 27f, bx - 30f * ux, by - 30f * uy, bx, by, 3.8f, 13f, 0.38f, 0.62f);
                        p.Ring(6f, 27f, 4.4f, 3.2f);
                        // cut the round end off flat, then a rim along the mouth
                        p.Erase(new[]
                        {
                            bx - vx * 20f, by - vy * 20f, bx + vx * 20f, by + vy * 20f,
                            bx + vx * 20f + ux * 20f, by + vy * 20f + uy * 20f, bx - vx * 20f + ux * 20f, by - vy * 20f + uy * 20f
                        }, 1f, 32f, 32f);
                        p.Seg(bx - vx * 16f, by - vy * 16f, bx + vx * 16f, by + vy * 16f, 5.5f);
                        p.Arc(bx, by, 19f, 32f, 88f, 5f);
                        p.Arc(bx, by, 27f, 38f, 82f, 5f);
                        break;
                    }

                case Glyph.Mark:
                    // a skull in the corners of a sight: this one dies first
                    for (int cx = 0; cx < 2; cx++)
                        for (int cy = 0; cy < 2; cy++)
                        {
                            float x = cx == 0 ? 5f : 59f, y = cy == 0 ? 5f : 59f;
                            float dx = cx == 0 ? 14f : -14f, dy = cy == 0 ? 14f : -14f;
                            p.Seg(x, y, x + dx, y, 5f);
                            p.Seg(x, y, x, y + dy, 5f);
                        }
                    p.Disc(32f, 36f, 15f);
                    p.Rect(23f, 17f, 41f, 30f);
                    p.Hole(26f, 35f, 4.6f);
                    p.Hole(38f, 35f, 4.6f);
                    p.Erase(new[] { 32f, 30.5f, 34.5f, 25.5f, 29.5f, 25.5f }, 1f, 32f, 32f);
                    p.Erase(new[] { 28.2f, 17f, 30f, 17f, 30f, 22f, 28.2f, 22f }, 1f, 32f, 32f);
                    p.Erase(new[] { 34f, 17f, 35.8f, 17f, 35.8f, 22f, 34f, 22f }, 1f, 32f, 32f);
                    break;

                case Glyph.Move:
                    p.Seg(12f, 12f, 38f, 38f, 8f);
                    p.Tri(58f, 58f, 26f, 50f, 50f, 26f);
                    p.Disc(11f, 11f, 6f);
                    break;

                case Glyph.Attack:
                    p.Sword(10f, 8f, 56f, 56f);
                    break;

                case Glyph.Follow:
                    for (int i = 0; i < 2; i++)
                    {
                        float x = 8f + i * 22f;
                        p.Seg(x, 10f, x + 20f, 32f, 8f);
                        p.Seg(x, 54f, x + 20f, 32f, 8f);
                    }
                    break;

                case Glyph.Interact:
                    // a cog: a ring with eight teeth
                    p.Ring(32f, 32f, 13f, 9f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                        p.Seg(32f + cos * 17f, 32f + sin * 17f, 32f + cos * 26f, 32f + sin * 26f, 8f);
                    }
                    break;

                case Glyph.Select:
                    // four corner brackets round a dot
                    for (int cx = 0; cx < 2; cx++)
                        for (int cy = 0; cy < 2; cy++)
                        {
                            float x = cx == 0 ? 7f : 57f, y = cy == 0 ? 7f : 57f;
                            float dx = cx == 0 ? 18f : -18f, dy = cy == 0 ? 18f : -18f;
                            p.Seg(x, y - (cy == 0 ? 2.5f : -2.5f), x + dx, y - (cy == 0 ? 2.5f : -2.5f), 5f);
                            p.Seg(x - (cx == 0 ? 2.5f : -2.5f), y, x - (cx == 0 ? 2.5f : -2.5f), y + dy, 5f);
                        }
                    p.Disc(32f, 32f, 6f);
                    break;

                case Glyph.ChevronDown:
                    p.Seg(10f, 42f, 32f, 20f, 8f);
                    p.Seg(54f, 42f, 32f, 20f, 8f);
                    break;

                case Glyph.Coin:
                    p.Ring(32f, 32f, 25f, 7f);
                    p.Rect(27f, 14f, 37f, 50f);
                    break;

                case Glyph.Person:
                    p.Disc(32f, 47f, 11f);
                    p.Poly(new[] { 8f, 4f, 56f, 4f, 53f, 27f, 42f, 35f, 22f, 35f, 11f, 27f });
                    break;

                case Glyph.Clock:
                    p.Ring(32f, 32f, 25f, 6.5f);
                    p.Seg(32f, 32f, 32f, 50f, 6f);
                    p.Seg(32f, 32f, 46f, 24f, 6f);
                    break;

                case Glyph.Chest:
                    // a chest: the body, the lid above a thin gap, and a latch across both
                    p.Rect(7f, 7f, 57f, 31f);
                    p.Rect(7f, 35f, 57f, 53f);
                    p.Erase(new[] { 25f, 21f, 39f, 21f, 39f, 44f, 25f, 44f }, 1f, 32f, 32f);
                    p.Rect(28.5f, 24f, 35.5f, 41f);
                    break;

                case Glyph.Cancel:
                    // a cross
                    p.Seg(13f, 13f, 51f, 51f, 9f);
                    p.Seg(13f, 51f, 51f, 13f, 9f);
                    break;

                case Glyph.SeekEnemies:
                    // a magnifying glass with a sight in its lens
                    p.Ring(26f, 38f, 18f, 6f);
                    p.Seg(39f, 25f, 58f, 6f, 9f);
                    p.Disc(26f, 38f, 4.5f);
                    p.Seg(26f, 25f, 26f, 30f, 3.5f);
                    p.Seg(26f, 46f, 26f, 51f, 3.5f);
                    p.Seg(13f, 38f, 18f, 38f, 3.5f);
                    p.Seg(34f, 38f, 39f, 38f, 3.5f);
                    break;

                case Glyph.SeekChests:
                    // a small chest with a magnifying glass over its corner
                    p.Rect(4f, 5f, 40f, 22f);
                    p.Rect(4f, 25f, 40f, 36f);
                    p.Rect(19f, 16f, 25f, 30f);
                    p.Ring(44f, 46f, 12f, 5f);
                    p.Seg(53f, 37f, 61f, 28f, 7f);
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

            /// <summary>Removes a disc (an eye socket, a hole).</summary>
            internal void Hole(float cx, float cy, float r)
            {
                Draw((x, y) => Edge(r - (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy))), erase: true);
            }

            /// <summary>A disc squashed into an ellipse.</summary>
            internal void Ellipse(float cx, float cy, float rx, float ry)
            {
                Draw((x, y) =>
                {
                    float u = (x - cx) / rx, v = (y - cy) / ry;
                    float f = (float)Math.Sqrt(u * u + v * v);
                    if (f < 1e-4f) return 1f;
                    float gx = u / rx, gy = v / ry;
                    float grad = (float)Math.Sqrt(gx * gx + gy * gy) / f;
                    return Edge((1f - f) / Math.Max(grad, 1e-4f));
                });
            }

            /// <summary>A ring squashed into an ellipse (a mark lying on the ground).</summary>
            internal void EllipseRing(float cx, float cy, float rx, float ry, float w)
            {
                Draw((x, y) =>
                {
                    float u = (x - cx) / rx, v = (y - cy) / ry;
                    float f = (float)Math.Sqrt(u * u + v * v);
                    if (f < 1e-4f) return 0f;
                    // distance to the outline: the field's value over its gradient
                    float gx = u / rx, gy = v / ry;
                    float grad = (float)Math.Sqrt(gx * gx + gy * gy) / f;
                    return Edge(w * 0.5f - Math.Abs(f - 1f) / Math.Max(grad, 1e-4f));
                });
            }

            /// <summary>
            /// A tube along a curve from <paramref name="ax"/>,<paramref name="ay"/> to <paramref name="bx"/>,<paramref name="by"/>
            /// (bent towards the control point), widening from <paramref name="r0"/> to <paramref name="r1"/>: a horn.
            /// <paramref name="bands"/> (0..1 along it) are thin gaps across the tube, like metal rings.
            /// </summary>
            internal void Horn(float ax, float ay, float qx, float qy, float bx, float by, float r0, float r1, params float[] bands)
            {
                const int Steps = 48;
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    HornAt(ax, ay, qx, qy, bx, by, t, out float x, out float y, out _, out _);
                    Disc(x, y, HornRadius(r0, r1, t));
                }
                foreach (float t in bands)
                {
                    HornAt(ax, ay, qx, qy, bx, by, t, out float x, out float y, out float ux, out float uy);
                    float r = HornRadius(r0, r1, t) + 1.5f, w = 1.2f;
                    float nx = -uy, ny = ux;
                    Erase(new[]
                    {
                        x - ux * w - nx * r, y - uy * w - ny * r, x + ux * w - nx * r, y + uy * w - ny * r,
                        x + ux * w + nx * r, y + uy * w + ny * r, x - ux * w + nx * r, y - uy * w + ny * r
                    }, 1f, 0f, 0f);
                }
            }

            // slow at first, flaring at the end like a bell
            private static float HornRadius(float r0, float r1, float t) => r0 + (r1 - r0) * t * t * t * t;

            private static void HornAt(float ax, float ay, float qx, float qy, float bx, float by, float t,
                out float x, out float y, out float ux, out float uy)
            {
                float s = 1f - t;
                x = s * s * ax + 2f * s * t * qx + t * t * bx;
                y = s * s * ay + 2f * s * t * qy + t * t * by;
                float dx = 2f * s * (qx - ax) + 2f * t * (bx - qx);
                float dy = 2f * s * (qy - ay) + 2f * t * (by - qy);
                float len = Math.Max(1e-4f, (float)Math.Sqrt(dx * dx + dy * dy));
                ux = dx / len;
                uy = dy / len;
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
