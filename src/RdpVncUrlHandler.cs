using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace RdpVncUrlHandler
{
    /// <summary>
    /// Browser URL protocol handler for rdp:// and vnc:// links.
    /// rdp:// starts mstsc.exe with a generated .rdp file, vnc:// starts the TightVNC viewer.
    /// </summary>
    internal sealed class Settings
    {
        public string MstscPath = string.Empty;
        public string VncViewerPath = string.Empty;
        public string VncViewerArgs = "{hostport}";
        public int VncDefaultPort = 5900;
        public int ScreenModeId = 2;
        public int UseMultiMon = 0;
        public int DesktopWidth = 1280;
        public int DesktopHeight = 800;
        public int PromptForCredentials = 1;
        public int AuthenticationLevel = 0;
        public int RedirectClipboard = 1;
        public int SessionBpp = 32;
        public int DefaultPort = 3389;
        public bool AllowRemoteHosts = false;
        public string[] AllowedHosts = new string[] { "127.0.0.1", "localhost", "::1" };
        public bool AllowMultipleSessions = false;
        public int LogRetentionDays = 14;
        public bool LogVerbose = false;
        public bool DeleteRdpFile = true;

        public static Settings Load()
        {
            Settings s = new Settings();
            s.MergeFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"));
            s.MergeFile(Path.Combine(Paths.AppDataDir, "settings.json"));
            return s;
        }

        private void MergeFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, object> map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (map == null) return;
                foreach (KeyValuePair<string, object> kv in map) Apply(kv.Key, kv.Value);
                Program.LogLine("INFO", "settings merged from " + path);
            }
            catch (Exception ex)
            {
                Program.LogLine("WARN", "ignoring settings file " + path + ": " + ex.Message);
            }
        }

        private void Apply(string key, object value)
        {
            try
            {
                string k = (key ?? string.Empty).Trim();
                if (k.Length == 0 || value == null) return;
                if (k == "MstscPath") { MstscPath = Text(value); return; }
                if (k == "VncViewerPath") { VncViewerPath = Text(value); return; }
                if (k == "VncViewerArgs") { VncViewerArgs = Text(value); return; }
                if (k == "VncDefaultPort") { VncDefaultPort = ToInt(value, VncDefaultPort); return; }
                if (k == "ScreenModeId") { ScreenModeId = ToInt(value, ScreenModeId); return; }
                if (k == "UseMultiMon") { UseMultiMon = ToInt(value, UseMultiMon); return; }
                if (k == "DesktopWidth") { DesktopWidth = ToInt(value, DesktopWidth); return; }
                if (k == "DesktopHeight") { DesktopHeight = ToInt(value, DesktopHeight); return; }
                if (k == "PromptForCredentials") { PromptForCredentials = ToInt(value, PromptForCredentials); return; }
                if (k == "AuthenticationLevel") { AuthenticationLevel = ToInt(value, AuthenticationLevel); return; }
                if (k == "RedirectClipboard") { RedirectClipboard = ToInt(value, RedirectClipboard); return; }
                if (k == "SessionBpp") { SessionBpp = ToInt(value, SessionBpp); return; }
                if (k == "DefaultPort") { DefaultPort = ToInt(value, DefaultPort); return; }
                if (k == "AllowRemoteHosts") { AllowRemoteHosts = ToBool(value, AllowRemoteHosts); return; }
                if (k == "AllowedHosts") { AllowedHosts = ToStringArray(value, AllowedHosts); return; }
                if (k == "AllowMultipleSessions") { AllowMultipleSessions = ToBool(value, AllowMultipleSessions); return; }
                if (k == "LogRetentionDays") { LogRetentionDays = ToInt(value, LogRetentionDays); return; }
                if (k == "LogVerbose") { LogVerbose = ToBool(value, LogVerbose); return; }
                if (k == "DeleteRdpFile") { DeleteRdpFile = ToBool(value, DeleteRdpFile); return; }
                Program.LogLine("WARN", "unknown setting '" + k + "' ignored");
            }
            catch (Exception ex)
            {
                Program.LogLine("WARN", "bad value for setting '" + key + "': " + ex.Message);
            }
        }

        private static string Text(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int ToInt(object value, int fallback)
        {
            if (value is bool) return ((bool)value) ? 1 : 0;
            if (value is int) return (int)value;
            int parsed;
            if (int.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }

        private static bool ToBool(object value, bool fallback)
        {
            if (value is bool) return (bool)value;
            string s = (Text(value) ?? string.Empty).Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "on") return true;
            if (s == "0" || s == "false" || s == "no" || s == "off") return false;
            return fallback;
        }

        private static string[] ToStringArray(object value, string[] fallback)
        {
            object[] items = value as object[];
            if (items == null) return fallback;
            List<string> list = new List<string>();
            foreach (object item in items)
            {
                string s = Text(item);
                if (!string.IsNullOrEmpty(s)) list.Add(s.Trim());
            }
            return list.Count == 0 ? fallback : list.ToArray();
        }
    }

    internal static class Paths
    {
        public static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RdpVncUrlHandler");

        public static readonly string TempDir = Path.Combine(Path.GetTempPath(), "RdpVncUrlHandler");

        private static string logPath;

        public static string LogPath
        {
            get
            {
                if (logPath == null)
                {
                    logPath = Path.Combine(AppDataDir, "logs",
                        "handler-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                }
                return logPath;
            }
        }

        public static string Mstsc(Settings settings)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(settings.MstscPath)) candidates.Add(Explicit(settings.MstscPath, "MstscPath"));
            string system = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrEmpty(system)) system = @"C:\Windows";
            candidates.Add(Path.Combine(system, @"System32\mstsc.exe"));
            return Existing(candidates, "mstsc.exe was not found. Set MstscPath in settings.json.");
        }

        public static string VncViewer(Settings settings)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(settings.VncViewerPath)) candidates.Add(Explicit(settings.VncViewerPath, "VncViewerPath"));

            foreach (string folder in SpecialFolders())
            {
                candidates.Add(Path.Combine(folder, @"TightVNC\tvnviewer.exe"));
            }
            return Existing(candidates, "no VNC viewer was found. Set VncViewerPath in settings.json.");
        }

        private static IEnumerable<string> SpecialFolders()
        {
            List<string> folders = new List<string>();
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            foreach (string folder in folders)
            {
                if (!string.IsNullOrEmpty(folder)) yield return folder;
            }
        }

        /// <summary>An explicit setting is authoritative: a wrong path is an error, not a hint.</summary>
        private static string Explicit(string configured, string settingName)
        {
            string value = configured.Trim().Trim('"');
            try
            {
                if (File.Exists(value)) return Path.GetFullPath(value);
            }
            catch (ArgumentException)
            {
            }
            throw new FileNotFoundException(settingName + " '" + value + "' from settings.json does not exist.");
        }

        private static string Existing(List<string> candidates, string errorMessage)
        {
            foreach (string candidate in candidates)
            {
                try
                {
                    if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }
            throw new FileNotFoundException(errorMessage);
        }
    }

    internal sealed class Target
    {
        public bool IsVnc;
        public string Host = string.Empty;      // written into "full address"
        public string BareHost = string.Empty;  // host without IPv6 brackets, used for the allow list
        public int Port;
        public readonly List<KeyValuePair<string, string>> Overrides = new List<KeyValuePair<string, string>>();
    }

    internal static class Program
    {
        private const string RdpScheme = "rdp";
        private const string VncScheme = "vnc";
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitBlocked = 2;
        private const int ExitLaunchFailed = 3;
        private const int AttachParentProcess = -1;

        private static readonly Regex HostPattern = new Regex(@"^\[?[A-Za-z0-9]([A-Za-z0-9._\-]{0,253}[A-Za-z0-9])?\]?$", RegexOptions.CultureInvariant);
        private static readonly Regex SettingKeyPattern = new Regex(@"^[A-Za-z][A-Za-z0-9 _\-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex SettingValuePattern = new Regex(@"^[A-Za-z0-9 ._:;,/=\\@%\-\+]{0,300}$", RegexOptions.CultureInvariant);

        private static bool verbose;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        private static int Main(string[] args)
        {
            bool showHelp = false;
            bool dryRun = false;
            bool noWait = false;
            string url = null;

            for (int i = 0; i < args.Length; i++)
            {
                string token = (args[i] ?? string.Empty).Trim().Trim('"').Trim();
                if (token.Length == 0) continue;

                if (token.StartsWith("-", StringComparison.Ordinal) || token.StartsWith("/", StringComparison.Ordinal))
                {
                    string option = token.ToLowerInvariant();
                    if (option == "/?" || option == "-?" || option == "--help" || option == "-h" || option == "-help" || option == "/h" || option == "/help")
                        showHelp = true;
                    else if (option == "/dryrun" || option == "--dryrun" || option == "-dryrun" || option == "/test" || option == "-test")
                        dryRun = true;
                    else if (option == "/nowait" || option == "--nowait" || option == "-nowait" || option == "/n")
                        noWait = true;
                    else if (option == "/verbose" || option == "--verbose" || option == "-verbose" || option == "/v")
                        verbose = true;
                    else if (option == "/console" || option == "--console" || option == "-console")
                        AttachParentConsole();
                    else
                        LogLine("WARN", "ignoring unknown option " + token);
                    continue;
                }

                if (url == null) url = token;
            }

            if (showHelp)
            {
                PrintUsage();
                return ExitOk;
            }

            if (url == null) url = UrlFromCommandLine();
            if (string.IsNullOrEmpty(url))
            {
                LogLine("ERROR", "no rdp:// or vnc:// url was supplied");
                PrintUsage();
                return ExitUsage;
            }

            if (dryRun) AttachParentConsole();

            Settings settings = Settings.Load();
            if (verbose) settings.LogVerbose = true;

            CleanupOldLogs(settings);

            LogLine("INFO", "invoked with url=" + Redact(url));

            Target target;
            string error;
            if (!TryParseUrl(url, settings, out target, out error))
            {
                LogLine("ERROR", "rejected url: " + error);
                return ExitUsage;
            }

            LogLine("INFO", "target " + target.BareHost + ":" + target.Port.ToString(CultureInfo.InvariantCulture) +
                (target.Overrides.Count > 0 ? " with " + target.Overrides.Count.ToString(CultureInfo.InvariantCulture) + " setting override(s)" : string.Empty));

            if (dryRun)
            {
                WriteLine("--- " + target.BareHost + ":" + target.Port.ToString(CultureInfo.InvariantCulture) + " (" +
                          (target.IsVnc ? "TightVNC" : "mstsc") + ") ---");
                WriteLine(target.IsVnc ? DescribeVncLaunch(target, settings) : BuildRdpFile(target, settings));
                WriteLine("--- dry run, nothing was started ---");
                return ExitOk;
            }

            if (target.IsVnc) return StartVncSession(target, settings);

            if (settings.AllowMultipleSessions) return StartMstscSession(target, settings, noWait);

            using (Mutex single = new Mutex(false, MutexName(target)))
            {
                bool acquired;
                try
                {
                    acquired = single.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    LogLine("INFO", "another session is already active, ignoring the duplicate request");
                    return ExitOk;
                }

                try
                {
                    return StartMstscSession(target, settings, noWait);
                }
                finally
                {
                    try { single.ReleaseMutex(); } catch (ApplicationException) { }
                }
            }
        }

        private static int StartVncSession(Target target, Settings settings)
        {
            string viewer;
            try
            {
                viewer = Paths.VncViewer(settings);
            }
            catch (Exception ex)
            {
                LogLine("ERROR", ex.Message);
                return ExitLaunchFailed;
            }

            string arguments = BuildVncArguments(target, settings);
            LogLine("INFO", "starting the vnc viewer: " + viewer + " " + arguments);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(viewer, arguments);
                psi.UseShellExecute = true;
                Process process = Process.Start(psi);
                if (process == null)
                {
                    LogLine("ERROR", "the vnc viewer did not start");
                    return ExitLaunchFailed;
                }
                LogLine("INFO", "vnc viewer running (pid " + process.Id.ToString(CultureInfo.InvariantCulture) + ")");
                return ExitOk;
            }
            catch (Exception ex)
            {
                LogLine("ERROR", "failed to start the vnc viewer: " + ex.Message);
                return ExitLaunchFailed;
            }
        }

        private static int StartMstscSession(Target target, Settings settings, bool noWait)
        {
            string mstsc;
            try
            {
                mstsc = Paths.Mstsc(settings);
            }
            catch (Exception ex)
            {
                LogLine("ERROR", ex.Message);
                return ExitLaunchFailed;
            }

            string content;
            try
            {
                content = BuildRdpFile(target, settings);
            }
            catch (Exception ex)
            {
                LogLine("ERROR", "could not build the rdp file: " + ex.Message);
                return ExitUsage;
            }

            string rdpFile;
            try
            {
                CleanupStaleFiles();
                Directory.CreateDirectory(Paths.TempDir);
                rdpFile = Path.Combine(Paths.TempDir, string.Format(CultureInfo.InvariantCulture, "rdp-{0}-{1}.rdp",
                    Process.GetCurrentProcess().Id,
                    DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)));
                File.WriteAllText(rdpFile, content, new UnicodeEncoding(false, true));
            }
            catch (Exception ex)
            {
                LogLine("ERROR", "could not write the rdp file: " + ex.Message);
                return ExitLaunchFailed;
            }

            LogLine("INFO", "starting " + mstsc + " \"" + rdpFile + "\"");
            ProcessStartInfo psi = new ProcessStartInfo(mstsc, "\"" + rdpFile + "\"");
            psi.UseShellExecute = true;
            try
            {
                Process process = Process.Start(psi);
                if (process == null)
                {
                    LogLine("ERROR", "mstsc did not start");
                    TryDelete(rdpFile);
                    return ExitLaunchFailed;
                }

                if (noWait)
                {
                    LogLine("INFO", "mstsc running in background (pid " + process.Id.ToString(CultureInfo.InvariantCulture) + "), rdp file kept at " + rdpFile);
                    return ExitOk;
                }

                process.WaitForExit();
                LogLine("INFO", "mstsc exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture));
                if (settings.DeleteRdpFile) TryDelete(rdpFile);
                return ExitOk;
            }
            catch (Exception ex)
            {
                LogLine("ERROR", "failed to start mstsc: " + ex.Message);
                TryDelete(rdpFile);
                return ExitLaunchFailed;
            }
        }

        private static string BuildVncArguments(Target target, Settings settings)
        {
            string template = string.IsNullOrEmpty(settings.VncViewerArgs) ? "{hostport}" : settings.VncViewerArgs;
            string port = target.Port.ToString(CultureInfo.InvariantCulture);
            // TightVNC reads host::port, a single colon would mean a display number
            return template.Replace("{hostport}", target.BareHost + "::" + port)
                            .Replace("{host}", target.BareHost)
                            .Replace("{port}", port);
        }

        private static string DescribeVncLaunch(Target target, Settings settings)
        {
            try
            {
                return Paths.VncViewer(settings) + " " + BuildVncArguments(target, settings);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string MutexName(Target target)
        {
            string sid = string.Empty;
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) sid = identity.User.Value;
            }
            catch (Exception)
            {
            }
            if (string.IsNullOrEmpty(sid)) sid = Environment.UserName;
            return @"Local\RdpVncUrlHandler-" + sid + "-" + target.BareHost.Replace("[", "").Replace("]", "") + "-" + target.Port.ToString(CultureInfo.InvariantCulture);
        }

        private static void CleanupOldLogs(Settings settings)
        {
            if (settings.LogRetentionDays <= 0) return;
            try
            {
                string logDir = Path.GetDirectoryName(Paths.LogPath);
                if (!Directory.Exists(logDir)) return;
                DateTime cutoff = DateTime.Now.AddDays(-settings.LogRetentionDays);
                foreach (string file in Directory.GetFiles(logDir, "handler-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                LogLine("WARN", "log cleanup failed: " + ex.Message);
            }
        }

        private static void CleanupStaleFiles()
        {
            try
            {
                if (!Directory.Exists(Paths.TempDir)) return;
                DateTime cutoff = DateTime.Now.AddDays(-1);
                foreach (string file in Directory.GetFiles(Paths.TempDir, "rdp-*.rdp"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                LogLine("WARN", "stale file cleanup failed: " + ex.Message);
            }
        }

        private static void TryDelete(string file)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(250);
                }
            }
            LogLine("WARN", "could not delete " + file);
        }

        private static bool TryParseUrl(string rawUrl, Settings settings, out Target target, out string error)
        {
            target = null;
            error = null;

            string url = (rawUrl ?? string.Empty).Trim().Trim('"').Trim();
            if (url.Length == 0)
            {
                error = "empty url";
                return false;
            }

            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                error = "missing rdp:// or vnc:// scheme";
                return false;
            }

            string scheme = url.Substring(0, schemeEnd);
            bool isVnc;
            if (scheme.Equals(RdpScheme, StringComparison.OrdinalIgnoreCase)) isVnc = false;
            else if (scheme.Equals(VncScheme, StringComparison.OrdinalIgnoreCase)) isVnc = true;
            else
            {
                error = "unsupported scheme '" + scheme + "'";
                return false;
            }

            string rest = url.Substring(schemeEnd + 3);
            string query = null;
            int queryStart = rest.IndexOf('?');
            if (queryStart >= 0)
            {
                query = rest.Substring(queryStart + 1);
                rest = rest.Substring(0, queryStart);
            }

            int pathStart = rest.IndexOf('/');
            if (pathStart >= 0) rest = rest.Substring(0, pathStart);

            rest = Uri.UnescapeDataString(rest).Trim();
            if (rest.Length == 0)
            {
                error = "no host in url";
                return false;
            }

            if (rest.IndexOf('@') >= 0 || rest.IndexOf('\\') >= 0)
            {
                error = "credentials in the url are not supported";
                return false;
            }

            string host;
            string portText = null;
            if (rest.StartsWith("[", StringComparison.Ordinal))
            {
                int close = rest.IndexOf(']');
                if (close < 0)
                {
                    error = "malformed ipv6 literal";
                    return false;
                }
                host = rest.Substring(0, close + 1);
                string tail = rest.Substring(close + 1);
                if (tail.Length > 0)
                {
                    if (tail[0] != ':')
                    {
                        error = "malformed ipv6 literal";
                        return false;
                    }
                    portText = tail.Substring(1);
                }
            }
            else
            {
                int colon = rest.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = rest.Substring(0, colon);
                    portText = rest.Substring(colon + 1);
                }
                else
                {
                    host = rest;
                }
            }

            string bareHost = host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal)
                ? host.Substring(1, host.Length - 2)
                : host;

            if (!IsWellFormedHost(host, bareHost, out error)) return false;

            int port = isVnc ? settings.VncDefaultPort : settings.DefaultPort;
            if (portText != null && portText.Length > 0)
            {
                int parsedPort;
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out parsedPort) || parsedPort < 1 || parsedPort > 65535)
                {
                    error = "invalid port '" + portText + "'";
                    return false;
                }
                port = parsedPort;
            }

            if (port < 1 || port > 65535)
            {
                error = "invalid port " + port.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (!IsHostAllowed(bareHost, settings, out error)) return false;

            target = new Target();
            target.IsVnc = isVnc;
            target.Host = host;
            target.BareHost = bareHost;
            target.Port = port;

            if (!string.IsNullOrEmpty(query))
            {
                if (isVnc)
                {
                    error = "vnc:// links do not take setting overrides";
                    return false;
                }

                foreach (string pair in query.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int equals = pair.IndexOf('=');
                    string keyPart = equals >= 0 ? pair.Substring(0, equals) : pair;
                    string valuePart = equals >= 0 ? pair.Substring(equals + 1) : string.Empty;

                    // mstsc writes overrides as "name:type=value", plain "name=value" also accepted
                    string key = keyPart;
                    string type = null;
                    int colonInKey = keyPart.IndexOf(':');
                    if (colonInKey > 0)
                    {
                        type = keyPart.Substring(colonInKey + 1);
                        key = keyPart.Substring(0, colonInKey);
                    }

                    string value = valuePart;
                    if (type == null)
                    {
                        int colonInValue = valuePart.IndexOf(':');
                        if (colonInValue > 0 && IsRdpType(valuePart.Substring(0, colonInValue)))
                        {
                            type = valuePart.Substring(0, colonInValue);
                            value = valuePart.Substring(colonInValue + 1);
                        }
                    }

                    key = Uri.UnescapeDataString(key).Trim();
                    value = Uri.UnescapeDataString(value).Trim();

                    if (type == null || type.Length != 1 || !IsRdpType(type)) type = InferType(value);

                    if (!SettingKeyPattern.IsMatch(key) || !SettingValuePattern.IsMatch(value))
                    {
                        error = "rejected rdp setting override '" + key + "'";
                        return false;
                    }

                    if (key.Equals("full address", StringComparison.OrdinalIgnoreCase) && !IsValidAddressValue(value, settings, out error))
                    {
                        return false;
                    }

                    target.Overrides.Add(new KeyValuePair<string, string>(key, type + ":" + value));
                }
            }

            return true;
        }

        private static bool IsWellFormedHost(string host, string bareHost, out string error)
        {
            error = null;
            if (bareHost.Length == 0)
            {
                error = "empty host";
                return false;
            }

            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                System.Net.IPAddress parsed;
                if (!System.Net.IPAddress.TryParse(bareHost, out parsed) ||
                    parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    error = "invalid ipv6 literal '" + bareHost + "'";
                    return false;
                }
                return true;
            }

            if (!HostPattern.IsMatch(host))
            {
                error = "invalid host '" + bareHost + "'";
                return false;
            }

            return true;
        }

        private static bool IsValidAddressValue(string value, Settings settings, out string error)
        {
            error = null;
            string address = value;
            int schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd > 0) address = address.Substring(schemeEnd + 3);

            string host;
            string portText = null;
            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                int close = address.IndexOf(']');
                if (close < 0)
                {
                    error = "malformed ipv6 literal in the full address override";
                    return false;
                }
                host = address.Substring(0, close + 1);
                string tail = address.Substring(close + 1);
                if (tail.Length > 0)
                {
                    if (tail[0] != ':')
                    {
                        error = "malformed ipv6 literal in the full address override";
                        return false;
                    }
                    portText = tail.Substring(1);
                }
            }
            else
            {
                int colon = address.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = address.Substring(0, colon);
                    portText = address.Substring(colon + 1);
                }
                else
                {
                    host = address;
                }
            }

            string bareHost = host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal)
                ? host.Substring(1, host.Length - 2)
                : host;

            if (!IsWellFormedHost(host, bareHost, out error)) return false;

            if (portText != null && portText.Length > 0)
            {
                int parsedPort;
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out parsedPort) || parsedPort < 1 || parsedPort > 65535)
                {
                    error = "invalid port '" + portText + "' in the full address override";
                    return false;
                }
            }

            return IsHostAllowed(bareHost, settings, out error);
        }

        private static bool IsHostAllowed(string bareHost, Settings settings, out string error)
        {
            error = null;
            if (settings.AllowRemoteHosts) return true;

            System.Net.IPAddress address;
            if (System.Net.IPAddress.TryParse(bareHost, out address) && System.Net.IPAddress.IsLoopback(address)) return true;
            if (bareHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (string entry in settings.AllowedHosts ?? new string[0])
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string trimmed = entry.Trim();
                if (trimmed.Equals(bareHost, StringComparison.OrdinalIgnoreCase)) return true;
                if (trimmed.StartsWith("*.", StringComparison.Ordinal) &&
                    bareHost.EndsWith(trimmed.Substring(1), StringComparison.OrdinalIgnoreCase) &&
                    bareHost.Length > trimmed.Length - 1)
                {
                    return true;
                }
            }

            error = "host '" + bareHost + "' is not in the allow list (add it to AllowedHosts in settings.json, or set AllowRemoteHosts to true)";
            return false;
        }

        private sealed class RdpFile
        {
            private readonly List<string> names = new List<string>();
            private readonly List<string> types = new List<string>();
            private readonly List<string> values = new List<string>();

            public void Set(string name, string type, string value)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        types[i] = type;
                        values[i] = value;
                        return;
                    }
                }
                names.Add(name);
                types.Add(type);
                values.Add(value);
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < names.Count; i++)
                {
                    sb.Append(names[i]).Append(':').Append(types[i]).Append(':').Append(values[i]).Append("\r\n");
                }
                return sb.ToString();
            }
        }

        private static string BuildRdpFile(Target target, Settings settings)
        {
            RdpFile file = new RdpFile();
            file.Set("screen mode id", "i", settings.ScreenModeId.ToString(CultureInfo.InvariantCulture));
            file.Set("use multimon", "i", settings.UseMultiMon.ToString(CultureInfo.InvariantCulture));
            file.Set("desktopwidth", "i", settings.DesktopWidth.ToString(CultureInfo.InvariantCulture));
            file.Set("desktopheight", "i", settings.DesktopHeight.ToString(CultureInfo.InvariantCulture));
            file.Set("session bpp", "i", settings.SessionBpp.ToString(CultureInfo.InvariantCulture));
            file.Set("full address", "s", target.Host + ":" + target.Port.ToString(CultureInfo.InvariantCulture));
            file.Set("prompt for credentials", "i", settings.PromptForCredentials.ToString(CultureInfo.InvariantCulture));
            file.Set("authentication level", "i", settings.AuthenticationLevel.ToString(CultureInfo.InvariantCulture));
            file.Set("enablecredsspsupport", "i", "1");
            file.Set("redirectclipboard", "i", settings.RedirectClipboard.ToString(CultureInfo.InvariantCulture));

            foreach (KeyValuePair<string, string> setting in target.Overrides)
            {
                // overrides are stored as "type:value"
                string type = setting.Value.Substring(0, 1);
                string value = setting.Value.Length > 2 ? setting.Value.Substring(2) : string.Empty;
                file.Set(setting.Key, type, value);
            }

            return file.ToString();
        }

        private static bool IsRdpType(string type)
        {
            return type == "s" || type == "i" || type == "b";
        }

        private static string InferType(string value)
        {
            if (value.Length == 0) return "s";
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9') return "s";
            }
            return "i";
        }

        private static void AttachParentConsole()
        {
            try
            {
                if (!AttachConsole(AttachParentProcess)) return;
            }
            catch (Exception)
            {
            }
        }

        private static string UrlFromCommandLine()
        {
            try
            {
                string[] all = Environment.GetCommandLineArgs();
                for (int i = 1; i < all.Length; i++)
                {
                    string candidate = all[i].Trim().Trim('"');
                    if (candidate.IndexOf("://", StringComparison.Ordinal) > 0) return candidate;
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static void PrintUsage()
        {
            WriteLine("RdpVncUrlHandler - starts mstsc.exe for rdp:// links and TightVNC for vnc:// links");
            WriteLine("");
            WriteLine("  rdp://host[:port]   remote desktop  (default port " + "3389)");
            WriteLine("  vnc://host[:port]   vnc session     (default port 5900)");
            WriteLine("");
            WriteLine("  RdpVncUrlHandler.exe <url> [options]");
            WriteLine("");
            WriteLine("  /dryrun    show what would run, start nothing");
            WriteLine("  /nowait    return as soon as mstsc is running");
            WriteLine("  /verbose   log extra detail");
            WriteLine("  /console   write output to the parent console");
            WriteLine("  /?         show this help");
            WriteLine("");
            WriteLine("  settings: " + Path.Combine(Paths.AppDataDir, "settings.json"));
            WriteLine("  log:      " + Paths.LogPath);
        }

        private static void WriteLine(string message)
        {
            try { Console.Out.WriteLine(message); } catch (IOException) { }
        }

        private static string Redact(string url)
        {
            int at = url.IndexOf('@');
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            return at > 0 && scheme > 0 && at < scheme ? "<redacted>" : url;
        }

        internal static void LogLine(string level, string message)
        {
            string line = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [pid {2}] {3}",
                DateTime.Now, level, Process.GetCurrentProcess().Id, message);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.LogPath));
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        File.AppendAllText(Paths.LogPath, line + Environment.NewLine, Encoding.UTF8);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception)
            {
            }

            if (level == "ERROR" || level == "WARN" || verbose)
            {
                WriteLine(line);
            }
        }
    }
}