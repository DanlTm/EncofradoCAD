using System.Collections.Generic;
using Avalonia;

namespace EncofradoCAD.App.Controls
{
    public class PolygonGeometryService
    {
        private readonly PolygonManager _polygonManager;

        public PolygonGeometryService(PolygonManager polygonManager)
        {
            _polygonManager = polygonManager;
        }

        public void AplicarEscalaGlobal(double factor)
        {
            if (_polygonManager.PuntoInicial.HasValue)
            {
                _polygonManager.PuntoInicial = new Point(_polygonManager.PuntoInicial.Value.X * factor, _polygonManager.PuntoInicial.Value.Y * factor);
            }
            
            foreach (var t in _polygonManager.Tramos)
            {
                t.PuntoFinal = new Point(t.PuntoFinal.X * factor, t.PuntoFinal.Y * factor);
            }
        }

        public void AplicarDeformacion(int idx, TipoSegmento tipo, double nuevaMedidaPx)
        {
            List<Point> V = new List<Point>();
            V.Add(_polygonManager.PuntoInicial.Value);
            foreach (var t in _polygonManager.Tramos) V.Add(t.PuntoFinal);
            
            int Count = V.Count;
            Point p1 = V[idx];
            Point p2 = V[(idx + 1) % Count];

            double distActualPx = (tipo == TipoSegmento.Recto) ? GeometryHelper.Distancia(p1, p2) : GeometryHelper.CalcularLargoArco(p1, p2, tipo);
            
            if (distActualPx < 0.001) return;

            Vector originalVector = p2 - p1;
            double factor = nuevaMedidaPx / distActualPx;
            Vector newVector = originalVector * factor;

            V[(idx + 1) % Count] = new Point(p1.X + newVector.X, p1.Y + newVector.Y);
            
            _polygonManager.PuntoInicial = V[0];
            for (int i = 0; i < _polygonManager.Tramos.Count; i++)
            {
                _polygonManager.Tramos[i].PuntoFinal = V[i + 1];
            }
        }
    }
}
