using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MaterialBrowser
{
    /// <summary>
    /// Minimal RFC 6455 WebSocket client. Only what the Gecko remote agent
    /// needs: a text channel over TCP, client-side masking and ping handling.
    /// </summary>
    public class WebSocket
    {
        TcpClient _client;
        NetworkStream _stream;
        readonly object _gate = new object();
        volatile bool _closed;

        public event EventHandler<string> TextReceived;
        public event EventHandler Closed;

        static readonly Random Rng = new Random(unchecked(Environment.TickCount * 397) ^ DateTime.Now.Millisecond);

        static byte[] NextBytes(int count)
        {
            var data = new byte[count];
            lock (Rng) Rng.NextBytes(data);
            return data;
        }

        public bool Connected
        {
            get { return !_closed && _client != null && _client.Connected; }
        }

        /// <summary>Performs the HTTP upgrade handshake and starts the read loop.</summary>
        public void Connect(string host, int port, string path, int timeoutMs)
        {
            _client = new TcpClient();
            IAsyncResult ar = _client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                throw new TimeoutException("gecko: connect timeout");
            _client.EndConnect(ar);
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _stream.ReadTimeout = Timeout.Infinite;   // the read loop owns the lifetime
            _stream.WriteTimeout = 5000;

            string key = Convert.ToBase64String(NextBytes(16));

            var request = new StringBuilder();
            request.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(host).Append(":").Append(port).Append("\r\n");
            request.Append("Upgrade: websocket\r\n");
            request.Append("Connection: Upgrade\r\n");
            request.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
            request.Append("Sec-WebSocket-Version: 13\r\n\r\n");
            byte[] header = Encoding.ASCII.GetBytes(request.ToString());
            _stream.Write(header, 0, header.Length);
            _stream.Flush();

            string head = ReadHeader();
            if (head.IndexOf(" 101 ", StringComparison.Ordinal) < 0)
                throw new IOException("gecko: handshake rejected: " + head.Split('\n')[0]);

            var worker = new Thread(ReadLoop);
            worker.IsBackground = true;
            worker.Name = "gecko-socket";
            worker.Start();
        }

        string ReadHeader()
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 8192)
            {
                int read = _stream.Read(one, 0, 1);
                if (read <= 0) break;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 &&
                    sb[sb.Length - 4] == '\r' && sb[sb.Length - 3] == '\n' &&
                    sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n') break;
            }
            return sb.ToString();
        }

        public void SendText(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            SendFrame(0x1, payload);
        }

        void SendFrame(int opcode, byte[] payload)
        {
            lock (_gate)
            {
                if (_closed || _stream == null) return;
                {
                    byte[] mask = NextBytes(4);

                    var head = new List<byte>();
                    head.Add((byte)(0x80 | opcode));
                    int length = payload.Length;
                    if (length < 126) head.Add((byte)(0x80 | length));
                    else if (length < 65536)
                    {
                        head.Add((byte)(0x80 | 126));
                        head.Add((byte)(length >> 8));
                        head.Add((byte)(length & 0xFF));
                    }
                    else
                    {
                        head.Add((byte)(0x80 | 127));
                        for (int shift = 56; shift >= 0; shift -= 8) head.Add((byte)((long)length >> shift));
                    }
                    head.AddRange(mask);

                    byte[] masked = new byte[length];
                    for (int i = 0; i < length; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);

                    _stream.Write(head.ToArray(), 0, head.Count);
                    if (length > 0) _stream.Write(masked, 0, length);
                    _stream.Flush();
                }
            }
        }

        void ReadLoop()
        {
            try
            {
                var buffer = new MemoryStream();
                int messageOpcode = 0;
                while (!_closed)
                {
                    int first = ReadByte();
                    if (first < 0) break;
                    int second = ReadByte();
                    if (second < 0) break;
                    bool fin = (first & 0x80) != 0;
                    int opcode = first & 0x0F;
                    long length = second & 0x7F;
                    if (length == 126)
                    {
                        int high = ReadByte();
                        int low = ReadByte();
                        length = ((long)high << 8) + low;
                    }
                    else if (length == 127)
                    {
                        length = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            int octet = ReadByte();
                            length = (length << 8) + octet;
                        }
                    }
                    if (length > 32 * 1024 * 1024) break;

                    byte[] payload = new byte[length];
                    int filled = 0;
                    while (filled < length)
                    {
                        int read = _stream.Read(payload, filled, (int)length - filled);
                        if (read <= 0) { filled = -1; break; }
                        filled += read;
                    }
                    if (filled < 0) break;

                    if (opcode == 0x8) { SendFrame(0x8, new byte[0]); break; }
                    if (opcode == 0x9) { SendFrame(0xA, payload); continue; }
                    if (opcode == 0xA) continue;

                    if (opcode == 0x0) buffer.Write(payload, 0, payload.Length);
                    else
                    {
                        buffer.SetLength(0);
                        buffer.Write(payload, 0, payload.Length);
                        messageOpcode = opcode;
                    }
                    if (!fin) continue;

                    string text = messageOpcode == 0x2 ? "" : Encoding.UTF8.GetString(buffer.ToArray());
                    buffer.SetLength(0);
                    EventHandler<string> handler = TextReceived;
                    if (handler != null) handler(this, text);
                }
            }
            catch (Exception ex) { Debug.Log("gecko socket: " + ex.Message); }
            finally { Shutdown(); }
        }

        int ReadByte()
        {
            try { return _stream.ReadByte(); }
            catch { return -1; }
        }

        public void Close()
        {
            if (_closed) return;
            try { SendFrame(0x8, new byte[0]); }
            catch { }
            Shutdown();
        }

        void Shutdown()
        {
            if (_closed) return;
            _closed = true;
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            EventHandler handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    /// <summary>One WebDriver BiDi command waiting for its answer.</summary>
    class GeckoReply
    {
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public Dictionary<string, object> Result;
        public string Error;
    }

    /// <summary>
    /// WebDriver BiDi session over a WebSocket. Firefox and its forks expose
    /// this at ws://127.0.0.1:&lt;port&gt;/session once the browser is started
    /// with --remote-debugging-port, which is what gives us navigation,
    /// JavaScript, titles and navigation events without any native library.
    /// </summary>
    public delegate void GeckoEventHandler(string method, Dictionary<string, object> parameters);

    public class GeckoClient
    {
        readonly WebSocket _socket = new WebSocket();
        readonly Dictionary<int, GeckoReply> _pending = new Dictionary<int, GeckoReply>();
        readonly object _gate = new object();
        int _nextId;
        volatile bool _ready;

        public event GeckoEventHandler Event;   // method, params
        public event EventHandler Closed;

        public bool Ready { get { return _ready; } }

        public string BrowserName = "";
        public string BrowserVersion = "";

        public void Connect(int port, int timeoutMs)
        {
            _socket.TextReceived += OnText;
            _socket.Closed += OnClosed;
            if (Debug.Enabled) Debug.Log("gecko bidi: opening ws://127.0.0.1:" + port + "/session");
            _socket.Connect("127.0.0.1", port, "/session", timeoutMs);
            // Firefox allows a single session per instance; ours is the only one.
            // session.new insists on an explicit, empty capabilities object.
            var start = new Dictionary<string, object>();
            start["capabilities"] = new Dictionary<string, object>();
            Dictionary<string, object> answer = Call("session.new", start, 15000);
            if (answer == null)
                throw new IOException("gecko: session.new failed: " + LastError);
            Dictionary<string, object> capabilities = Json.Child(answer, "capabilities");
            BrowserName = Json.Text(capabilities, "browserName");
            BrowserVersion = Json.Text(capabilities, "browserVersion");
            Subscribe();
            _ready = true;
        }

        string LastError = "";
        public string LastErrorPublic { get { return LastError; } }

        /// <summary>
        /// Browsers only push the event families a session asked for. Without this
        /// every command works but no navigation or load notification ever arrives,
        /// so the loading bar would never stop.
        /// </summary>
        static readonly string[] Events =
        {
            "browsingContext.navigationStarted",
            "browsingContext.domContentLoaded",
            "browsingContext.load",
            "browsingContext.contextCreated",
            "browsingContext.contextDestroyed",
            "browsingContext.userPromptOpened",
        };

        void Subscribe()
        {
            var events = new List<object>();
            foreach (string name in Events) events.Add(name);
            var parameters = new Dictionary<string, object>();
            parameters["events"] = events;
            // No "contexts" key: that subscribes to every context, which is what
            // we want. Passing an empty array is an error, not a wildcard.
            Dictionary<string, object> answer = Call("session.subscribe", parameters, 8000);
            if (answer == null)
                Debug.Log("gecko bidi: subscribe failed: " + LastError);
            else if (Debug.Enabled) Debug.Log("gecko bidi: subscribed to " + Events.Length + " event families");
        }

        /// <summary>Sends a command and waits for its answer. Returns null on failure.</summary>
        public Dictionary<string, object> Call(string method, Dictionary<string, object> parameters, int timeoutMs)
        {
            var message = new Dictionary<string, object>();
            int id = Interlocked.Increment(ref _nextId);
            message["id"] = (double)id;
            message["method"] = method;
            message["params"] = parameters ?? new Dictionary<string, object>();

            var reply = new GeckoReply();
            lock (_gate) _pending[id] = reply;
            try { _socket.SendText(Json.Write(message)); }
            catch (Exception ex)
            {
                lock (_gate) _pending.Remove(id);
                LastError = ex.Message;
                return null;
            }

            if (!reply.Done.Wait(timeoutMs))
            {
                lock (_gate) _pending.Remove(id);
                LastError = "timeout";
                return null;
            }
            lock (_gate) _pending.Remove(id);
            if (reply.Error != null) { LastError = reply.Error; return null; }
            return reply.Result;
        }

        /// <summary>Fire-and-forget command; failures are only traced.</summary>
        public void Fire(string method, Dictionary<string, object> parameters)
        {
            var message = new Dictionary<string, object>();
            int id = Interlocked.Increment(ref _nextId);
            message["id"] = (double)id;
            message["method"] = method;
            message["params"] = parameters ?? new Dictionary<string, object>();
            try { _socket.SendText(Json.Write(message)); }
            catch (Exception ex) { Debug.Log("gecko fire " + method + ": " + ex.Message); }
        }

        void OnText(object sender, string text)
        {
            Dictionary<string, object> message = Json.Object(Json.Parse(text));
            if (message == null) return;
            string type = Json.Text(message, "type");

            if (type == "event")
            {
                GeckoEventHandler handler = Event;
                if (handler != null) handler(Json.Text(message, "method"), Json.Child(message, "params"));
                return;
            }

            object rawId;
            if (!message.TryGetValue("id", out rawId)) return;
            int id = (int)Convert.ToDouble(rawId);
            GeckoReply reply;
            lock (_gate)
            {
                if (!_pending.TryGetValue(id, out reply)) return;
            }
            if (type == "error")
            {
                reply.Error = Json.Text(message, "error") + ": " + Json.Text(message, "message");
            }
            else
            {
                reply.Result = Json.Child(message, "result") ?? new Dictionary<string, object>();
            }
            reply.Done.Set();
        }

        void OnClosed(object sender, EventArgs e)
        {
            _ready = false;
            lock (_gate)
            {
                foreach (GeckoReply reply in _pending.Values) reply.Done.Set();
                _pending.Clear();
            }
            EventHandler handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_ready)
            {
                try { Call("session.end", null, 2000); }
                catch { }
                _ready = false;
            }
            _socket.Close();
        }
    }
}