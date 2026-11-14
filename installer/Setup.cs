// Instalador e desinstalador (por usuário, sem administrador). Nome, versão e editor vêm de Product.cs.
// Instala em %LOCALAPPDATA%\Programs\<nome>, associa .dbml, põe o schema.ts no menu do botão direito,
// cria atalhos e registra a entrada em "Aplicativos instalados".
//   Setup.exe                janela de instalação (ou de atualização, se já houver uma versão instalada)
//   Setup.exe /S             instala em silêncio
//   Uninstall.exe            janela de desinstalação   (ou Setup.exe /uninstall)
//   Uninstall.exe /S         desinstala em silêncio, mantendo layouts e preferências
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// o que aparece em Propriedades > Detalhes do instalador
[assembly: AssemblyTitle(Product.Name + " - Instalador")]
[assembly: AssemblyDescription(Product.Description)]
[assembly: AssemblyProduct(Product.Name)]
[assembly: AssemblyCompany(Product.Publisher)]
[assembly: AssemblyCopyright(Product.Copyright)]
[assembly: AssemblyVersion(Product.Version + ".0")]
[assembly: AssemblyFileVersion(Product.Version + ".0")]
[assembly: AssemblyInformationalVersion(Product.Version)]

static class Setup
{
    const string Prog = "c2tSchemaDbEditor.File";
    const string Classes = @"Software\Classes";
    const string UninstRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    const string UninstKey = UninstRoot + Product.Name;
    const string AppKey = @"Applications\" + Product.ExeName;
    const string TsVerb = @"SystemFileAssociations\.ts\shell\" + Product.Name;

    // instalação antiga, de quando o app se chamava "DBML Editor": é removida ao instalar esta
    const string OldName = "DBML Editor", OldExe = "DbmlEditor.exe", OldProg = "DBMLEditor.File", OldUninstKey = UninstRoot + "DBMLEditor";

