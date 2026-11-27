using System;
using System.Collections.Generic;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    // ════════════════════════════════════════════════════════════════════════
    //  TIPOS COMPARTIDOS (los consumen tanto el motor como el dibujo 3D)
    //  Viven en EncofradoCAD.Core.Layout; los consume el motor, el BOM y el preview 3D.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Resultado geométrico completo del encofrado para una losa.</summary>
    public sealed record PreviewLayout(
        List<Point> Puntales,
        List<LinearPiece> HorizontalRails,
        List<LinearPiece> VerticalRails,
        List<LinearPiece> HorizontalSeparators,
        List<LinearPiece> VerticalSeparators,
        List<CrucetaPiece> SmallCrucetas,
        List<CrucetaPiece> LargeCrucetas,
        List<BoardPiece> Boards,
        List<Rect> Remates,
        List<Rect> Tablas,
        List<Rect> BorderTablas,
        Point[]? CentralTowerCorners);

    /// <summary>
    /// Cruceta de arriostre en X entre dos puntales adyacentes (A y B son los pies de los dos puntales
    /// que la cruceta une; el aspa se dibuja en el plano vertical entre ambos).
    /// </summary>
    public sealed record CrucetaPiece(Point A, Point B);

    /// <summary>Pieza lineal (riel o separador) con su longitud nominal de catálogo.</summary>
    public sealed record LinearPiece(double Start, double End, double FixedCoord, bool Horizontal, double NominalLength)
    {
        public double ActualLength => Math.Abs(End - Start);
    }

    /// <summary>Tablero de contacto con su rectángulo en planta y ancho.</summary>
    public sealed record BoardPiece(Rect Rect, double Width, double Length);

    /// <summary>Instantánea inmutable de una columna (metros).</summary>
    public sealed record ColumnSnapshot(double X, double Y, double Width, double Length)
    {
        public double MinX => X - (Width / 2);
        public double MaxX => X + (Width / 2);
        public double MinY => Y - (Length / 2);
        public double MaxY => Y + (Length / 2);

        public bool Contains(Point point, double clearance)
        {
            return point.X >= MinX - clearance
                && point.X <= MaxX + clearance
                && point.Y >= MinY - clearance
                && point.Y <= MaxY + clearance;
        }
    }
}
