using EncofradoCAD.Core.Enums;

namespace EncofradoCAD.Core.Models
{
    /// <summary>
    /// Cabecera del proyecto. La GEOMETRÍA no vive aquí: la losa y las columnas las lleva el lienzo 2D
    /// (DrawingCanvas) y las piezas las resuelve el motor (PreviewLayout), así que este modelo solo
    /// guarda lo que la interfaz muestra del proyecto en sí.
    ///
    /// Tuvo además colecciones de Tableros / Rieles / Puntales / Crucetas / Separadores / Remates y una
    /// losa poligonal propia, de la etapa anterior del programa: nada las llenaba ni las leía, y se
    /// retiraron junto con las clases de ese modelo (Tablero, Riel, Puntal, Columna, LosaPolygon, Vano…).
    /// </summary>
    public class Proyecto
    {
        /// <summary>Nombre del proyecto; es lo que se ve en la cabecera del módulo Proyecto.</summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Sentido de los rieles principales que resultó del último cálculo.</summary>
        public DireccionBase DireccionOptima { get; set; }
    }
}
