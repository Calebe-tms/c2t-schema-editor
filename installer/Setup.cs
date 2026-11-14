// Instalador/desinstalador do DBML Editor (por usuário, sem admin).
// Instala em %LOCALAPPDATA%\Programs\DBML Editor, associa .dbml, cria atalho no Menu Iniciar
// e registra a entrada em "Aplicativos instalados". Com /uninstall remove tudo; com /S roda em silêncio.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string Name = "DBML Editor", Version = "1.0.0", Prog = "DBMLEditor.File";
    const string Classes = @"Software\Classes";
    const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DBMLEditor";

    static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\" + Name); } }
    static string AppExe { get { return Path.Combine(Dir, "DbmlEditor.exe"); } }
    static string Uninst { get { return Path.Combine(Dir, "Uninstall.exe"); } }
    static string Lnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); } }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Array.IndexOf(args, "/S") >= 0;
        bool uninstall = Array.IndexOf(args, "/uninstall") >= 0 || Path.GetFileName(Assembly.GetExecutingAssembly().Location).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (uninstall)
            {
                if (!silent && MessageBox.Show("Remover o " + Name + " deste computador?", Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                Remove();
                if (!silent) MessageBox.Show(Name + " foi removido.\nSeus layouts salvos continuam em %APPDATA%\\dbml-viewer.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                if (!silent && MessageBox.Show("Instalar o " + Name + "?\n\nSerá instalado só para o seu usuário (sem precisar de administrador) e os arquivos .dbml passarão a abrir nele. O schema.ts do Drizzle ganha \"Abrir no DBML Editor\" no menu do botão direito.", Name + " - instalação", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1;
                Install();
                if (!silent) MessageBox.Show("Instalação concluída!\n\nNa primeira vez que abrir um .dbml, o Windows perguntará qual app usar: escolha \"" + Name + "\" e clique em Sempre.\nO atalho também está no Menu Iniciar.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show("Erro: " + ex.Message + "\n\nSe o editor estiver aberto, feche as janelas dele e tente de novo.", Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    static void Install()
    {
        Directory.CreateDirectory(Dir);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DbmlEditor.exe"))
        using (var f = File.Create(AppExe)) s.CopyTo(f);
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Uninst), StringComparison.OrdinalIgnoreCase)) File.Copy(self, Uninst, true);

        string cmd = "\"" + AppExe + "\" \"%1\"";
        Set(@"Applications\DbmlEditor.exe", "FriendlyAppName", Name);
        Set(@"Applications\DbmlEditor.exe\shell\open\command", null, cmd);
        Set(@"Applications\DbmlEditor.exe\SupportedTypes", ".dbml", "");
        // .ts só entra na lista "Abrir com" (schema.ts do Drizzle); o app NÃO vira o padrão dos arquivos TypeScript
        Set(@"Applications\DbmlEditor.exe\SupportedTypes", ".ts", "");
        Set(@".ts\OpenWithList\DbmlEditor.exe", null, "");
        // Item no primeiro nível do menu de contexto, como o WinRAR, só para arquivos chamados schema.ts.
        // SystemFileAssociations não muda o programa padrão do .ts.
        const string Verb = @"SystemFileAssociations\.ts\shell\DbmlEditor";
        Set(Verb, null, "Abrir no DBML Editor");
        Set(Verb, "Icon", AppExe + ",0");
        Set(Verb, "AppliesTo", "System.FileName:\"schema.ts\"");
        Set(Verb + @"\command", null, cmd);
        Set(@".dbml", null, Prog);
        Set(@".dbml\OpenWithProgids", Prog, "");
        Set(Prog, null, "Arquivo DBML");
        Set(Prog + @"\DefaultIcon", null, AppExe + ",0");
        Set(Prog + @"\shell\open\command", null, cmd);

        using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", Name);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Equalizagro");
            k.SetValue("DisplayIcon", AppExe);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", "\"" + Uninst + "\" /uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { Lnk });
        Prop(lnk, "TargetPath", AppExe); Prop(lnk, "WorkingDirectory", Dir); Prop(lnk, "Description", Name);
        lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        Notify();
    }

    static void Remove()
    {
        foreach (var k in new[] { ".dbml", Prog, @"Applications\DbmlEditor.exe" })
            Registry.CurrentUser.DeleteSubKeyTree(Classes + "\\" + k, false);
        // só a nossa entrada em "Abrir com"; a chave .ts em si é do sistema/outros programas e não pode ser apagada
        Registry.CurrentUser.DeleteSubKeyTree(Classes + @"\.ts\OpenWithList\DbmlEditor.exe", false);
        Registry.CurrentUser.DeleteSubKeyTree(Classes + @"\SystemFileAssociations\.ts\shell\DbmlEditor", false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false);
        if (File.Exists(Lnk)) File.Delete(Lnk);
        if (File.Exists(AppExe)) File.Delete(AppExe);
        Notify();
        // o Uninstall.exe não consegue apagar a si mesmo: delega a um cmd que espera ele sair
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + Uninst + "\" & rmdir \"" + Dir + "\"");
        psi.CreateNoWindow = true; psi.UseShellExecute = false;
        Process.Start(psi);
    }

    static void Set(string key, string name, string value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Classes + "\\" + key)) k.SetValue(name, value);
    }

    static void Prop(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new[] { v }); }

    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);
    static void Notify() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
}