    static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
    public static string Dir { get { return Path.Combine(Local, @"Programs\" + Product.Name); } }
    public static string AppExe { get { return Path.Combine(Dir, Product.ExeName); } }
    static string Uninst { get { return Path.Combine(Dir, "Uninstall.exe"); } }
    static string StartLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product.Name + ".lnk"); } }
    static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product.Name + ".lnk"); } }
    static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.DataDir); } }
    static string Self { get { return Assembly.GetExecutingAssembly().Location; } }

    static bool selfDelete; // o Uninstall.exe se apaga ao sair

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Array.IndexOf(args, "/S") >= 0;
        bool uninstall = Array.IndexOf(args, "/uninstall") >= 0 || Path.GetFileName(Self).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase);
        int code = 0;
        if (silent)
        {
            try { CloseRunning(); if (uninstall) Remove(false); else Install(false); }
            catch { code = 2; }
        }
        else
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new SetupForm(uninstall);
            Application.Run(form);
            code = form.Done ? 0 : 1;
        }
        if (selfDelete) DeleteSelf();
        return code;
    }

    /* ============================ consultas ============================ */
    // versão já instalada (desta ou da antiga "DBML Editor"); null se não houver
    public static string InstalledVersion()
    {
        foreach (string key in new[] { UninstKey, OldUninstKey })
            using (var k = Registry.CurrentUser.OpenSubKey(key))
                if (k != null) return (k.GetValue("DisplayVersion") as string ?? "?") + (key == OldUninstKey ? " (" + OldName + ")" : "");
        return null;
    }

    // janelas do editor abertas (cada arquivo aberto é um processo), da versão nova ou da antiga
    public static List<Process> Running()
    {
        var list = new List<Process>(); int me = Process.GetCurrentProcess().Id;
        foreach (string n in new[] { Path.GetFileNameWithoutExtension(Product.ExeName), Path.GetFileNameWithoutExtension(OldExe) })
            foreach (Process p in Process.GetProcessesByName(n)) if (p.Id != me) list.Add(p);
        return list;
    }

    public static void CloseRunning()
    {
        foreach (Process p in Running())
            try { p.Kill(); p.WaitForExit(4000); } catch { /* já saiu */ }
    }

    /* ============================ instalar ============================ */
    public static void Install(bool desktopShortcut)
    {
        RemoveLegacy();
        Directory.CreateDirectory(Dir);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.exe"))
        using (var f = File.Create(AppExe)) s.CopyTo(f);
        if (!string.Equals(Path.GetFullPath(Self), Path.GetFullPath(Uninst), StringComparison.OrdinalIgnoreCase)) File.Copy(Self, Uninst, true);

        string cmd = "\"" + AppExe + "\" \"%1\"", icon = AppExe + ",0";
        Set(AppKey, "FriendlyAppName", Product.Name);
        Set(AppKey + @"\shell\open\command", null, cmd);
        Set(AppKey + @"\SupportedTypes", ".dbml", "");
        // .ts só entra em "Abrir com" e no menu do botão direito do schema.ts; o app NÃO vira o padrão dos arquivos TypeScript
        Set(AppKey + @"\SupportedTypes", ".ts", "");
        Set(@".ts\OpenWithList\" + Product.ExeName, null, "");
        Set(TsVerb, null, "Abrir no " + Product.Name);
        Set(TsVerb, "Icon", icon);
        Set(TsVerb, "AppliesTo", "System.FileName:\"schema.ts\"");
        Set(TsVerb + @"\command", null, cmd);
        // .dbml abre no app com duplo clique
        Set(@".dbml", null, Prog);
        Set(@".dbml\OpenWithProgids", Prog, "");
        Set(Prog, null, "Arquivo DBML");
        Set(Prog + @"\DefaultIcon", null, icon);
        Set(Prog + @"\shell\open\command", null, cmd);

        // entrada em Configurações > Aplicativos instalados
        using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", Product.Name);
            k.SetValue("DisplayVersion", Product.Version);
            k.SetValue("Publisher", Product.Publisher);
            k.SetValue("DisplayIcon", icon);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("EstimatedSize", (int)((new FileInfo(AppExe).Length + new FileInfo(Uninst).Length) / 1024), RegistryValueKind.DWord);
            k.SetValue("URLInfoAbout", Product.Url);
            k.SetValue("HelpLink", Product.Url);
            k.SetValue("Comments", Product.Description);
            k.SetValue("UninstallString", "\"" + Uninst + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + Uninst + "\" /uninstall /S");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        Shortcut(StartLnk);
        if (desktopShortcut) Shortcut(DesktopLnk);
        Notify();
    }

    static void Shortcut(string lnkPath)
    {
        object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
        Prop(lnk, "TargetPath", AppExe); Prop(lnk, "WorkingDirectory", Dir); Prop(lnk, "Description", Product.Description); Prop(lnk, "IconLocation", AppExe + ",0");
        lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    // Tira do computador a instalação com o nome antigo, para não ficarem dois programas iguais
    static void RemoveLegacy()
    {
        string oldDir = Path.Combine(Local, @"Programs\" + OldName);
        foreach (string k in new[] { OldProg, @"Applications\" + OldExe, @".ts\OpenWithList\" + OldExe, @"SystemFileAssociations\.ts\shell\DbmlEditor" })
            Registry.CurrentUser.DeleteSubKeyTree(Classes + "\\" + k, false);
        Registry.CurrentUser.DeleteSubKeyTree(OldUninstKey, false);
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), OldName + ".lnk"));
        if (Directory.Exists(oldDir))
        {
            TryDelete(Path.Combine(oldDir, OldExe)); TryDelete(Path.Combine(oldDir, "Uninstall.exe"));
            try { Directory.Delete(oldDir, false); } catch { /* sobrou algo que não é nosso: fica */ }
        }
    }

    /* ============================ desinstalar ============================ */
    public static void Remove(bool removeData)
    {
        foreach (string k in new[] { ".dbml", Prog, AppKey, TsVerb })
            Registry.CurrentUser.DeleteSubKeyTree(Classes + "\\" + k, false);
        // só a nossa entrada em "Abrir com"; a chave .ts em si é do sistema/outros programas e não pode ser apagada
        Registry.CurrentUser.DeleteSubKeyTree(Classes + @"\.ts\OpenWithList\" + Product.ExeName, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false);
        TryDelete(StartLnk); TryDelete(DesktopLnk);
        if (File.Exists(AppExe)) File.Delete(AppExe); // se falhar (editor aberto), o erro sobe e a janela avisa
        if (removeData && Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        Notify();
        selfDelete = true;
    }

    // um .exe não consegue apagar a si mesmo: delega a um cmd que espera ele sair
    static void DeleteSelf()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + Uninst + "\" & rmdir \"" + Dir + "\"");
        psi.CreateNoWindow = true; psi.UseShellExecute = false;
        Process.Start(psi);
    }

    /* ============================ utilidades ============================ */
    static void Set(string key, string name, string value)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Classes + "\\" + key)) k.SetValue(name, value);
    }

    static void TryDelete(string file) { try { if (File.Exists(file)) File.Delete(file); } catch { /* em uso ou sem permissão: segue */ } }

    static void Prop(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new[] { v }); }

    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);
    static void Notify() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
}

/* Janela única do instalador e do desinstalador: cabeçalho com ícone, texto, opções e botões. */
class SetupForm : Form
{
    readonly bool uninstall;
    readonly Label message = new Label(), detail = new Label();
    readonly CheckBox optA = new CheckBox(), optB = new CheckBox();
    readonly Button primary = new Button(), cancel = new Button();
    bool finished;

    public bool Done { get { return finished; } }

