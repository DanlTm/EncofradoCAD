using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.App.Controls;
using EncofradoCAD.Core.Enums;
using EncofradoCAD.Core.Layout;
using CoreGeom = EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.App.Services
{
    /// <summary>
    /// Puente entre el lienzo 2D (Avalonia) y el motor de cálculo (Core). Extrae el polígono de la losa
    /// y las columnas dibujadas, los convierte a la geometría neutral de Core y delega en LayoutEngine.
    /// Sustituye a los métodos que en la v1 leían el PathGeometry de WPF.
    /// </summary>
    public static class LayoutEngineAdapter
    {
        /// <summary>
        /// Polígono de la losa (en metros). Los lados curvos (media luna / ovalado) se teselan en puntos que
        /// siguen el arco real, de modo que el 3D y el BOM reflejan el contorno curvo y no su cuerda.
        /// </summary>
        public static List<CoreGeom.Point> GetSlabPolygon(DrawingCanvas canvas)
        {
            double ppm = canvas.PixelesPorMetro;
            return canvas.PolygonManager.ObtenerVerticesFlatten()
                .Select(p => new CoreGeom.Point(p.X / ppm, p.Y / ppm))
                .ToList();
        }

        /// <summary>Columnas colocadas en el lienzo (en metros), ordenadas.</summary>
        public static List<ColumnSnapshot> GetColumns(DrawingCanvas canvas)
        {
            return canvas.ColumnManager.EstadosColumnas
                .Select(state => new ColumnSnapshot(state.Datos.PosX, state.Datos.PosY, state.Datos.Ancho, state.Datos.Largo))
                .OrderBy(column => column.X)
                .ThenBy(column => column.Y)
                .ToList();
        }

        /// <summary>
        /// Extrae SOLO el plano (polígono de la losa, columnas y su caja envolvente) sin ejecutar el motor
        /// de cálculo. Lo usa el ARMADO MANUAL del módulo 3D, que dibuja el contorno y el voladizo pero
        /// construye la estructura pieza por pieza en vez de resolverla automáticamente.
        /// </summary>
        public static bool TryGetPlano(
            DrawingCanvas canvas,
            out List<CoreGeom.Point> slab,
            out List<ColumnSnapshot> columns,
            out double minX, out double maxX, out double minY, out double maxY,
            out string? mensajeError)
        {
            columns = new List<ColumnSnapshot>();
            minX = maxX = minY = maxY = 0;
            mensajeError = null;

            slab = GetSlabPolygon(canvas);
            if (slab.Count < 3)
            {
                mensajeError = "Trace y cierre el polígono de la losa antes de armar en 3D.";
                return false;
            }

            columns = GetColumns(canvas);
            minX = slab.Min(p => p.X);
            maxX = slab.Max(p => p.X);
            minY = slab.Min(p => p.Y);
            maxY = slab.Max(p => p.Y);
            return true;
        }

        public static bool TryBuildLayout(
            DrawingCanvas canvas,
            out PreviewLayout? layout,
            out List<CoreGeom.Point> slab,
            out List<ColumnSnapshot> columns,
            out double minX, out double maxX, out double minY, out double maxY,
            out string? mensajeError)
        {
            layout = null;
            columns = new List<ColumnSnapshot>();
            minX = maxX = minY = maxY = 0;
            mensajeError = null;

            slab = GetSlabPolygon(canvas);
            if (slab.Count < 3)
            {
                mensajeError = "Trace y cierre el polígono de la losa antes de calcular materiales.";
                return false;
            }

            columns = GetColumns(canvas);

            minX = slab.Min(p => p.X);
            maxX = slab.Max(p => p.X);
            minY = slab.Min(p => p.Y);
            maxY = slab.Max(p => p.Y);

            layout = LayoutEngine.BuildLayout(slab, columns, minX, maxX, minY, maxY);
            return true;
        }

        public static bool TryComputeMaterialBill(
            DrawingCanvas canvas,
            out List<(string Nombre, string Cantidad)> materiales,
            out DireccionBase direccionOptima,
            out string? mensajeError)
        {
            materiales = new List<(string Nombre, string Cantidad)>();
            direccionOptima = DireccionBase.Horizontal;

            if (!TryBuildLayout(canvas, out PreviewLayout? layout, out List<CoreGeom.Point> slab,
                    out List<ColumnSnapshot> columns,
                    out double minX, out double maxX, out double minY, out double maxY, out mensajeError)
                || layout == null)
            {
                return false;
            }

            direccionOptima = LayoutEngine.DetermineOptimalDirection(slab, minX, maxX, minY, maxY);
            materiales = LayoutEngine.BuildMaterialBill(layout, slab, columns);
            return true;
        }
    }
}
