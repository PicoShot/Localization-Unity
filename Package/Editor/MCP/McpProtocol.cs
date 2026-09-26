using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// HTTP transport headers.
    /// </summary>
    public sealed class McpRequestHeaders
    {
        public readonly string ProtocolVersion;
        public readonly string Method;
        public readonly string Name;

        public McpRequestHeaders(string protocolVersion, string method, string name)
        {
            ProtocolVersion = protocolVersion;
            Method = method;
            Name = name;
        }
    }

    /// <summary>
    /// MCP Streamable-HTTP (stateless) JSON-RPC dispatcher.
    /// </summary>
    public static class McpProtocol
    {
        public const string ServerName = "picoshot-localization";
        public const string ServerVersion = "2.2.1";

        public const string ModernVersion = "2026-07-28";

        private static readonly string[] SupportedVersions = new string[]
        {
            "2026-07-28",
            "2025-11-25",
            "2025-06-18",
            "2025-03-26",
            "2024-11-05",
        };

        private static readonly HashSet<string> KnownProtocolVersions = new HashSet<string>(SupportedVersions, StringComparer.Ordinal);

        private const string LatestProtocolVersion = ModernVersion;

        private const string MetaVersionKey = "io.modelcontextprotocol/protocolVersion";
        private const string MetaServerInfoKey = "io.modelcontextprotocol/serverInfo";

        private const int ParseError = -32700;
        private const int InvalidRequest = -32600;
        private const int MethodNotFound = -32601;
        private const int InvalidParams = -32602;
        private const int InternalError = -32603;
        private const int HeaderMismatch = -32020;
        private const int UnsupportedVersion = -32022;

        /// <summary>
        /// Handles a single JSON-RPC message or a batch array without HTTP
        /// </summary>
        public static string HandleRequest(string requestJson, IMcpLocaleIO io)
        {
            return HandleRequest(requestJson, io, null, out int _);
        }

        /// <summary>
        /// Handles a single JSON-RPC message or a batch array.
        /// </summary>
        public static string HandleRequest(string requestJson, IMcpLocaleIO io, McpRequestHeaders headers, out int httpStatus)
        {
            object parsed;
            try
            {
                parsed = McpJson.Parse(requestJson);
            }
            catch (FormatException ex)
            {
                httpStatus = 200;
                return McpJson.Serialize(ParseErrorResponse(null, "Invalid JSON: " + ex.Message));
            }

            var store = new McpLocalesStore(io);
            if (parsed is List<object> batch)
            {
                if (batch.Count == 0)
                {
                    httpStatus = 200;
                    return McpJson.Serialize(Error(null, InvalidRequest, "Invalid Request: empty batch."));
                }
                var responses = new List<object>(batch.Count);
                int status = 200;
                foreach (object item in batch)
                {
                    var response = Dispatch(item, store, headers, out int itemStatus);
                    if (response == null) continue;
                    responses.Add(response);
                    if (itemStatus == 400) status = 400;
                }
                httpStatus = status;
                if (responses.Count == 0) return null;
                return McpJson.Serialize(responses);
            }

            var single = Dispatch(parsed, store, headers, out httpStatus);
            if (single == null) return null;
            return McpJson.Serialize(single);
        }

        private static Dictionary<string, object> Dispatch(object message, McpLocalesStore store, McpRequestHeaders headers, out int httpStatus)
        {
            httpStatus = 200;
            if (!(message is Dictionary<string, object> req))
                return Error(null, InvalidRequest, "Invalid Request: expected a JSON object.");

            bool hasId = req.TryGetValue("id", out object id);
            req.TryGetValue("method", out object methodRaw);
            string method = methodRaw as string;

            if (!hasId)
            {
                httpStatus = 202;
                return null;
            }
            if (string.IsNullOrEmpty(method))
                return Error(id, InvalidRequest, "Invalid Request: missing 'method'.");

            Dictionary<string, object> parameters = null;
            if (req.TryGetValue("params", out object paramsRaw))
            {
                if (paramsRaw is Dictionary<string, object> p) parameters = p;
                else return Error(id, InvalidParams, "Invalid params: expected a JSON object.");
            }
            parameters ??= new Dictionary<string, object>(StringComparer.Ordinal);

            if (string.Equals(method, "server/discover", StringComparison.Ordinal))
                return Result(id, DiscoverResult());

            string bodyVersion = MetaVersion(parameters);
            string headerVersion = headers != null ? headers.ProtocolVersion : null;

            if (!ResolveEra(id, method, bodyVersion, headerVersion, out bool modern, out Dictionary<string, object> rejection))
            {
                httpStatus = 400;
                return rejection;
            }

            try
            {
                if (modern && (string.Equals(method, "initialize", StringComparison.Ordinal) ||
                               string.Equals(method, "ping", StringComparison.Ordinal)))
                {
                    httpStatus = 404;
                    return Error(id, MethodNotFound, $"Method not found: '{method}'.");
                }

                if (modern)
                {
                    Dictionary<string, object> headerError = ValidateModernHeaders(method, parameters, headers, id);
                    if (headerError != null)
                    {
                        httpStatus = 400;
                        return headerError;
                    }
                }

                switch (method)
                {
                    case "initialize":
                        return Result(id, Initialize(parameters));
                    case "ping":
                        return Result(id, new Dictionary<string, object>(StringComparer.Ordinal));
                    case "tools/list":
                        {
                            var result = new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                ["tools"] = McpTools.GetToolDefinitions(),
                                ["ttlMs"] = 3600000L,
                                ["cacheScope"] = "public",
                            };
                            return Result(id, result);
                        }
                    case "tools/call":
                        {
                            string toolName = McpJson.GetString(parameters, "name");
                            if (string.IsNullOrEmpty(toolName))
                                return Error(id, InvalidParams, "Invalid params: missing 'name'.");
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
                        {
                            var result = ListResources(store);
                            result["ttlMs"] = 60000L;
                            result["cacheScope"] = "private";
                            return Result(id, result);
                        }
                    case "resources/read":
                        {
                            string uri = McpJson.GetString(parameters, "uri");
                            if (string.IsNullOrEmpty(uri))
                                return Error(id, InvalidParams, "Invalid params: missing 'uri'.");
                            var (found, payload) = ReadResource(uri, store);
                            if (!found)
                                return Error(id, InvalidParams, $"Unknown resource '{uri}'.");
                            payload["ttlMs"] = 60000L;
                            payload["cacheScope"] = "private";
                            return Result(id, payload);
                        }
                    default:
                        if (method.StartsWith("notifications/", StringComparison.Ordinal))
                            return null;
                        if (modern) httpStatus = 404;
                        return Error(id, MethodNotFound, $"Method not found: '{method}'.");
                }
            }
            catch (FormatException ex)
            {
                return Error(id, InvalidParams, "Invalid params: " + ex.Message);
            }
            catch (Exception ex)
            {
                return Error(id, InternalError, "Internal error: " + ex.Message);
            }
        }

        /// <summary>
        /// Decides legacy vs modern era and enforces version/header presence.
        /// </summary>
        private static bool ResolveEra(object id, string method, string bodyVersion, string headerVersion, out bool modern, out Dictionary<string, object> rejection)
        {
            modern = false;
            rejection = null;

            bool isHandshake = string.Equals(method, "initialize", StringComparison.Ordinal);
            if (isHandshake)
            {
                if (string.Equals(headerVersion, ModernVersion, StringComparison.Ordinal) ||
                    string.Equals(bodyVersion, ModernVersion, StringComparison.Ordinal))
                {
                    modern = true;
                }
                return true;
            }

            if (headerVersion == null && bodyVersion == null)
                return true;

            if (headerVersion == null)
            {
                if (string.Equals(bodyVersion, ModernVersion, StringComparison.Ordinal))
                {
                    rejection = Error(id, HeaderMismatch, "Header mismatch: missing required 'MCP-Protocol-Version' header.");
                    return false;
                }
                if (bodyVersion != null && !KnownProtocolVersions.Contains(bodyVersion))
                {
                    rejection = UnsupportedVersionError(id, bodyVersion);
                    return false;
                }
                return true;
            }

            if (bodyVersion == null)
            {
                if (string.Equals(headerVersion, ModernVersion, StringComparison.Ordinal))
                {
                    rejection = Error(id, HeaderMismatch, "Header mismatch: 'MCP-Protocol-Version: 2026-07-28' requires matching '_meta'.");
                    return false;
                }
                if (!KnownProtocolVersions.Contains(headerVersion))
                {
                    rejection = UnsupportedVersionError(id, headerVersion);
                    return false;
                }
                return true;
            }

            if (!string.Equals(headerVersion, bodyVersion, StringComparison.Ordinal))
            {
                rejection = Error(id, HeaderMismatch,
                    $"Header mismatch: 'MCP-Protocol-Version: {headerVersion}' does not match body '{bodyVersion}'.");
                return false;
            }
            if (!KnownProtocolVersions.Contains(headerVersion))
            {
                rejection = UnsupportedVersionError(id, headerVersion);
                return false;
            }
            modern = string.Equals(headerVersion, ModernVersion, StringComparison.Ordinal);
            return true;
        }

        private static Dictionary<string, object> ValidateModernHeaders(
            string method, Dictionary<string, object> parameters, McpRequestHeaders headers, object id)
        {
            if (headers == null || headers.Method == null)
                return Error(id, HeaderMismatch, "Header mismatch: missing required 'Mcp-Method' header.");
            if (!string.Equals(headers.Method, method, StringComparison.Ordinal))
                return Error(id, HeaderMismatch,
                    $"Header mismatch: 'Mcp-Method: {headers.Method}' does not match body '{method}'.");
            string routedValue = null;
            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
                routedValue = McpJson.GetString(parameters, "name");
            else if (string.Equals(method, "resources/read", StringComparison.Ordinal))
                routedValue = McpJson.GetString(parameters, "uri");
            if (routedValue != null)
            {
                if (headers.Name == null)
                    return Error(id, HeaderMismatch, $"Header mismatch: missing required 'Mcp-Name' header for '{method}'.");
                if (!string.Equals(headers.Name, routedValue, StringComparison.Ordinal))
                    return Error(id, HeaderMismatch,
                        $"Header mismatch: 'Mcp-Name: {headers.Name}' does not match body '{routedValue}'.");
            }
            return null;
        }

        private static string MetaVersion(Dictionary<string, object> parameters)
        {
            if (parameters.TryGetValue("_meta", out object metaRaw) && metaRaw is Dictionary<string, object> meta)
                return McpJson.GetString(meta, MetaVersionKey);
            return null;
        }

        private static Dictionary<string, object> UnsupportedVersionError(object id, string requested)
        {
            var supported = new List<object>(SupportedVersions.Length);
            foreach (string version in SupportedVersions)
                supported.Add(version);
            return Error(id, UnsupportedVersion, "Unsupported protocol version", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["supported"] = supported,
                ["requested"] = requested,
            });
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

        private static Dictionary<string, object> DiscoverResult()
        {
            var supported = new List<object>(SupportedVersions.Length);
            foreach (string version in SupportedVersions)
                supported.Add(version);
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["supportedVersions"] = supported,
                ["capabilities"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["tools"] = new Dictionary<string, object>(StringComparer.Ordinal),
                    ["resources"] = new Dictionary<string, object>(StringComparer.Ordinal),
                },
                ["instructions"] = "Unity localization server. Use list_languages and validate to find gaps, " +
                    "add_key to create keys (fans out to every language), get_key or locales://{lang} resources to read, " +
                    "and set_translation / set_translations (max 500 items per call) to write translations. " +
                    "Keys are unique case-insensitively; values are strings or string arrays.",
                ["ttlMs"] = 3600000L,
                ["cacheScope"] = "public",
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
                        wired.Add(s ?? string.Empty);
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
            var resultObj = result as Dictionary<string, object>;
            if (resultObj == null)
            {
                resultObj = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["value"] = result,
                };
            }
            resultObj["resultType"] = "complete";
            resultObj["_meta"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [MetaServerInfoKey] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion,
                },
            };
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = resultObj,
            };
        }

        private static Dictionary<string, object> Error(object id, int code, string message)
        {
            return Error(id, code, message, null);
        }

        private static Dictionary<string, object> Error(object id, int code, string message, Dictionary<string, object> data)
        {
            var errorObj = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["code"] = (long)code,
                ["message"] = message,
            };
            if (data != null) errorObj["data"] = data;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = errorObj,
            };
        }

        private static Dictionary<string, object> ParseErrorResponse(object id, string message)
        {
            var e = Error(id, ParseError, message);
            e["id"] = null;
            return e;
        }
    }
}