    public SetupForm(bool uninstall)
    {
        this.uninstall = uninstall;
        string previous = Setup.InstalledVersion();
        SuspendLayout();

        // cabeçalho
        var header = new Panel { BackColor = Color.White, Location = new Point(0, 0), Size = new Size(520, 88) };
        var pic = new PictureBox { Location = new Point(20, 16), Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom };
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.png")) if (s != null) pic.Image = Image.FromStream(s);
        var title = new Label { Text = Product.Name, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Location = new Point(90, 16), AutoSize = true };
        var sub = new Label { Text = "Versão " + Product.Version + "  ·  " + Product.Publisher, ForeColor = Color.FromArgb(100, 110, 125), Location = new Point(92, 50), AutoSize = true };
        header.Controls.AddRange(new Control[] { pic, title, sub });
        var rule = new Panel { BackColor = Color.FromArgb(218, 222, 230), Location = new Point(0, 88), Size = new Size(520, 1) };

        // corpo
        message.Location = new Point(24, 108); message.Size = new Size(472, 54);
        detail.Location = new Point(24, 166); detail.Size = new Size(472, 36); detail.ForeColor = Color.FromArgb(100, 110, 125);
        optA.Location = new Point(24, 208); optA.Size = new Size(472, 22);
        optB.Location = new Point(24, 234); optB.Size = new Size(472, 22);

        if (uninstall)
        {
            Text = Product.Name + " - Desinstalação";
            message.Text = "Remover o " + Product.Name + " deste computador?\nO programa, os atalhos e as associações de arquivo serão apagados.";
            detail.Text = "Seus arquivos .dbml e schema.ts não são tocados.";
            optA.Text = "Apagar também meus layouts e preferências"; optA.Checked = false;
            optB.Visible = false;
            primary.Text = "Desinstalar";
        }
        else
        {
            Text = Product.Name + " - Instalação";
            message.Text = previous == null
                ? Product.Description + ".\nA instalação é só para o seu usuário e não pede administrador."
                : "Já existe a versão " + previous + " instalada.\nEla será atualizada para a " + Product.Version + "; layouts e preferências são mantidos.";
            detail.Text = "Pasta: " + Setup.Dir;
            optA.Text = "Criar atalho na Área de Trabalho"; optA.Checked = false;
            optB.Text = "Abrir o programa ao concluir"; optB.Checked = true;
            primary.Text = previous == null ? "Instalar" : "Atualizar";
        }

        // rodapé
        var foot = new Panel { BackColor = Color.FromArgb(243, 245, 248), Location = new Point(0, 272), Size = new Size(520, 58) };
        primary.Location = new Point(292, 14); primary.Size = new Size(104, 30); primary.Click += OnPrimary;
        cancel.Text = "Cancelar"; cancel.Location = new Point(404, 14); cancel.Size = new Size(96, 30); cancel.Click += delegate { Close(); };
        foot.Controls.AddRange(new Control[] { primary, cancel });

        Controls.AddRange(new Control[] { header, rule, message, detail, optA, optB, foot });
        AcceptButton = primary; CancelButton = cancel;
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.White;
        try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { /* fica o ícone padrão */ }
        ResumeLayout(false);
    }

    void OnPrimary(object sender, EventArgs e)
    {
        if (finished)
        {
            if (!uninstall && optB.Checked) try { Process.Start(Setup.AppExe); } catch { /* o atalho continua no Menu Iniciar */ }
            Close(); return;
        }
        int open = Setup.Running().Count;
        if (open > 0)
        {
            string q = (open == 1 ? "Há 1 janela do editor aberta." : "Há " + open + " janelas do editor abertas.")
                + "\n\nSalve o que estiver pendente nelas e clique em Sim: o instalador vai encerrá-las para continuar.";
            if (MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        primary.Enabled = cancel.Enabled = false; Cursor = Cursors.WaitCursor;
        try
        {
            Setup.CloseRunning();
            if (uninstall) Setup.Remove(optA.Checked); else Setup.Install(optA.Checked);
            finished = true;
            if (uninstall)
            {
                message.Text = Product.Name + " foi removido deste computador.";
                detail.Text = optA.Checked ? "Layouts e preferências também foram apagados." : "Layouts e preferências foram mantidos em %APPDATA%\\" + Product.DataDir + ".";
                primary.Text = "Fechar";
            }
            else
            {
                message.Text = "Instalação concluída.\nPara abrir: Menu Iniciar, duplo clique num arquivo .dbml, ou botão direito no schema.ts › \"Abrir no " + Product.Name + "\".";
                detail.Text = "Para remover: Configurações › Aplicativos instalados.";
                primary.Text = "Concluir";
            }
            optA.Visible = false; cancel.Visible = false;
            optB.Location = optA.Location;
            primary.Location = cancel.Location; primary.Size = cancel.Size;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível concluir:\n\n" + ex.Message + "\n\nSe o editor estiver aberto, feche as janelas dele e tente de novo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            cancel.Enabled = true;
        }
        finally { primary.Enabled = true; Cursor = Cursors.Default; }
    }
}
