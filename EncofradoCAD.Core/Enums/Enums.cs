namespace EncofradoCAD.Core.Enums
{
    /// <summary>
    /// Sentido en el que corren los rieles principales de 3 m. Lo decide el motor (DirectionSolver) o,
    /// en armado manual, la elección del usuario; el módulo de cálculo lo muestra en su resumen.
    /// </summary>
    public enum DireccionBase
    {
        /// <summary>Retícula alineada con el eje X.</summary>
        Horizontal,

        /// <summary>Retícula alineada con el eje Y.</summary>
        Vertical
    }
}
