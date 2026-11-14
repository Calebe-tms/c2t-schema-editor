// Dados do produto num lugar só: compilado junto com o app (App.cs) e com o instalador (Setup.cs).
// Para lançar uma versão nova, mude só Version aqui e rode installer\build.ps1.
static class Product
{
    public const string Name = "c2t-schema-db-editor";
    public const string Version = "1.1.0";
    public const string Publisher = "Calebe-tms";
    public const string Description = "Editor e visualizador de diagramas ER para DBML e schema do Drizzle";
    public const string Url = "https://github.com/Calebe-tms/dbml-editor";
    public const string Copyright = "Copyright © 2026 " + Publisher;

    public const string ExeName = Name + ".exe";   // executável instalado
    public const string DataDir = Name;            // %APPDATA%\<DataDir>: layouts e preferências
    public const string LegacyDataDir = "dbml-viewer"; // pasta de dados de quando o app se chamava DBML Editor
}
