using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MaterialBrowser
{
    public class HttpResult
    {
        public bool Ok;
        public string Url = "";             // final URL after redirects
        public string Status = "";
        public int StatusCode;
        public string ContentType = "";
        public string Body = "";
        public byte[] Bytes;
        public string Error = "";
        public long ElapsedMs;
        public long Length;

        public bool IsHtml
        {
            get
            {
                string type = ContentType.ToLowerInvariant();
                return type.Contains("html") || type.Contains("xml") || type.Length == 0;
            }
        }
    }

    /// <summary>
    /// Thin HTTP layer on HttpWebRequest: modern TLS, transparent gzip, redirect
    /// following, per-session cookies and charset sniffing from the meta tag.
    /// </summary>
    public static class Http
    {
        static readonly object _tlsLock = new object();
        static bool _tlsReady;

        public const long MaxDocumentBytes = 6L * 1024 * 1024;
        public const long MaxImageBytes = 4L * 1024 * 1024;
        public static int TimeoutMs = 25000;

        public static void Prepare()
        {
            lock (_tlsLock)
            {
                if (_tlsReady) return;
                try
                {
                    // .NET 4 defaults to SSL3/TLS1.0 on many machines; force TLS 1.2.
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                }
                catch { }
                try { ServicePointManager.DefaultConnectionLimit = 16; } catch { }
                try { ServicePointManager.Expect100Continue = false; } catch { }
                try
                {
                    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                }
                catch { }
                _tlsReady = true;
            }
        }

        public static string UserAgent
        {
            get
            {
                string custom = Settings.UserAgent;
                if (!string.IsNullOrEmpty(custom)) return custom;
                return Settings.UserAgents[1];
            }
        }

        /// <summary>Loads a document. Returns null and sets <paramref name="error"/> on failure.</summary>
        public static HttpResult Fetch(string url, string referer, CookieContainer cookies, out string error)
        {
            Prepare();
            error = "";
            var result = new HttpResult();
            result.Url = url;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                ReadFile(url, result);
                result.ElapsedMs = watch.ElapsedMilliseconds;
                return result;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = UserAgent;
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 10;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                request.Headers["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7";
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                request.KeepAlive = true;
                if (!string.IsNullOrEmpty(referer)) request.Referer = referer;
                if (cookies != null) request.CookieContainer = cookies;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    result.StatusCode = (int)response.StatusCode;
                    result.Status = response.StatusCode + " " + response.StatusDescription;
                    result.Url = response.ResponseUri != null ? response.ResponseUri.AbsoluteUri : url;
                    result.ContentType = response.ContentType ?? "";
                    result.Ok = true;
                    ReadBody(response, result);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    result.StatusCode = (int)response.StatusCode;
                    result.Status = response.StatusCode + " " + response.StatusDescription;
                    result.ContentType = response.ContentType ?? "";
                    result.Ok = result.StatusCode < 400;
                    error = Describe((int)response.StatusCode);
                    try { ReadBody(response, result); } catch { }
                }
                else
                {
                    result.Ok = false;
                    error = DescribeException(ex);
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                error = ex.Message;
            }

            result.ElapsedMs = watch.ElapsedMilliseconds;
            if (!result.Ok && error.Length > 0) result.Error = error;
            return result;
        }

        static void ReadFile(string url, HttpResult result)
        {
            try
            {
                string path = ToLocalPath(url);
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var sb = new StringBuilder();
                    sb.Append("<!doctype html><meta charset=\"utf-8\"><title>").Append(Path.GetFileName(path)).Append("</title>");
                    sb.Append("<body style=\"font-family:Segoe UI,Arial\"><h1>").Append(Path.GetFileName(path)).Append("</h1>");
                    sb.Append("<p>Локальная папка:</p><ul>");
                    foreach (string file in Directory.GetFiles(path))
                    {
                        string name = Path.GetFileName(file);
                        long size = 0;
                        try { size = new FileInfo(file).Length; } catch { }
                        sb.Append("<li><a href=\"").Append(Escape("file:///" + file.Replace('\\', '/'))).Append("\">")
                          .Append(Escape(name)).Append("</a> <span style=\"color:#888\">")
                          .Append(Escape(Format.Bytes(size))).Append("</span></li>");
                    }
                    foreach (string dir in Directory.GetDirectories(path))
                    {
                        string name = Path.GetFileName(dir);
                        sb.Append("<li><a href=\"").Append(Escape("file:///" + dir.Replace('\\', '/'))).Append("/\">")
                          .Append(Escape(name)).Append("/</a></li>");
                    }
                    sb.Append("</ul></body>");
                    result.Ok = true;
                    result.Body = sb.ToString();
                    result.ContentType = "text/html";
                    result.Status = "200 OK";
                    result.StatusCode = 200;
                    result.Length = result.Body.Length;
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                result.Ok = true;
                result.Bytes = bytes;
                result.Length = bytes.Length;
                result.ContentType = GuessContentType(path);
                result.Status = "200 OK";
                result.StatusCode = 200;
                if (result.IsHtml || result.ContentType.StartsWith("text/")) result.Body = Decode(bytes, null);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }
        }

        static void ReadBody(HttpWebResponse response, HttpResult result)
        {
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null) { result.Body = ""; return; }

                long read;
                byte[] raw = ReadLimited(stream, MaxDocumentBytes, out read);
                result.Length = (int)read;
                string charset = CharsetFrom(response.ContentType);
                if (charset == null) charset = CharsetFromMeta(raw);
                result.Body = Decode(raw, charset);

                if (result.IsHtml && result.ContentType.Length == 0) result.ContentType = "text/html; charset=" + (charset ?? "utf-8");
            }
        }

        public static byte[] ReadLimited(Stream stream, long limit, out long total)
        {
            var buffer = new byte[64 * 1024];
            var memory = new MemoryStream();
            total = 0;
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                total += read;
                memory.Write(buffer, 0, read);
                if (total > limit) break;
            }
            return memory.ToArray();
        }

        public static string Decode(byte[] bytes, string charset)
        {
            if (bytes == null) return "";
            try
            {
                Encoding encoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(charset))
                {
                    try { encoding = Encoding.GetEncoding(charset.Trim().Trim('"')); } catch { encoding = Encoding.UTF8; }
                }
                // UTF-8 is a superset of ASCII; fall back to Windows-1251 when it is not valid UTF-8.
                if (encoding == Encoding.UTF8 && !IsValidUtf8(bytes)) encoding = Encoding.GetEncoding(1251);
                return encoding.GetString(bytes);
            }
            catch { return Encoding.Default.GetString(bytes); }
        }

        static bool IsValidUtf8(byte[] bytes)
        {
            int limit = Math.Min(bytes.Length, 4096);
            int i = 0;
            while (i < limit)
            {
                byte b = bytes[i];
                if (b < 0x80) { i++; continue; }
                int extra;
                if ((b & 0xE0) == 0xC0) extra = 1;
                else if ((b & 0xF0) == 0xE0) extra = 2;
                else if ((b & 0xF8) == 0xF0) extra = 3;
                else return false;
                if (i + extra >= bytes.Length) return true;   // truncated tail, assume fine
                for (int k = 1; k <= extra; k++)
                {
                    byte c = bytes[i + k];
                    if ((c & 0xC0) != 0x80) return false;
                }
                i += extra + 1;
            }
            return true;
        }

        public static string CharsetFrom(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return null;
            int index = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            string value = contentType.Substring(index + 8).Trim();
            int end = value.IndexOf(';');
            if (end >= 0) value = value.Substring(0, end);
            return value.Trim().Trim('"').Trim('\'');
        }

        /// <summary>Sniffs &lt;meta charset&gt; / &lt;meta http-equiv content-type&gt; from the head of a document.</summary>
        public static string CharsetFromMeta(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 20) return null;
            int limit = Math.Min(bytes.Length, 4096);
            string head = Encoding.ASCII.GetString(bytes, 0, limit);

            int index = head.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < 2048)
            {
                string value = head.Substring(index + 8, Math.Min(40, head.Length - index - 8));
                int end = 0;
                while (end < value.Length && (char.IsLetterOrDigit(value[end]) || value[end] == '-' || value[end] == '_')) end++;
                if (end > 1) return value.Substring(0, end);
            }
            return null;
        }

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public static string GuessContentType(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".html": case ".htm": return "text/html";
                case ".txt": return "text/plain";
                case ".css": return "text/css";
                case ".js": return "application/javascript";
                case ".json": return "application/json";
                case ".xml": return "application/xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                default: return "application/octet-stream";
            }
        }

        public static string Describe(int status)
        {
            switch (status)
            {
                case 400: return "Сервер не понял запрос";
                case 401: return "Требуется вход";
                case 403: return "Доступ запрещён";
                case 404: return "Страница не найдена";
                case 408: return "Превышено время ожидания";
                case 429: return "Слишком много запросов";
                case 500: return "Ошибка сервера";
                case 502: return "Шлюз вернул ошибку";
                case 503: return "Сервис временно недоступен";
                case 504: return "Шлюз не ответил вовремя";
                default: return "HTTP " + status;
            }
        }

        static string DescribeException(Exception ex)
        {
            WebException wex = ex as WebException;
            if (wex != null)
            {
                switch (wex.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure:
                        return "Не удалось найти сервер: проверьте подключение к интернету";
                    case WebExceptionStatus.ConnectFailure:
                        return "Не удалось подключиться к серверу";
                    case WebExceptionStatus.Timeout:
                        return "Превышено время ожидания ответа";
                    case WebExceptionStatus.TrustFailure:
                        return "Ошибка сертификата сервера";
                    case WebExceptionStatus.ProxyNameResolutionFailure:
                        return "Не удалось подключиться к прокси";
                    case WebExceptionStatus.SecureChannelFailure:
                        return "Ошибка защищённого соединения";
                }
            }
            return ex.Message;
        }

        public static string ToLocalPath(string fileUrl)
        {
            string url = fileUrl;
            if (url.StartsWith("file:///")) url = url.Substring(8);
            else if (url.StartsWith("file://")) url = url.Substring(7);
            url = Uri.UnescapeDataString(url);
            if (url.Length > 2 && url[0] == '/' && url[2] == ':') url = url.Substring(1);
            return url.Replace('/', '\\');
        }

        public static string ToFileUrl(string path)
        {
            string full = Path.GetFullPath(path);
            return "file:///" + full.Replace('\\', '/');
        }

        // ─── downloads ────────────────────────────────────────────
        public static void Download(string url, DownloadItem item, CookieContainer cookies)
        {
            Prepare();
            try
            {
                if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(ToLocalPath(url), item.Path, true);
                    item.Bytes = new FileInfo(item.Path).Length;
                    item.Total = item.Bytes;
                    item.Done = true;
                    item.Status = "завершено";
                    Downloads.Update(item, true);
                    return;
                }

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = UserAgent;
                request.Timeout = TimeoutMs;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 10;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (cookies != null) request.CookieContainer = cookies;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    item.Total = response.ContentLength;
                    item.Status = "загрузка";

                    string disposition = response.Headers["Content-Disposition"];
                    if (!string.IsNullOrEmpty(disposition))
                    {
                        int index = disposition.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            string name = disposition.Substring(index + 9).Trim().Trim('"');
                            int cut = name.IndexOf(";");
                            if (cut >= 0) name = name.Substring(0, cut);
                            item.FileName = Downloads.Sanitize(name);
                            item.Path = Downloads.Unique(Path.Combine(SettingsFile.DownloadFolder, item.FileName));
                        }
                    }

                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(item.Path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
                    {
                        var buffer = new byte[64 * 1024];
                        while (true)
                        {
                            if (item.Cancel)
                            {
                                item.Failed = true;
                                item.Status = "отменено";
                                break;
                            }
                            int read = input.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;
                            output.Write(buffer, 0, read);
                            item.Bytes += read;
                            Downloads.Raise();
                        }
                    }

                    if (!item.Failed)
                    {
                        item.Done = true;
                        item.Status = "завершено";
                        if (item.Total <= 0) item.Total = item.Bytes;
                    }
                }
            }
            catch (Exception ex)
            {
                item.Failed = true;
                item.Status = "ошибка: " + ex.Message;
                try { if (File.Exists(item.Path)) File.Delete(item.Path); } catch { }
            }
            Downloads.Update(item, true);
        }
    }
}
