# EncofradoCAD

Aplicacion de escritorio para dibujar losas y columnas, visualizar un sistema de encofrado y calcular una lista de materiales.

## Tecnologias

- **C# y .NET 10** para la aplicacion, el motor de calculo y las pruebas.
- **Avalonia UI 12.1** y **XAML** para la interfaz de escritorio.
- **CommunityToolkit.Mvvm 8.4** para el patron MVVM y los comandos de la interfaz.
- **MathNet.Numerics 5.0** como biblioteca numerica del nucleo.
- **System.Text.Json** para guardar proyectos en formato JSON `.encproj`.
- **xUnit** para las pruebas automatizadas.

## Funcionalidades

- Dibujo y edicion de losas en 2D, con segmentos rectos y curvos.
- Agregar columnas con medidas predeterminadas o personalizadas.
- Cargar una imagen de un plano como referencia o convertirla en un modelo 2D editable.
- Generar y visualizar en 3D el armado automatico del encofrado.
- Armar manualmente la estructura y colocar tableros del catalogo.
- Calcular el area, la direccion de armado y la lista de materiales.
- Exportar resultados a PDF, CSV y PNG.
- Guardar y abrir el contorno de la losa y las columnas mediante archivos `.encproj`.

## Como funciona

La aplicacion esta separada en tres proyectos:

- **EncofradoCAD.App** contiene la interfaz Avalonia, el lienzo 2D, la vista 3D, los modulos de proyecto y calculo, y la adaptacion entre la interfaz y el motor.
- **EncofradoCAD.Core** contiene la geometria y el motor de calculo, independiente de la interfaz. A partir de la losa y las columnas, determina la direccion de armado y genera los elementos del encofrado, como apoyos, rieles, separadores, crucetas y tableros.
- **EncofradoCAD.Tests** contiene las pruebas automatizadas del nucleo y la logica de armado.

El flujo habitual es dibujar o importar la losa, agregar las columnas, revisar el armado automatico en 3D y calcular la lista de materiales. La vista 3D y el calculo consumen el mismo resultado del motor para mantenerlos sincronizados. Los archivos `.encproj` guardan la geometria de la losa y las columnas en metros; no guardan los archivos generados al compilar.

## Requisitos

- SDK de **.NET 10**.
- Windows para usar los scripts `.bat` incluidos. Avalonia es multiplataforma, pero la ejecucion en otros sistemas no se ha verificado en este proyecto.
- Conexion a Internet la primera vez que se restauran los paquetes NuGet. Si falta el SDK 10, `run.bat` puede instalar una copia local en `.dotnet10`.

## Ejecutar

Desde el Explorador de archivos, abre `run.bat` y elige la opcion **1** para iniciar la aplicacion. La primera ejecucion puede tardar mientras se instala el SDK local y se restauran las dependencias.

Tambien puedes usar PowerShell desde la carpeta del proyecto:

```powershell
dotnet run --project .\EncofradoCAD.App\EncofradoCAD.App.csproj
```

Si tu SDK 10 esta instalado localmente en `.dotnet10`, usa:

```powershell
.\.dotnet10\dotnet.exe run --project .\EncofradoCAD.App\EncofradoCAD.App.csproj
```

## Compilar y probar

```powershell
dotnet build .\EncofradoCAD.sln
dotnet test .\EncofradoCAD.sln
```

Si utilizas el SDK local:

```powershell
.\.dotnet10\dotnet.exe build .\EncofradoCAD.sln
.\.dotnet10\dotnet.exe test .\EncofradoCAD.sln
```

Los scripts `compilar_app.bat` y `probar_motor.bat` tambien permiten compilar la solucion y ejecutar las pruebas desde Windows.

> **Estado conocido:** en la ultima ejecucion de la suite, la solucion compilo correctamente, pero 9 de 240 pruebas fallaron. Por tanto, la compilacion esta verificada, pero esos resultados de prueba aun requieren revision.

## Estructura del repositorio

```text
EncofradoCAD.App/     Interfaz de escritorio
EncofradoCAD.Core/    Geometria y motor de calculo
EncofradoCAD.Tests/   Pruebas automatizadas
EncofradoCAD.sln      Solucion de Visual Studio y .NET
```
