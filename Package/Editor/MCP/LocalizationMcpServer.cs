using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Editor.Services;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;


namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// All-in-one MCP server hosted inside the Unity Editor.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalizationMcpServer
    {
        public const int DefaultPort = 8123;
        public const string McpPath = "/mcp";

        private const string PortPref = "PicoShot_Localization_McpPort";
        private const string AutoStartPref = "PicoShot_Localization_McpAutoStart";
        private const long MaxRequestBytes = 32L * 1024L * 1024L;

        private static readonly object StateLock = new();

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static IMcpLocaleIO _io;
        private static int _port = DefaultPort;
        private static volatile bool _externalChangePending;

        /// <summary>Invoked on Unity's main thread when MCP clients changed locale files.</summary>
        public static event Action ExternalChanged;

        public static bool IsRunning
        {
            get { lock (StateLock) return _listener != null && _listener.IsListening; }
        }

        public static int Port => _port;

        public static string Url => "http://127.0.0.1:" + _port + McpPath;

        public static string ConfiguredUrl => "http://127.0.0.1:" + PortPrefValue + McpPath;

        static LocalizationMcpServer()
        {
            EditorApplication.update += DrainMainThreadQueue;
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            if (EditorPrefs.GetBool(AutoStartPref, false))
                EditorApplication.delayCall += () => Start(PortPrefValue);
        }

        public static int PortPrefValue
        {
            get => EditorPrefs.GetInt(PortPref, DefaultPort);
            set => EditorPrefs.SetInt(PortPref, value);
        }

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartPref, false);
            set => EditorPrefs.SetBool(AutoStartPref, value);
        }

        #region Menu

        [MenuItem("Tools/Localization/MCP Server/Start Server", false, 50)]
        public static void StartFromMenu()
        {
            Start(PortPrefValue);
        }

        [MenuItem("Tools/Localization/MCP Server/Start Server", true)]
        private static bool ValidateStart()
        {
            return !IsRunning;
        }

        [MenuItem("Tools/Localization/MCP Server/Stop Server", false, 51)]
        public static void StopFromMenu()
        {
            Stop();
        }

        [MenuItem("Tools/Localization/MCP Server/Stop Server", true)]
        private static bool ValidateStop()
        {
            return IsRunning;
        }

        [MenuItem("Tools/Localization/MCP Server/Restart Server", false, 52)]
        public static void RestartFromMenu()
        {
            Stop();
            Start(PortPrefValue);
        }

        [MenuItem("Tools/Localization/MCP Server/Copy Agent Config", false, 60)]
        public static void CopyAgentConfig()
        {
            int port = IsRunning ? _port : PortPrefValue;
            var config = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["mcpServers"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["picoshot-localization"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["url"] = "http://127.0.0.1:" + port + McpPath,
                    }
                },
            };
            EditorGUIUtility.systemCopyBuffer = McpJson.Serialize(config);
            Debug.Log("[Localization MCP] Agent config copied to clipboard. See Docs/MCP.md for per-client setup.");
        }

        [MenuItem("Tools/Localization/MCP Server/Open Status Page", false, 61)]
        public static void OpenStatusPage()
        {
            Application.OpenURL("http://127.0.0.1:" + (IsRunning ? _port : PortPrefValue) + "/");
        }

        #endregion

        #region Lifecycle

        /// <summary>Starts the server. Must be called on Unity's main thread.</summary>
        public static void Start(int port)
        {
            lock (StateLock)
            {
                if (_listener != null && _listener.IsListening)
                {
                    Debug.Log("[Localization MCP] Server is already running at " + Url);
                    return;
                }

                // Resolve everything that touches Unity APIs here, on the main thread.
                string localesDir = LocalizationManager.LanguagesPath;
                string defaultLanguage = LocalizationConfigProvider.Config.DefaultLanguage;
                ApplyCompressionSettings(LocalizationConfigProvider.Config);

                _port = port;
                _io = new NotifyingIO(new BlocMcpLocaleIO(localesDir, defaultLanguage));
                _cts = new CancellationTokenSource();

                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Prefixes.Add("http://localhost:" + port + "/");
                try
                {
                    listener.Start();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Localization MCP] Failed to start on port " + port +
                        ". Another process may be using it (change it via MCP Server settings or restart Unity). " + ex.Message);
                    return;
                }

                _listener = listener;
                CancellationToken token = _cts.Token;
                Task.Run(() => AcceptLoop(listener, token));
            }
        }

        /// <summary>Stops the server. Safe to call from any thread.</summary>
        public static void Stop()
        {
            HttpListener listener;
            CancellationTokenSource cts;
            lock (StateLock)
            {
                listener = _listener;
                cts = _cts;
                _listener = null;
                _cts = null;
            }
            try
            {
                if (cts != null)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            catch (Exception) { }
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch (Exception) { }
        }

        private static void ApplyCompressionSettings(LocalizationConfig config)
        {
            switch (config.CompressionMode)
            {
                case CompressionMode.Disabled:
                    LocaleBlocSerializer.CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression;
                    break;
                case CompressionMode.Fastest:
                    LocaleBlocSerializer.CompressionLevel = System.IO.Compression.CompressionLevel.Fastest;
                    break;
                default:
                    LocaleBlocSerializer.CompressionLevel = System.IO.Compression.CompressionLevel.Optimal;
                    break;
            }
        }

        #endregion

        #region HTTP

        private static async Task AcceptLoop(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break; // Listener stopped.
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Log("[Localization MCP] Accept error: " + ex.Message);
                    break;
                }
#pragma warning disable 4014
                Task.Run(() => HandleContext(context));
#pragma warning restore 4014
            }
        }

        private static void HandleContext(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                string path = request.Url.AbsolutePath ?? "/";

                if (request.HttpMethod == "OPTIONS")
                {
                    WriteEmpty(context, 204);
                    return;
                }
                if (path == "/" && request.HttpMethod == "GET")
                {
                    WriteText(context, 200, "text/html; charset=utf-8", StatusPage());
                    return;
                }
                if (path == "/health" && request.HttpMethod == "GET")
                {
                    var health = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["status"] = "ok",
                        ["server"] = McpProtocol.ServerName,
                        ["version"] = McpProtocol.ServerVersion,
                    };
                    WriteText(context, 200, "application/json", McpJson.Serialize(health));
                    return;
                }
                if (path == McpPath && request.HttpMethod == "GET")
                {
                    context.Response.AddHeader("Allow", "POST");
                    WriteText(context, 405, "application/json",
                        "{\"error\":\"Use POST with a JSON-RPC body (Streamable HTTP, stateless).\"}");
                    return;
                }
                if (path == McpPath && request.HttpMethod == "POST")
                {
                    HandleMcpPost(context);
                    return;
                }
                WriteText(context, 404, "application/json", "{\"error\":\"Not found.\"}");
            }
            catch (Exception ex)
            {
                try
                {
                    WriteText(context, 500, "application/json",
                        "{\"error\":\"Internal server error: " + EscapeJson(ex.Message) + "\"}");
                }
                catch (Exception) { }
            }
        }

        private static void HandleMcpPost(HttpListenerContext context)
        {
            var request = context.Request;
            if (request.ContentLength64 > MaxRequestBytes)
            {
                WriteText(context, 413, "application/json", "{\"error\":\"Request too large.\"}");
                return;
            }
            if (!IsAllowedOrigin(request.Headers["Origin"]))
            {
                WriteText(context, 403, "application/json",
                    "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32000,\"message\":\"Forbidden: cross-origin POST rejected.\"}}");
                return;
            }
            string body = ReadBody(request.InputStream);
            if (body == null)
            {
                WriteText(context, 413, "application/json", "{\"error\":\"Request too large.\"}");
                return;
            }
            if (string.IsNullOrWhiteSpace(body))
            {
                WriteText(context, 400, "application/json", "{\"error\":\"Empty request body.\"}");
                return;
            }

            var headers = new McpRequestHeaders(
                request.Headers["MCP-Protocol-Version"],
                request.Headers["Mcp-Method"],
                request.Headers["Mcp-Name"]);

            IMcpLocaleIO io;
            lock (StateLock)
            {
                io = _io;
            }
            string responseJson;
            int httpStatus;
            try
            {
                responseJson = McpProtocol.HandleRequest(body, io, headers, out httpStatus);
            }
            catch (Exception ex)
            {
                WriteText(context, 500, "application/json",
                    "{\"error\":\"Dispatch failed: " + EscapeJson(ex.Message) + "\"}");
                return;
            }
            if (responseJson == null)
            {
                WriteEmpty(context, 202);
                return;
            }
            WriteText(context, httpStatus, "application/json", responseJson);
        }

        /// <summary>Reads a UTF-8 body, or returns null when it exceeds <see cref="MaxRequestBytes"/> (covers chunked requests).</summary>
        private static string ReadBody(Stream input)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int read;
                while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxRequestBytes)
                        return null;
                    buffer.Write(chunk, 0, read);
                }
                byte[] bytes = buffer.GetBuffer();
                int length = (int)buffer.Length;
                int start = length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return Encoding.UTF8.GetString(bytes, start, length - start);
            }
        }

        private static bool IsAllowedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return true;
            try
            {
                var uri = new Uri(origin);
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (IPAddress.TryParse(uri.Host, out var address))
                    return IPAddress.IsLoopback(address);
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        private static void WriteText(HttpListenerContext context, int status, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var response = context.Response;
            AddCorsHeaders(response);
            response.StatusCode = status;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private static void WriteEmpty(HttpListenerContext context, int status)
        {
            var response = context.Response;
            AddCorsHeaders(response);
            response.StatusCode = status;
            response.ContentLength64 = 0;
            response.OutputStream.Close();
        }

        private static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Mcp-Session-Id, MCP-Protocol-Version");
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }

        private static string StatusPage()
        {
            int port;
            bool running;
            lock (StateLock)
            {
                port = _port;
                running = _listener != null && _listener.IsListening;
            }
            return "<!doctype html><html><body style='font-family:sans-serif'>" +
                "<h1>PicoShot Localization MCP</h1>" +
                "<p>Status: " + (running ? "running" : "stopped") + "</p>" +
                "<p>Endpoint: <code>http://127.0.0.1:" + port + "/mcp</code></p>" +
                "<p>Health: <code>http://127.0.0.1:" + port + "/health</code></p>" +
                "<p>Manage from Unity: Tools &gt; Localization &gt; MCP Server</p>" +
                "</body></html>";
        }

        #endregion

        #region Main-thread bridge

        private static void DrainMainThreadQueue()
        {
            if (_externalChangePending)
            {
                _externalChangePending = false;
                LocaleHashSync.SyncIfEnabled("MCP edit");
                if (LocalizationManager.IsInitialized)
                    LocalizationManager.Reload();
                try { ExternalChanged?.Invoke(); }
                catch (Exception ex) { Debug.LogError("[Localization MCP] ExternalChanged handler failed: " + ex.Message); }
            }
        }

        /// <summary>Marks locale files as externally changed; delivered on the main thread.</summary>
        internal static void NotifyExternalChange()
        {
            _externalChangePending = true;
        }

        /// <summary>IMcpLocaleIO decorator that flags external changes on write.</summary>
        private sealed class NotifyingIO : IMcpLocaleIO
        {
            private readonly IMcpLocaleIO _inner;

            public NotifyingIO(IMcpLocaleIO inner)
            {
                _inner = inner;
            }

            public string DefaultLanguage => _inner.DefaultLanguage;

            public McpLocaleSnapshot Load() => _inner.Load();

            public string NormalizeLanguage(string languageCode) => _inner.NormalizeLanguage(languageCode);

            public string GetLanguageName(string languageCode) => _inner.GetLanguageName(languageCode);

            public bool IsRightToLeft(string languageCode) => _inner.IsRightToLeft(languageCode);

            public void SaveLanguage(string languageCode, Dictionary<string, object> keys)
            {
                _inner.SaveLanguage(languageCode, keys);
                NotifyExternalChange();
            }

            public void DeleteLanguage(string languageCode)
            {
                _inner.DeleteLanguage(languageCode);
                NotifyExternalChange();
            }
        }

        #endregion
    }
}
