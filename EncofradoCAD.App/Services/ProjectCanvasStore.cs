using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Services
{
    /// <summary>
    /// Guarda y abre el proyecto (losa + columnas) en un archivo JSON propio (.encproj). Reemplaza a la
    /// persistencia SQLite de la v1. Todo se almacena en METROS (independiente del zoom/píxeles) para que
    /// el archivo sea portable. Conserva el tipo de cada tramo (recto / media luna / ovalado).
    /// </summary>
    public static class ProjectCanvasStore
    {
        public const string Extension = ".encproj";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public sealed class CanvasProjectDto
        {
            public string Nombre { get; set; } = "Proyecto Losa";
            public List<VertexDto> Losa { get; set; } = new();
            public List<ColumnaDto> Columnas { get; set; } = new();
        }

        public sealed class VertexDto
        {
            public double X { get; set; }
            public double Y { get; set; }
            /// <summary>Tipo del tramo que TERMINA en este vértice. En el primer vértice no aplica.</summary>
            public TipoSegmento Tipo { get; set; } = TipoSegmento.Recto;
        }

        public sealed class ColumnaDto
        {
            public string Nombre { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Ancho { get; set; }
            public double Largo { get; set; }
        }

        /// <summary>Toma una instantánea (en metros) del estado actual del lienzo.</summary>
        public static CanvasProjectDto Capture(DrawingCanvas canvas, string nombre = "Proyecto Losa")
        {
            var dto = new CanvasProjectDto { Nombre = nombre };
            double ppm = canvas.PixelesPorMetro;
            PolygonManager pm = canvas.PolygonManager;

            if (pm.PuntoInicial is Point p0)
            {
                dto.Losa.Add(new VertexDto { X = p0.X / ppm, Y = p0.Y / ppm, Tipo = TipoSegmento.Recto });
                foreach (TramoLosa t in pm.Tramos)
                {
                    dto.Losa.Add(new VertexDto { X = t.PuntoFinal.X / ppm, Y = t.PuntoFinal.Y / ppm, Tipo = t.Tipo });
                }
            }

            foreach (EstadoColumnaUI estado in canvas.ColumnManager.EstadosColumnas)
            {
                DatosColumna d = estado.Datos;
                dto.Columnas.Add(new ColumnaDto { Nombre = d.Nombre, X = d.PosX, Y = d.PosY, Ancho = d.Ancho, Largo = d.Largo });
            }

            return dto;
        }

        /// <summary>Reconstruye el lienzo a partir de una instantánea (limpia lo anterior).</summary>
        public static void Apply(CanvasProjectDto dto, DrawingCanvas canvas)
        {
            canvas.Limpiar();

            double ppm = canvas.PixelesPorMetro;
            PolygonManager pm = canvas.PolygonManager;

            if (dto.Losa.Count > 0)
            {
                pm.PuntoInicial = new Point(dto.Losa[0].X * ppm, dto.Losa[0].Y * ppm);
                for (int i = 1; i < dto.Losa.Count; i++)
                {
                    VertexDto v = dto.Losa[i];
                    pm.Tramos.Add(new TramoLosa { PuntoFinal = new Point(v.X * ppm, v.Y * ppm), Tipo = v.Tipo });
                }
                if (dto.Losa.Count >= 3)
                {
                    pm.IsDrawingMode = false;
                }
                pm.ActualizarLosaVisual();
            }

            var losaPath = pm.LosaPath;
            foreach (ColumnaDto c in dto.Columnas)
            {
                var datos = new DatosColumna { Nombre = c.Nombre, PosX = c.X, PosY = c.Y, Ancho = c.Ancho, Largo = c.Largo };
                canvas.ColumnManager.DibujarColumna(datos, losaPath, pm.PuntoInicial, pm.Tramos);
            }
        }

        public static void Save(CanvasProjectDto dto, string path)
            => File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));

        public static CanvasProjectDto Load(string path)
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CanvasProjectDto>(json, Options)
                   ?? throw new InvalidDataException($"El archivo '{path}' está vacío o es inválido.");
        }
    }
}
