using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EncofradoCAD.App.Services
{
    /// <summary>Exporta la lista de materiales (BOM) a CSV (abre en Excel) y a PDF (vía <see cref="MiniPdf"/>).</summary>
    public static class BomExporter
    {
        /// <summary>CSV con BOM UTF-8 para que Excel muestre bien los acentos. Separador ';' (locales ES).</summary>
        public static void ExportCsv(string titulo, IReadOnlyList<(string Cantidad, string Nombre)> filas, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\"{titulo.Replace("\"", "\"\"")}\"");
            sb.AppendLine("Cantidad;Material");
            foreach ((string Cantidad, string Nombre) f in filas)
            {
                sb.AppendLine($"\"{f.Cantidad.Replace("\"", "\"\"")}\";\"{f.Nombre.Replace("\"", "\"\"")}\"");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static void ExportPdf(string titulo, IReadOnlyList<(string Cantidad, string Nombre)> filas, string path)
            => MiniPdf.CreateBom(titulo, filas, path);
    }
}
