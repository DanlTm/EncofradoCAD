using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EncofradoCAD.App.Services
{
    /// <summary>
    /// Generador MÍNIMO de PDF (una fuente estándar Helvetica, texto plano paginado) SIN dependencias
    /// externas. Suficiente para exportar la lista de materiales. Escribe el archivo en Latin-1 (WinAnsi),
    /// así los acentos se ven correctamente y cada carácter equivale a un byte (offsets exactos del xref).
    /// </summary>
    public static class MiniPdf
    {
        private const double PageW = 595;   // A4 en puntos (72 dpi)
        private const double PageH = 842;
        private const double Margin = 50;
        private const double LineHeight = 16;
        private const double FontSize = 11;
        private const double TitleSize = 16;

        public static void CreateBom(string titulo, IReadOnlyList<(string Cantidad, string Nombre)> filas, string path)
        {
            var lines = new List<(string Text, double Size)>
            {
                (titulo, TitleSize),
                ($"Generado: {DateTime.Now:yyyy-MM-dd HH:mm}", 9),
                ("", FontSize),
                ("Cantidad    Material", FontSize),
                ("------------------------------------------------------------", FontSize),
            };
            foreach ((string Cantidad, string Nombre) f in filas)
            {
                lines.Add(($"{f.Cantidad,-10}  {f.Nombre}", FontSize));
            }

            int maxLines = Math.Max(1, (int)((PageH - (2 * Margin)) / LineHeight));
            var pages = new List<List<(string Text, double Size)>>();
            for (int i = 0; i < lines.Count; i += maxLines)
            {
                pages.Add(lines.GetRange(i, Math.Min(maxLines, lines.Count - i)));
            }
            if (pages.Count == 0)
            {
                pages.Add(new List<(string, double)>());
            }

            const int fontId = 3;
            int n = 3 + (2 * pages.Count);
            var bodies = new Dictionary<int, string>();

            // Kids
            var kids = new StringBuilder();
            for (int p = 0; p < pages.Count; p++)
            {
                int pageId = 5 + (2 * p);
                kids.Append(pageId).Append(" 0 R ");
            }

            bodies[1] = "<< /Type /Catalog /Pages 2 0 R >>";
            bodies[2] = $"<< /Type /Pages /Kids [ {kids.ToString().Trim()} ] /Count {pages.Count} >>";
            bodies[fontId] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

            for (int p = 0; p < pages.Count; p++)
            {
                int contentId = 4 + (2 * p);
                int pageId = 5 + (2 * p);

                var cs = new StringBuilder();
                cs.Append("BT\n");
                double y = PageH - Margin;
                foreach ((string Text, double Size) line in pages[p])
                {
                    cs.Append(FormattableString.Invariant($"/F1 {line.Size:0.##} Tf\n"));
                    cs.Append(FormattableString.Invariant($"1 0 0 1 {Margin:0.##} {y:0.##} Tm\n"));
                    cs.Append('(').Append(Escape(line.Text)).Append(") Tj\n");
                    y -= LineHeight;
                }
                cs.Append("ET");
                string content = cs.ToString();

                bodies[contentId] = $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";
                bodies[pageId] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW:0} {PageH:0}] " +
                                 $"/Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>";
            }

            var sb = new StringBuilder();
            sb.Append("%PDF-1.4\n");
            var offsets = new int[n + 1];
            for (int i = 1; i <= n; i++)
            {
                offsets[i] = sb.Length;
                sb.Append(i).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
            }

            int xrefStart = sb.Length;
            sb.Append("xref\n0 ").Append(n + 1).Append('\n');
            sb.Append("0000000000 65535 f \n");
            for (int i = 1; i <= n; i++)
            {
                sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }
            sb.Append("trailer\n<< /Size ").Append(n + 1).Append(" /Root 1 0 R >>\nstartxref\n")
              .Append(xrefStart).Append("\n%%EOF");

            File.WriteAllText(path, sb.ToString(), Encoding.Latin1);
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '\\' || c == '(' || c == ')')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c > 0xFF)
                {
                    sb.Append('?'); // fuera de Latin-1
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
