using System;
using System.Collections.Generic;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Decide en qué sentido corren los rieles: el del lado MÁS LARGO de la losa (menos material).
    /// Módulo aislado para poder cambiar el criterio sin tocar el resto del motor.
    /// </summary>
    public static class DirectionSolver
    {
        /// <summary>
        /// true = los rieles corren en horizontal (eje X). Usa el BORDE más largo del polígono (su eje
        /// dominante) para acertar con losas inclinadas/paralelogramo; si es ambiguo (casi diagonal),
        /// recurre a la caja envolvente.
        /// </summary>
        public static bool DeterminePrimaryHorizontal(List<Point> slab, double minX, double maxX, double minY, double maxY)
        {
            double bestLength = -1;
            double domDx = 0;
            double domDy = 0;

            for (int i = 0; i < slab.Count; i++)
            {
                Point a = slab[i];
                Point b = slab[(i + 1) % slab.Count];
                double dx = Math.Abs(b.X - a.X);
                double dy = Math.Abs(b.Y - a.Y);
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length > bestLength)
                {
                    bestLength = length;
                    domDx = dx;
                    domDy = dy;
                }
            }

            if (bestLength > LayoutCatalog.GeometryEpsilon && Math.Abs(domDx - domDy) > 0.05)
            {
                return domDx >= domDy;
            }

            return (maxX - minX) >= (maxY - minY);
        }
    }
}
