using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace EncofradoCAD.App.Services
{
    /// <summary>
    /// Convierte una imagen de plano (PNG/JPG/…) en el POLÍGONO de la losa: binariza la imagen, rellena la
    /// región cerrada, toma el contorno exterior de la mancha mayor y lo simplifica a vértices (Douglas–Peucker).
    /// El resultado (en píxeles de la imagen) lo usa la app para crear la losa como si se hubiera dibujado.
    /// </summary>
    public static class PlanImageVectorizer
    {
        private const int MaxDim = 1000;      // resolución de trabajo (lado largo)
        private const double DpEpsFactor = 0.02; // simplificación relativa a la diagonal
        private const int MaxVertices = 60;

        // ── Parámetros de detección de columnas ──────────────────────────────
        private const double ColAreaMinRel = 0.00025; // área mínima de columna respecto al bbox de la losa
        private const double ColAreaMaxRel = 0.06;    // área máxima de columna respecto al bbox de la losa
        private const double ColRellenoMin = 0.55;    // qué tan "llena" debe estar su caja (rechaza texto/líneas)
        private const double ColAspectoMax = 5.0;     // relación de aspecto máxima (rechaza líneas de cota)
        private const int ColMaxCantidad = 300;

        /// <summary>Una columna detectada en el plano, en píxeles de la imagen procesada.</summary>
        public sealed class ColumnaPlano
        {
            public double CentroX { get; set; }
            public double CentroY { get; set; }
            public double Ancho { get; set; }
            public double Largo { get; set; }
        }

        /// <summary>Devuelve el polígono de la losa en píxeles de la imagen procesada.</summary>
        public static bool TryVectorizar(Bitmap source, out List<Point> polygon, out string? error)
        {
            return TryVectorizarPlano(source, out polygon, out _, out error);
        }

        /// <summary>
        /// Convierte una imagen de plano en un MODELO 2D funcional: el polígono de la losa y, si el plano
        /// los incluye, los rectángulos interiores (columnas). Todo en píxeles de la imagen procesada.
        /// El resultado se adapta a lo que traiga la imagen: solo contorno, o contorno + columnas.
        /// </summary>
        public static bool TryVectorizarPlano(Bitmap source, out List<Point> polygon, out List<ColumnaPlano> columnas, out string? error)
        {
            polygon = new List<Point>();
            columnas = new List<ColumnaPlano>();
            error = null;
            try
            {
                if (!Preparar(source, out bool[] fg, out int W, out int H, out int w, out int h, out error))
                {
                    return false;
                }

                // Región SÓLIDA = primer plano ∪ (fondo encerrado). El exterior sale del borde.
                bool[] solid = ConstruirSolido(fg, W, H);

                // Mancha conectada mayor (la losa) + contorno + simplificación.
                bool[] mayor = ComponenteMayor(solid, W, H);
                List<(int x, int y)> contorno = TrazarContorno(mayor, W, H);
                if (contorno.Count < 4)
                {
                    error = "No se pudo detectar el contorno del plano. Usa una imagen más nítida (líneas oscuras sobre fondo claro).";
                    return false;
                }

                double diag = Math.Sqrt((double)(w * w) + (h * h));
                double eps = DpEpsFactor * diag;
                List<(int x, int y)> simp = SimplificarCerrado(contorno, eps);

                // Ajuste: si quedaron demasiados o muy pocos vértices, corrige el epsilon.
                int intentos = 0;
                while (simp.Count > MaxVertices && intentos++ < 6)
                {
                    eps *= 1.6;
                    simp = SimplificarCerrado(contorno, eps);
                }
                intentos = 0;
                while (simp.Count < 3 && intentos++ < 6)
                {
                    eps *= 0.5;
                    simp = SimplificarCerrado(contorno, eps);
                }
                if (simp.Count < 3)
                {
                    error = "No se pudo formar un polígono válido a partir de la imagen.";
                    return false;
                }

                // Coordenadas de imagen (quitando el margen de 1 px).
                foreach ((int x, int y) in simp)
                {
                    polygon.Add(new Point(x - 1, y - 1));
                }

                // Columnas interiores (best-effort): manchas de tinta dentro de la losa.
                columnas = DetectarColumnas(fg, mayor, W, H);
                return true;
            }
            catch (Exception ex)
            {
                error = "Error al procesar la imagen: " + ex.Message;
                return false;
            }
        }

        // ── Preparación común: raster + gris + Otsu + máscara de primer plano ─
        private static bool Preparar(Bitmap source, out bool[] fg, out int W, out int H, out int w, out int h, out string? error)
        {
            fg = Array.Empty<bool>();
            W = H = w = h = 0;
            error = null;

            int sw = source.PixelSize.Width;
            int sh = source.PixelSize.Height;
            if (sw < 8 || sh < 8)
            {
                error = "La imagen es demasiado pequeña.";
                return false;
            }

            // 1) Rasterizar a Bgra8888 a resolución de trabajo.
            double escala = Math.Max(sw, sh) > MaxDim ? (double)MaxDim / Math.Max(sw, sh) : 1.0;
            w = Math.Max(1, (int)Math.Round(sw * escala));
            h = Math.Max(1, (int)Math.Round(sh * escala));

            byte[] bgra = new byte[w * h * 4];
            using (var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96)))
            {
                using (var ctx = rtb.CreateDrawingContext())
                {
                    ctx.DrawImage(source, new Rect(0, 0, w, h));
                }
                GCHandle handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
                try
                {
                    rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), bgra.Length, w * 4);
                }
                finally
                {
                    handle.Free();
                }
            }

            // 2) Escala de grises (el transparente cuenta como fondo claro).
            byte[] gray = new byte[w * h];
            for (int i = 0; i < w * h; i++)
            {
                byte b = bgra[(i * 4) + 0];
                byte g = bgra[(i * 4) + 1];
                byte r = bgra[(i * 4) + 2];
                byte a = bgra[(i * 4) + 3];
                int lum = a < 8 ? 255 : (int)((0.299 * r) + (0.587 * g) + (0.114 * b));
                gray[i] = (byte)Math.Clamp(lum, 0, 255);
            }

            // 3) Umbral de Otsu + polaridad (fondo = esquinas).
            int thr = Otsu(gray);
            double bg = (gray[0] + gray[w - 1] + gray[(h - 1) * w] + gray[(h * w) - 1]) / 4.0;
            bool fgEsOscuro = bg > thr;

            // 4) Máscara de primer plano con MARGEN de 1 px (para losas que tocan el borde).
            W = w + 2;
            H = h + 2;
            fg = new bool[W * H];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool oscuro = gray[(y * w) + x] < thr;
                    fg[((y + 1) * W) + (x + 1)] = fgEsOscuro ? oscuro : !oscuro;
                }
            }
            return true;
        }

        // ── Detección de columnas interiores ─────────────────────────────────
        // Etiqueta las manchas de tinta (fg). La que forma el perímetro de la losa es enorme (se descarta
        // por área). Las manchas pequeñas, compactas y llenas que caen DENTRO de la losa son columnas.
        private static List<ColumnaPlano> DetectarColumnas(bool[] fg, bool[] mayor, int W, int H)
        {
            var resultado = new List<ColumnaPlano>();

            // Caja de la losa (para escalar los umbrales de área).
            int lMinX = W, lMinY = H, lMaxX = -1, lMaxY = -1;
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    if (mayor[(y * W) + x])
                    {
                        if (x < lMinX) lMinX = x;
                        if (x > lMaxX) lMaxX = x;
                        if (y < lMinY) lMinY = y;
                        if (y > lMaxY) lMaxY = y;
                    }
                }
            }
            if (lMaxX < lMinX || lMaxY < lMinY)
            {
                return resultado;
            }
            double losaBBoxArea = (double)(lMaxX - lMinX + 1) * (lMaxY - lMinY + 1);
            double areaMin = Math.Max(16, ColAreaMinRel * losaBBoxArea);
            double areaMax = ColAreaMaxRel * losaBBoxArea;
            int ladoMaxX = (int)((lMaxX - lMinX + 1) * 0.5);
            int ladoMaxY = (int)((lMaxY - lMinY + 1) * 0.5);

            int[] lab = new int[W * H];
            var pila = new Stack<int>();
            int cur = 0;

            for (int s = 0; s < W * H; s++)
            {
                if (!fg[s] || lab[s] != 0)
                {
                    continue;
                }
                cur++;
                lab[s] = cur;
                pila.Push(s);

                int minX = W, minY = H, maxX = -1, maxY = -1, cnt = 0;
                bool tocaBorde = false;

                while (pila.Count > 0)
                {
                    int i = pila.Pop();
                    cnt++;
                    int x = i % W, y = i / W;
                    if (x == 0 || y == 0 || x == W - 1 || y == H - 1)
                    {
                        tocaBorde = true;
                    }
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= W || ny < 0 || ny >= H)
                            {
                                continue;
                            }
                            int j = (ny * W) + nx;
                            if (fg[j] && lab[j] == 0)
                            {
                                lab[j] = cur;
                                pila.Push(j);
                            }
                        }
                    }
                }

                if (tocaBorde)
                {
                    continue; // tinta que llega al borde de la imagen = no es columna
                }

                int bw = maxX - minX + 1;
                int bh = maxY - minY + 1;
                double bboxArea = (double)bw * bh;
                if (bboxArea < areaMin || bboxArea > areaMax)
                {
                    continue;
                }
                if (bw < 3 || bh < 3 || bw > ladoMaxX || bh > ladoMaxY)
                {
                    continue;
                }
                double aspecto = (double)Math.Max(bw, bh) / Math.Min(bw, bh);
                if (aspecto > ColAspectoMax)
                {
                    continue;
                }

                // Centro dentro de la losa.
                int cx = (minX + maxX) / 2;
                int cy = (minY + maxY) / 2;
                if (!mayor[(cy * W) + cx])
                {
                    continue;
                }

                // Relleno sólido de su caja (rellena huecos internos): distingue columnas de texto/líneas.
                double relleno = RellenoSolido(lab, cur, W, minX, minY, bw, bh);
                if (relleno < ColRellenoMin)
                {
                    continue;
                }

                resultado.Add(new ColumnaPlano
                {
                    CentroX = cx - 1, // quitar margen de 1 px
                    CentroY = cy - 1,
                    Ancho = bw,
                    Largo = bh
                });

                if (resultado.Count >= ColMaxCantidad)
                {
                    break;
                }
            }

            return resultado;
        }

        // Fracción de la caja del componente que queda "sólida" (tinta + huecos encerrados).
        // Una columna llena o un cuadro hueco cerrado dan ~1; texto, tramas o líneas dan valores bajos.
        private static double RellenoSolido(int[] lab, int etiqueta, int W, int minX, int minY, int bw, int bh)
        {
            bool[] comp = new bool[bw * bh];
            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++)
                {
                    comp[(y * bw) + x] = lab[((minY + y) * W) + (minX + x)] == etiqueta;
                }
            }

            // Flood fill del exterior desde el borde de la caja sobre celdas no-componente.
            bool[] ext = new bool[bw * bh];
            var pila = new Stack<int>();
            void Sembrar(int idx)
            {
                if (!comp[idx] && !ext[idx])
                {
                    ext[idx] = true;
                    pila.Push(idx);
                }
            }
            for (int x = 0; x < bw; x++)
            {
                Sembrar(x);
                Sembrar(((bh - 1) * bw) + x);
            }
            for (int y = 0; y < bh; y++)
            {
                Sembrar(y * bw);
                Sembrar((y * bw) + (bw - 1));
            }
            while (pila.Count > 0)
            {
                int i = pila.Pop();
                int x = i % bw, y = i / bw;
                if (x > 0) Sembrar(i - 1);
                if (x < bw - 1) Sembrar(i + 1);
                if (y > 0) Sembrar(i - bw);
                if (y < bh - 1) Sembrar(i + bw);
            }

            int solidos = 0;
            for (int i = 0; i < bw * bh; i++)
            {
                if (comp[i] || !ext[i])
                {
                    solidos++;
                }
            }
            return (double)solidos / (bw * bh);
        }

        // ── Otsu ─────────────────────────────────────────────────────────────
        private static int Otsu(byte[] gray)
        {
            var hist = new int[256];
            foreach (byte v in gray)
            {
                hist[v]++;
            }

            int total = gray.Length;
            double sum = 0;
            for (int t = 0; t < 256; t++)
            {
                sum += t * (double)hist[t];
            }

            double sumB = 0;
            int wB = 0;
            double maxVar = -1;
            int umbral = 127;
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t];
                if (wB == 0)
                {
                    continue;
                }
                int wF = total - wB;
                if (wF == 0)
                {
                    break;
                }
                sumB += t * (double)hist[t];
                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double var = (double)wB * wF * (mB - mF) * (mB - mF);
                if (var > maxVar)
                {
                    maxVar = var;
                    umbral = t;
                }
            }
            return umbral;
        }

        // ── Relleno de región sólida (exterior alcanzable desde el borde) ────
        private static bool[] ConstruirSolido(bool[] fg, int w, int h)
        {
            bool[] ext = new bool[w * h];
            var pila = new Stack<int>();

            void Sembrar(int i)
            {
                if (!fg[i] && !ext[i])
                {
                    ext[i] = true;
                    pila.Push(i);
                }
            }

            for (int x = 0; x < w; x++)
            {
                Sembrar(x);
                Sembrar(((h - 1) * w) + x);
            }
            for (int y = 0; y < h; y++)
            {
                Sembrar(y * w);
                Sembrar((y * w) + (w - 1));
            }

            while (pila.Count > 0)
            {
                int i = pila.Pop();
                int x = i % w, y = i / w;
                if (x > 0) Sembrar(i - 1);
                if (x < w - 1) Sembrar(i + 1);
                if (y > 0) Sembrar(i - w);
                if (y < h - 1) Sembrar(i + w);
            }

            bool[] solid = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                solid[i] = fg[i] || !ext[i];
            }
            return solid;
        }

        // ── Componente conectado mayor (8-conexo) ────────────────────────────
        private static bool[] ComponenteMayor(bool[] solid, int w, int h)
        {
            int[] lab = new int[w * h];
            int cur = 0, best = 0, bestLab = 0;
            var pila = new Stack<int>();

            for (int s = 0; s < w * h; s++)
            {
                if (!solid[s] || lab[s] != 0)
                {
                    continue;
                }
                cur++;
                int cnt = 0;
                lab[s] = cur;
                pila.Push(s);
                while (pila.Count > 0)
                {
                    int i = pila.Pop();
                    cnt++;
                    int x = i % w, y = i / w;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                            {
                                continue;
                            }
                            int j = (ny * w) + nx;
                            if (solid[j] && lab[j] == 0)
                            {
                                lab[j] = cur;
                                pila.Push(j);
                            }
                        }
                    }
                }
                if (cnt > best)
                {
                    best = cnt;
                    bestLab = cur;
                }
            }

            bool[] mask = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                mask[i] = lab[i] == bestLab;
            }
            return mask;
        }

        // ── Trazado de contorno Moore (horario, 8-conexo) ────────────────────
        private static readonly int[] Ox = { 0, 1, 1, 1, 0, -1, -1, -1 }; // N,NE,E,SE,S,SW,W,NW
        private static readonly int[] Oy = { -1, -1, 0, 1, 1, 1, 0, -1 };

        private static List<(int x, int y)> TrazarContorno(bool[] mask, int w, int h)
        {
            var contorno = new List<(int, int)>();
            int sx = -1, sy = -1;
            for (int y = 0; y < h && sy < 0; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (mask[(y * w) + x])
                    {
                        sx = x;
                        sy = y;
                        break;
                    }
                }
            }
            if (sx < 0)
            {
                return contorno;
            }

            bool Fg(int x, int y) => x >= 0 && x < w && y >= 0 && y < h && mask[(y * w) + x];

            int cx = sx, cy = sy, b = 6; // se entra desde el Oeste
            contorno.Add((cx, cy));
            int maxIt = w * h * 8, it = 0;
            while (it++ < maxIt)
            {
                int found = -1;
                for (int k = 0; k < 8; k++)
                {
                    int d = (b + 1 + k) % 8;
                    if (Fg(cx + Ox[d], cy + Oy[d]))
                    {
                        found = d;
                        break;
                    }
                }
                if (found < 0)
                {
                    break; // pixel aislado
                }
                int nx = cx + Ox[found], ny = cy + Oy[found];
                b = (found + 4) % 8;
                cx = nx;
                cy = ny;
                if (cx == sx && cy == sy)
                {
                    break;
                }
                contorno.Add((cx, cy));
            }
            return contorno;
        }

        // ── Douglas–Peucker sobre contorno cerrado ───────────────────────────
        private static List<(int x, int y)> SimplificarCerrado(List<(int x, int y)> c, double eps)
        {
            if (c.Count < 4)
            {
                return new List<(int, int)>(c);
            }

            int far = 0;
            double dmax = -1;
            for (int i = 1; i < c.Count; i++)
            {
                double d = Dist2(c[i], c[0]);
                if (d > dmax)
                {
                    dmax = d;
                    far = i;
                }
            }

            var a = c.GetRange(0, far + 1);
            var bParte = new List<(int, int)>(c.GetRange(far, c.Count - far)) { c[0] };

            List<(int, int)> ra = Dp(a, eps);
            List<(int, int)> rb = Dp(bParte, eps);

            var res = new List<(int, int)>();
            for (int i = 0; i < ra.Count - 1; i++)
            {
                res.Add(ra[i]);
            }
            for (int i = 0; i < rb.Count - 1; i++)
            {
                res.Add(rb[i]);
            }
            return res;
        }

        private static List<(int x, int y)> Dp(List<(int x, int y)> pts, double eps)
        {
            int n = pts.Count;
            if (n < 3)
            {
                return new List<(int, int)>(pts);
            }

            bool[] keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            var pila = new Stack<(int i, int j)>();
            pila.Push((0, n - 1));
            while (pila.Count > 0)
            {
                (int i, int j) = pila.Pop();
                if (j <= i + 1)
                {
                    continue;
                }
                double dmax = 0;
                int k = -1;
                for (int m = i + 1; m < j; m++)
                {
                    double d = DistPuntoSegmento(pts[m], pts[i], pts[j]);
                    if (d > dmax)
                    {
                        dmax = d;
                        k = m;
                    }
                }
                if (dmax > eps && k > 0)
                {
                    keep[k] = true;
                    pila.Push((i, k));
                    pila.Push((k, j));
                }
            }

            var res = new List<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                {
                    res.Add(pts[i]);
                }
            }
            return res;
        }

        private static double Dist2((int x, int y) a, (int x, int y) b)
        {
            double dx = a.x - b.x, dy = a.y - b.y;
            return (dx * dx) + (dy * dy);
        }

        private static double DistPuntoSegmento((int x, int y) p, (int x, int y) a, (int x, int y) b)
        {
            double ax = a.x, ay = a.y, bx = b.x, by = b.y, px = p.x, py = p.y;
            double dx = bx - ax, dy = by - ay;
            if (dx == 0 && dy == 0)
            {
                return Math.Sqrt(((px - ax) * (px - ax)) + ((py - ay) * (py - ay)));
            }
            double t = (((px - ax) * dx) + ((py - ay) * dy)) / ((dx * dx) + (dy * dy));
            double cx = ax + (t * dx), cy = ay + (t * dy);
            return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
        }
    }
}
