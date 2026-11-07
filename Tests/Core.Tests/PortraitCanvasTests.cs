using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T352. Owner 2026-11-07: "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's
    /// what the assets are." Every portrait is a whole 1024x1024 RGBA canvas (never alpha-trimmed or shrunk), every
    /// class's Thumbnail frames that canvas, and its Figure is the figure's real box on it (whole-figure views draw it).
    /// </summary>
    public class PortraitCanvasTests
    {
        private const int Canvas = 1024;
        private static string PortraitDir => Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits");

        [Test]
        public void Every_portrait_is_a_1024_square_RGBA_canvas_with_a_transparent_background()
        {
            var files = Directory.GetFiles(PortraitDir, "*.png");
            Assert.That(files.Length, Is.GreaterThan(100));
            foreach (var f in files)
            {
                var png = Png.Read(f);
                string n = Path.GetFileName(f);
                Assert.That((png.Width, png.Height), Is.EqualTo((Canvas, Canvas)), $"{n} is {png.Width}x{png.Height}, not {Canvas}x{Canvas}");
                Assert.That(png.ColorType, Is.EqualTo(6), $"{n} is not 8-bit RGBA (colour type {png.ColorType})");
                // Transparent background: a good part of the canvas is see-through (some originals carry a faint
                // vignette or alpha noise at the edges, so this measures the canvas, not two corner pixels).
                Assert.That(png.ClearFraction(), Is.GreaterThan(0.2), $"{n}: hardly any of the canvas is transparent (a solid background left in?)");
            }
        }

        [Test]
        public void Every_class_frames_its_portrait_and_knows_its_figure_box()
        {
            foreach (var c in Real.Classes.Values)
            {
                var th = c.Thumbnail;
                Assert.That(th, Is.Not.Null, $"{c.Id}: no Thumbnail");
                Assert.That(th.Zoom, Is.InRange(Thumbnail.MinZoom, Thumbnail.MaxZoom), $"{c.Id}: Thumbnail Zoom");
                Assert.That(th.X, Is.InRange(Thumbnail.MinOffset, Thumbnail.MaxOffset), $"{c.Id}: Thumbnail X");
                Assert.That(th.Y, Is.InRange(Thumbnail.MinOffset, Thumbnail.MaxOffset), $"{c.Id}: Thumbnail Y");
                Assert.That(c.Figure, Is.Not.Null, $"{c.Id}: no Figure");
                Assert.That(c.Figure.IsValid, Is.True, $"{c.Id}: Figure is not a box inside the portrait");
                // The thumbnail's centre lands on the figure (its own box, give or take a margin), not on empty canvas.
                var f = c.Figure;
                Assert.That(th.X, Is.InRange(f.X - 0.1f, f.X + f.W + 0.1f), $"{c.Id}: the Thumbnail centre X {th.X} is off the figure");
                Assert.That(th.Y, Is.InRange(f.Y - 0.1f, f.Y + f.H + 0.1f), $"{c.Id}: the Thumbnail centre Y {th.Y} is off the figure");
            }
            Assert.That(ContentLoader.Validate(Real).Where(p => p.Contains("Figure") || p.Contains("Thumbnail")), Is.Empty);
        }

        [Test]
        public void Every_class_figure_is_the_alpha_bounds_of_its_portrait()
        {
            foreach (var c in Real.Classes.Values)
            {
                var path = Path.Combine(PortraitDir, c.Id + ".png");
                if (!File.Exists(path)) continue; // Every_class_can_be_instantiated_and_has_art_reference reports it
                var (x0, y0, x1, y1) = Png.Read(path).AlphaBounds();
                var f = c.Figure;
                const float px = 1f / Canvas; // one canvas pixel, plus the 4-decimal rounding of the JSON
                Assert.That(f.X, Is.EqualTo(x0 / (float)Canvas).Within(px), $"{c.Id}: Figure X");
                Assert.That(f.Y, Is.EqualTo(y0 / (float)Canvas).Within(px), $"{c.Id}: Figure Y");
                Assert.That(f.W, Is.EqualTo((x1 - x0) / (float)Canvas).Within(px), $"{c.Id}: Figure W");
                Assert.That(f.H, Is.EqualTo((y1 - y0) / (float)Canvas).Within(px), $"{c.Id}: Figure H");
            }
        }

        /// <summary>Just enough of a PNG reader for 8-bit, non-interlaced images (what tools/import_art.py writes).</summary>
        private sealed class Png
        {
            public int Width, Height, ColorType, Channels;
            private byte[] pixels;

            public byte Alpha(int x, int y) => Channels == 4 ? pixels[(y * Width + x) * 4 + 3] : (byte)255;

            /// <summary>Share of the pixels that are (nearly) fully transparent, alpha below 16.</summary>
            public double ClearFraction()
            {
                if (Channels != 4) return 0;
                int n = 0;
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] < 16) n++;
                return n / (double)(Width * Height);
            }

            /// <summary>(x0, y0, x1, y1) of the pixels with any alpha, exclusive end (PIL getbbox).</summary>
            public (int, int, int, int) AlphaBounds()
            {
                int x0 = Width, y0 = Height, x1 = 0, y1 = 0;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (Alpha(x, y) == 0) continue;
                    if (x < x0) x0 = x;
                    if (x >= x1) x1 = x + 1;
                    if (y < y0) y0 = y;
                    if (y >= y1) y1 = y + 1;
                }
                return x1 == 0 ? (0, 0, 0, 0) : (x0, y0, x1, y1);
            }

            public static Png Read(string path)
            {
                var b = File.ReadAllBytes(path);
                var p = new Png();
                int bitDepth = 0, interlace = 0;
                var idat = new MemoryStream();
                for (int i = 8; i + 8 <= b.Length;)
                {
                    int len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
                    string type = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);
                    int d = i + 8;
                    if (type == "IHDR")
                    {
                        p.Width = (b[d] << 24) | (b[d + 1] << 16) | (b[d + 2] << 8) | b[d + 3];
                        p.Height = (b[d + 4] << 24) | (b[d + 5] << 16) | (b[d + 6] << 8) | b[d + 7];
                        bitDepth = b[d + 8]; p.ColorType = b[d + 9]; interlace = b[d + 12];
                    }
                    else if (type == "IDAT") idat.Write(b, d, len);
                    else if (type == "IEND") break;
                    i = d + len + 4;
                }
                p.Channels = p.ColorType switch { 6 => 4, 2 => 3, 4 => 2, _ => 1 };
                if (bitDepth != 8 || interlace != 0 || p.ColorType != 6) return p; // the size/type asserts say why
                idat.Position = 0;
                int stride = p.Width * 4;
                var raw = new byte[(stride + 1) * p.Height];
                using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.ReadExactly(raw);
                p.pixels = new byte[stride * p.Height];
                var px = p.pixels;
                for (int y = 0; y < p.Height; y++)
                {
                    int filter = raw[y * (stride + 1)], src = y * (stride + 1) + 1, dst = y * stride;
                    for (int x = 0; x < stride; x++)
                    {
                        int a = x >= 4 ? px[dst + x - 4] : 0, up = y > 0 ? px[dst - stride + x] : 0;
                        int c = x >= 4 && y > 0 ? px[dst - stride + x - 4] : 0, v = raw[src + x];
                        px[dst + x] = (byte)(filter switch
                        {
                            1 => v + a,
                            2 => v + up,
                            3 => v + ((a + up) >> 1),
                            4 => v + Paeth(a, up, c),
                            _ => v,
                        });
                    }
                }
                return p;
            }

            private static int Paeth(int a, int b, int c)
            {
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
            }
        }
    }
}
