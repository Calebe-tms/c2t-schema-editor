// DBML Editor: servidor local + launcher, tudo num .exe só (sem PowerShell).
// Serve a página embutida em 127.0.0.1 (porta aleatória, token na URL), grava o .dbml
// e abre uma janela limpa do Chrome/Edge. Encerra sozinho quando a janela fecha.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

static class App
{
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    const string Json = "application/json; charset=utf-8";

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string path = args.Length > 0 ? args[0] : Pick();
            if (path == null) return;
            Run(Path.GetFullPath(path), args.Length > 1 && args[1] == "--no-browser");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível abrir o arquivo:\n\n" + ex.Message, "DBML Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static string Pick()
    {
        using (var d = new OpenFileDialog())
        {
            d.Title = "DBML Editor - abrir arquivo";
            d.Filter = "DBML ou schema do Drizzle (*.dbml;*.ts)|*.dbml;*.ts|Todos os arquivos (*.*)|*.*";
            return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        }
    }

    static void Run(string full, bool noBrowser)
    {
        if (!File.Exists(full)) throw new FileNotFoundException("Arquivo não encontrado: " + full);
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dbml-viewer");
        Directory.CreateDirectory(data);
        string hash;
        using (var md5 = MD5.Create())
            hash = BitConverter.ToString(md5.ComputeHash(Utf8.GetBytes(full.ToLowerInvariant()))).Replace("-", "").Substring(0, 12).ToLower();
        string layoutFile = Path.Combine(data, "layout-" + hash + ".json");
        string prefsFile = Path.Combine(data, "prefs.json");

        string token = Guid.NewGuid().ToString("N");
        HttpListener listener = null;
        int port = 0;
        var rnd = new Random();
        for (int i = 0; i < 20 && listener == null; i++)
        {
            port = rnd.Next(49152, 65000);
            var l = new HttpListener();
            l.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try { l.Start(); listener = l; } catch (HttpListenerException) { }
        }
        if (listener == null) throw new Exception("Não foi possível abrir uma porta local.");
        string url = "http://127.0.0.1:" + port + "/" + token + "/";

        if (noBrowser) File.WriteAllText(Path.Combine(data, "last-url.txt"), url); else OpenBrowser(url);

        var task = listener.GetContextAsync();
        DateTime last = DateTime.Now; DateTime? byeAt = null;
        while (true)
        {
            if (!task.Wait(500))
            {
                if (byeAt.HasValue && DateTime.Now > byeAt.Value) break;
                if ((DateTime.Now - last).TotalMinutes > 10) break;
                continue;
            }
            var ctx = task.Result; task = listener.GetContextAsync();
            last = DateTime.Now; byeAt = null;
            try
            {
                var req = ctx.Request;
                string[] segs = req.Url.AbsolutePath.TrimStart('/').Split(new[] { '/' }, 2);
                if (req.Url.Host != "127.0.0.1" || segs[0] != token) { Text(ctx, 404, "not found", "text/plain"); continue; }
                string rest = segs.Length > 1 ? segs[1] : "";
                string m = req.HttpMethod;
                if (rest == "") Bytes(ctx, 200, Res("viewer.html"), "text/html; charset=utf-8");
                else if (rest == "session.js")
                    Text(ctx, 200, "window.__DBML__ = {\"name\":" + Q(Path.GetFileName(full)) + ",\"path\":" + Q(full) + ",\"api\":true};", "application/javascript; charset=utf-8");
                else if (rest.StartsWith("js/") && rest.IndexOf("..") < 0 && rest.EndsWith(".js"))
                {
                    byte[] b = Res(rest);
                    if (b != null) Bytes(ctx, 200, b, "application/javascript; charset=utf-8"); else Text(ctx, 404, "not found", "text/plain");
                }
                else if (rest == "file")
                {
                    if (m == "PUT") { File.WriteAllText(full, Body(ctx), Utf8); Text(ctx, 200, "{\"mtime\":" + Mtime(full) + "}", Json); }
                    else { ctx.Response.Headers.Add("X-Mtime", Mtime(full).ToString()); Bytes(ctx, 200, File.ReadAllBytes(full), "text/plain; charset=utf-8"); }
                }
                else if (rest == "snapshot")
                {
                    // snapshot mais recente do Drizzle (meta/NNNN_snapshot.json ao lado do schema.ts); "null" se não houver
                    string snap = full.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? LatestSnapshot(full) : null;
                    if (snap != null) Bytes(ctx, 200, File.ReadAllBytes(snap), Json); else Text(ctx, 200, "null", Json);
                }
                else if (rest == "ping") Text(ctx, 200, "{\"mtime\":" + Mtime(full) + "}", Json);
                else if (rest == "bye") { Text(ctx, 200, "{}", Json); byeAt = DateTime.Now.AddSeconds(8); }
                else if (rest == "layout" || rest == "prefs")
                {
                    string f = rest == "layout" ? layoutFile : prefsFile;
                    if (m == "PUT") { File.WriteAllText(f, Body(ctx), Utf8); Text(ctx, 200, "{}", Json); }
                    else if (File.Exists(f)) Bytes(ctx, 200, File.ReadAllBytes(f), Json);
                    else Text(ctx, 200, "null", Json);
                }
                else Text(ctx, 404, "not found", "text/plain");
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
        listener.Stop();
    }

    static void OpenBrowser(string url)
    {
        string pf = Environment.GetEnvironmentVariable("ProgramFiles"), pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
               local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string exe = Find(new[] { pf + @"\Google\Chrome\Application\chrome.exe", pf86 + @"\Google\Chrome\Application\chrome.exe", local + @"\Google\Chrome\Application\chrome.exe" }, "chrome.exe")
                  ?? Find(new[] { pf86 + @"\Microsoft\Edge\Application\msedge.exe", pf + @"\Microsoft\Edge\Application\msedge.exe" }, "msedge.exe");
        if (exe != null) Process.Start(exe, "--app=" + url); else Process.Start(url);
    }

    static string Find(string[] candidates, string regName)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + regName))
            {
                var v = k == null ? null : k.GetValue(null) as string;
                if (v != null && File.Exists(v)) return v;
            }
        foreach (var c in candidates) if (c != null && File.Exists(c)) return c;
        return null;
    }

    static string LatestSnapshot(string schemaFile)
    {
        string dir = Path.Combine(Path.GetDirectoryName(schemaFile), "meta");
        if (!Directory.Exists(dir)) return null;
        string[] files = Directory.GetFiles(dir, "*_snapshot.json");
        if (files.Length == 0) return null;
        Array.Sort(files, StringComparer.OrdinalIgnoreCase); // 0000_, 0001_… : o último é o mais recente
        return files[files.Length - 1];
    }

    static byte[] Res(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null) return null;
            var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
        }
    }

    static long Mtime(string p) { return File.Exists(p) ? (long)(File.GetLastWriteTimeUtc(p) - new DateTime(1970, 1, 1)).TotalMilliseconds : 0; }
    static string Body(HttpListenerContext c) { using (var sr = new StreamReader(c.Request.InputStream, Utf8)) return sr.ReadToEnd(); }
    static void Text(HttpListenerContext c, int status, string text, string type) { Bytes(c, status, Utf8.GetBytes(text), type); }

    static void Bytes(HttpListenerContext c, int status, byte[] b, string type)
    {
        var r = c.Response; r.StatusCode = status; r.ContentType = type;
        r.Headers.Add("Cache-Control", "no-store");
        r.ContentLength64 = b.Length;
        if (b.Length > 0) r.OutputStream.Write(b, 0, b.Length);
        r.OutputStream.Close();
    }

    static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch < 32 || ch > 126) sb.AppendFormat("\\u{0:x4}", (int)ch);
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
