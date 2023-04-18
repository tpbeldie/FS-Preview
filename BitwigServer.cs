using System.IO;
using System.Net;
using System.Text;

namespace FSPreview
{
    public class BitwigServer
    {

        private readonly HttpListener m_listener;

        public event EventHandler<BitwigReply> ResponseReceived;

        private bool m_running = true;

        public BitwigServer() {
            try {
                m_listener = new HttpListener();
                m_listener.Prefixes.Add("http://localhost:8080/");
                m_listener.Start();
            } catch { MessageBox.Show("Only one instance supported!"); }
        }

        public void Listen() {
            Task.Run(() => {
                while (m_running) {
                    HttpListenerContext context = m_listener.GetContext();
                    HttpListenerRequest request = context.Request;
                    if (request.HttpMethod == "POST") {
                        using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding)) {
                            string bodyReply = reader.ReadToEnd();
                            BitwigReply reply = GetReply(bodyReply);
                            OnResponseReceived(reply);
                        }
                    }
                    HttpListenerResponse response = context.Response;
                    byte[] buffer = Encoding.UTF8.GetBytes("OK");
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.Close();
                }
            });
        }

        public void Stop() {
            m_running = false;
        }

        public virtual void OnResponseReceived(BitwigReply r) {
            ResponseReceived?.Invoke(this, r);
        }

        const string Tempo_Key = "Tempo:";

        const string Transport_Start = "Transport-Start:";

        const string Transport_Position = "Transport-Position:";

        const string Play_State = "Play-State:";

        private Dictionary<BitwigChangeNotifyType, string> ReplyParameters = new Dictionary<BitwigChangeNotifyType, string>
        {
            { BitwigChangeNotifyType.Tempo, Tempo_Key },
            { BitwigChangeNotifyType.TransportStart, Transport_Start },
            { BitwigChangeNotifyType.TransportPosition, Transport_Position },
            { BitwigChangeNotifyType.PlayState, Play_State },
        };

        private BitwigReply GetReply(string bodyReply) {
            if (bodyReply.StartsWith(Tempo_Key)) {
                return new BitwigReply() { MessageType = BitwigChangeNotifyType.Tempo, Message = double.Parse(bodyReply.Remove(0, ReplyParameters[BitwigChangeNotifyType.Tempo].Length)) };
            }
            if (bodyReply.StartsWith(Transport_Start)) {
                return new BitwigReply() { MessageType = BitwigChangeNotifyType.TransportStart, Message = double.Parse(bodyReply.Remove(0, ReplyParameters[BitwigChangeNotifyType.TransportStart].Length)) };
            }
            if (bodyReply.StartsWith(Transport_Position)) {
                return new BitwigReply() { MessageType = BitwigChangeNotifyType.TransportPosition, Message = double.Parse(bodyReply.Remove(0, ReplyParameters[BitwigChangeNotifyType.TransportPosition].Length)) };
            }
            if (bodyReply.StartsWith(Play_State)) {
                return new BitwigReply() { MessageType = BitwigChangeNotifyType.PlayState, Message = double.Parse(bodyReply.Remove(0, ReplyParameters[BitwigChangeNotifyType.PlayState].Length)) };
            }
            return default;
        }
    }

    public enum BitwigChangeNotifyType
    {
        // The tempo has changed.
        Tempo,
        // The playstate has changed from stop/paused to playing and vice versa.
        PlayState,
        // The playback start time has been changed.
        TransportStart,
        // The transport position has been changed.
        TransportPosition
    }

    public struct BitwigReply
    {
        public BitwigChangeNotifyType MessageType;
        public double Message;
    }
}