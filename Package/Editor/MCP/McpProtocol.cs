using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// MCP Streamable-HTTP (stateless) JSON-RPC dispatcher.
    /// </summary>
    public static class McpProtocol
    {
        public const string ServerName = "picoshot-localization";
        public const string ServerVersion = "2.2.0";

        private static readonly HashSet<string> KnownProtocolVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            "2024-11-05",
            "2025-03-26",
            "2025-06-18",
            "2025-11-25",
        };

        private const string LatestProtocolVersion = "2025-11-25";

        /// <summary>
        /// Handles a single JSON-RPC message or a batch array.
        /// Returns null when there is nothing to respond with (notification only).
        /// </summary>
        public static string HandleRequest(string requestJson, IMcpLocaleIO io)
        {
            object parsed;
            try
            {
                parsed = McpJson.Parse(requestJson);
            }
            catch (FormatException ex)
            {
                return McpJson.Serialize(ParseError(null, "Invalid JSON: " + ex.Message));
            }

            var store = new McpLocalesStore(io);
            if (parsed is List<object> batch)
            {
                var responses = new List<object>();
                foreach (object item in batch)
                {
                    var response = Dispatch(item, store);
                    if (response != null) responses.Add(response);
                }
                if (responses.Count == 0) return null;
                return McpJson.Serialize(responses);
            }

            var single = Dispatch(parsed, store);
            if (single == null) return null;
            return McpJson.Serialize(single);
        }

        private static Dictionary<string, object> Dispatch(object message, McpLocalesStore store)
        {
            if (!(message is Dictionary<string, object> req))
                return Error(null, -32600, "Invalid Request: expected a JSON object.");

            bool hasId = req.TryGetValue("id", out object id);
            req.TryGetValue("method", out object methodRaw);
            string method = methodRaw as string;

            if (!hasId)
                return null;
            if (string.IsNullOrEmpty(method))
                return Error(id, -32600, "Invalid Request: missing 'method'.");

            Dictionary<string, object> parameters = null;
            if (req.TryGetValue("params", out object paramsRaw))
            {
                if (paramsRaw is Dictionary<string, object> p) parameters = p;
                else return Error(id, -32602, "Invalid params: expected a JSON object.");
            }
            if (parameters == null)
                parameters = new Dictionary<string, object>(StringComparer.Ordinal);

            try
            {
                switch (method)
                {
                    case "initialize":
                        return Result(id, Initialize(parameters));
                    case "ping":
                        return Result(id, new Dictionary<string, object>(StringComparer.Ordinal));
                    case "tools/list":
                        return Result(id, new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["tools"] = McpTools.GetToolDefinitions(),
                        });
                    case "tools/call":
                        {
                            string toolName = McpJson.GetString(parameters, "name");
                            if (string.IsNullOrEmpty(toolName))
                                return Error(id, -32602, "Invalid params: missing 'name'.");
                            Dictionary<string, object> toolArgs;
                            if (parameters.TryGetValue("arguments", out object a) && a is Dictionary<string, object> ad)
                                toolArgs = ad;
                            else
                                toolArgs = new Dictionary<string, object>(StringComparer.Ordinal);
                            var (isError, result) = McpTools.Execute(toolName, toolArgs, store);
                            var content = new List<object>
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                ["type"] = "text",
                                ["text"] = McpJson.Serialize(result),
                            }
                        };
                            var resultObj = new Dictionary<string, object>(StringComparer.Ordinal) { ["content"] = content };
                            if (isError) resultObj["isError"] = true;
                            return Result(id, resultObj);
                        }
                    case "resources/list":
                        return Result(id, ListResources(store));
                    case "resources/read":
                        {
                            string uri = McpJson.GetString(parameters, "uri");
                            if (string.IsNullOrEmpty(uri))
                                return Error(id, -32602, "Invalid params: missing 'uri'.");
                            var (found, payload) = ReadResource(uri, store);
                            if (!found)
                                return Error(id, -32602, $"Unknown resource '{uri}'.");
                            return Result(id, payload);
                        }
                    default:
                        if (method.StartsWith("notifications/", StringComparison.Ordinal))
                            return null;
                        return Error(id, -32601, $"Method not found: '{method}'.");
                }
            }
            catch (FormatException ex)
            {
                return Error(id, -32602, "Invalid params: " + ex.Message);
            }
            catch (Exception ex)
            {
                return Error(id, -32603, "Internal error: " + ex.Message);
            }
        }

        private static Dictionary<string, object> Initialize(Dictionary<string, object> parameters)
        {
            string requested = McpJson.GetString(parameters, "protocolVersion", string.Empty) ?? string.Empty;
            string negotiated = KnownProtocolVersions.Contains(requested) ? requested : LatestProtocolVersion;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["protocolVersion"] = negotiated,
                ["capabilities"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["tools"] = new Dictionary<string, object>(StringComparer.Ordinal),
                    ["resources"] = new Dictionary<string, object>(StringComparer.Ordinal),
                },
                ["serverInfo"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion,
                },
            };
        }

        private static Dictionary<string, object> ListResources(McpLocalesStore store)
        {
            var resources = new List<object>();
            foreach (string lang in store.ListLanguages())
            {
                resources.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["uri"] = "locales://" + lang,
                    ["name"] = lang,
                    ["mimeType"] = "application/json",
                });
            }
            return new Dictionary<string, object>(StringComparer.Ordinal) { ["resources"] = resources };
        }

        private static (bool found, Dictionary<string, object> payload) ReadResource(string uri, McpLocalesStore store)
        {
            const string prefix = "locales://";
            if (!uri.StartsWith(prefix, StringComparison.Ordinal)) return (false, null);
            string lang = uri.Substring(prefix.Length);
            var all = store.LoadAll();
            string actual = null;
            foreach (var existing in all.Keys)
            {
                if (string.Equals(existing, lang, StringComparison.OrdinalIgnoreCase))
                {
                    actual = existing;
                    break;
                }
            }
            if (actual == null) return (false, null);
            var wire = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in all[actual])
            {
                if (kvp.Value is List<string> list)
                {
                    var wired = new List<object>(list.Count);
                    foreach (string s in list)
                        wired.Add((object)(s ?? string.Empty));
                    wire[kvp.Key] = wired;
                }
                else
                {
                    wire[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                }
            }
            return (true, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["contents"] = new List<object>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["uri"] = "locales://" + actual,
                        ["mimeType"] = "application/json",
                        ["text"] = McpJson.Serialize(wire),
                    }
                },
            });
        }

        private static Dictionary<string, object> Result(object id, object result)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = result,
            };
        }

        private static Dictionary<string, object> Error(object id, int code, string message)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["code"] = (long)code,
                    ["message"] = message,
                },
            };
        }

        private static Dictionary<string, object> ParseError(object id, string message)
        {
            var e = Error(id, -32700, message);
            e["id"] = null;
            return e;
        }
    }
}
