using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System;
using System.Collections;
using System.Security.Cryptography;
namespace WebSocketServer
{
    public class RDPClientSocketConnection
    {
        private Logger logger;

        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        private Boolean isDataMasked;
        public Boolean IsDataMasked
        {
            get { return isDataMasked; }
            set { isDataMasked = value; }
        }

        public Socket ConnectionSocket;

        private int MaxBufferSize;
        private string Handshake;
        private string New_Handshake;

        public byte[] receivedDataBuffer;
        private byte[] FirstByte;
        private byte[] LastByte;
        private byte[] ServerKey1;
        private byte[] ServerKey2;

        public string RDPServerAddr;
        public int RDPServerPort;
        public event ClientDataReceivedEventHandler ClientDataReceived;
        public event ClientDisconnectedEventHandler ClientDisconnected;

        public RDPServerSocketConnection rdpServer;

        public RDPClientSocketConnection()
        {
            logger = new Logger();
            MaxBufferSize = 1024*100;
            receivedDataBuffer = new byte[MaxBufferSize];
            FirstByte = new byte[MaxBufferSize];
            LastByte = new byte[MaxBufferSize];
            FirstByte[0] = 0x00;
            LastByte[0] = 0xFF;

            Handshake = "HTTP/1.1 101 Web Socket Protocol Handshake" + Environment.NewLine;
            Handshake += "Upgrade: WebSocket" + Environment.NewLine;
            Handshake += "Connection: Upgrade" + Environment.NewLine;
            Handshake += "Sec-WebSocket-Origin: " + "{0}" + Environment.NewLine;
            Handshake += string.Format("Sec-WebSocket-Location: " + "ws://{0}:4141/chat" + Environment.NewLine, WebSocketServer.getLocalmachineIPAddress());
            Handshake += Environment.NewLine;

            New_Handshake = "HTTP/1.1 101 Switching Protocols" + Environment.NewLine;
            New_Handshake += "Upgrade: WebSocket" + Environment.NewLine;
            New_Handshake += "Connection: Upgrade" + Environment.NewLine;
            New_Handshake += "Sec-WebSocket-Accept: {0}" + Environment.NewLine;
            New_Handshake += "Sec-WebSocket-Protocol: binary" + Environment.NewLine;
            New_Handshake += Environment.NewLine;

//             RDPServerAddr = "10.103.62.121";
//             RDPServerPort = 8000;

            RDPServerAddr = "";
            RDPServerPort = 3389;
        }

        private void Read(IAsyncResult status)
        {
            if (!ConnectionSocket.Connected) return;
            string messageReceived = string.Empty;
            DataFrame dr = new DataFrame(receivedDataBuffer, false);

            try
            {
                if (!this.isDataMasked)
                {
                    // Web Socket protocol: messages are sent with 0x00 and 0xFF as padding bytes
                    System.Text.UTF8Encoding decoder = new System.Text.UTF8Encoding();
                    int startIndex = 0;
                    int endIndex = 0;

                    // Search for the start byte
                    while (receivedDataBuffer[startIndex] == FirstByte[0]) startIndex++;
                    // Search for the end byte
                    endIndex = startIndex + 1;
                    while (receivedDataBuffer[endIndex] != LastByte[0] && endIndex != MaxBufferSize - 1) endIndex++;
                    if (endIndex == MaxBufferSize - 1) endIndex = MaxBufferSize;

                    // Get the message
                    messageReceived = decoder.GetString(receivedDataBuffer, startIndex, endIndex - startIndex);
                }
                else
                {
                    messageReceived = dr.Text;
                }

                //parse rdp server address
                if (dr.Content[0] == 0xff && dr.Content[1] == 0xff)
                {
                    //connect rdp server
                    RDPServerAddr = dr.Content[2].ToString() + "." + dr.Content[3].ToString() + "." + dr.Content[4] + "." + dr.Content[5].ToString();
                    rdpServer = new RDPServerSocketConnection(RDPServerAddr, RDPServerPort);
                    rdpServer.ConnectedClient = this;
                    rdpServer.Connect();
                }
                else if (ClientDataReceived != null)
                {
                    ClientDataReceived(this, dr.Content, EventArgs.Empty);
                }
                Array.Clear(receivedDataBuffer, 0, receivedDataBuffer.Length);
                ConnectionSocket.BeginReceive(receivedDataBuffer, 0, receivedDataBuffer.Length, 0, new AsyncCallback(Read), null);

            }
            catch(Exception ex)
            {
                logger.Log(ex.Message);
                logger.Log("Socket connection is about to stop.");
                if (ClientDisconnected != null)
                    ClientDisconnected(this, EventArgs.Empty);
            }
        }

        private void BuildServerPartialKey(int keyNum, string clientKey)
        {
            string partialServerKey = "";
            byte[] currentKey;
            int spacesNum = 0;
            char[] keyChars = clientKey.ToCharArray();
            foreach (char currentChar in keyChars)
            {
                if (char.IsDigit(currentChar)) partialServerKey += currentChar;
                if (char.IsWhiteSpace(currentChar)) spacesNum++;
            }
            try
            {
                currentKey = BitConverter.GetBytes((int)(Int64.Parse(partialServerKey) / spacesNum));
                if (BitConverter.IsLittleEndian) Array.Reverse(currentKey);

                if (keyNum == 1) ServerKey1 = currentKey;
                else ServerKey2 = currentKey;
            }
            catch
            {
                if (ServerKey1 != null) Array.Clear(ServerKey1, 0, ServerKey1.Length);
                if (ServerKey2 != null) Array.Clear(ServerKey2, 0, ServerKey2.Length);
            }
        }

        private byte[] BuildServerFullKey(byte[] last8Bytes)
        {
            byte[] concatenatedKeys = new byte[16];
            Array.Copy(ServerKey1, 0, concatenatedKeys, 0, 4);
            Array.Copy(ServerKey2, 0, concatenatedKeys, 4, 4);
            Array.Copy(last8Bytes, 0, concatenatedKeys, 8, 8);

            // MD5 Hash
            System.Security.Cryptography.MD5 MD5Service = System.Security.Cryptography.MD5.Create();
            return MD5Service.ComputeHash(concatenatedKeys);
        }

        public void ManageHandshake(IAsyncResult status)
        {
            string header = "Sec-WebSocket-Version:";
            int HandshakeLength = (int)status.AsyncState;
            byte[] last8Bytes = new byte[8];

            System.Text.UTF8Encoding decoder = new System.Text.UTF8Encoding();
            String rawClientHandshake = decoder.GetString(receivedDataBuffer, 0, HandshakeLength);

            Array.Copy(receivedDataBuffer, HandshakeLength - 8, last8Bytes, 0, 8);

            if (rawClientHandshake.IndexOf(header) != -1)
            {
                this.isDataMasked = true;
                string[] rawClientHandshakeLines = rawClientHandshake.Split(new string[] { Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries);
                string acceptKey = "";
                foreach (string Line in rawClientHandshakeLines)
                {
                    Console.WriteLine(Line);
                    if (Line.Contains("Sec-WebSocket-Key:"))
                    {
                        acceptKey = ComputeWebSocketHandshakeSecurityHash09(Line.Substring(Line.IndexOf(":") + 2));
                    }
                }

                New_Handshake = string.Format(New_Handshake, acceptKey);
                byte[] newHandshakeText = Encoding.UTF8.GetBytes(New_Handshake);
                ConnectionSocket.BeginSend(newHandshakeText, 0, newHandshakeText.Length, 0, HandshakeFinished, null);
                return;
            }

            string ClientHandshake = decoder.GetString(receivedDataBuffer, 0, HandshakeLength - 8);

            string[] ClientHandshakeLines = ClientHandshake.Split(new string[] { Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries);

            logger.Log("A new connection" + ConnectionSocket.LocalEndPoint + "Connecting...");

            // Welcome the new client
            foreach (string Line in ClientHandshakeLines)
            {
                logger.Log(Line);
                if (Line.Contains("Sec-WebSocket-Key1:"))
                    BuildServerPartialKey(1, Line.Substring(Line.IndexOf(":") + 2));
                if (Line.Contains("Sec-WebSocket-Key2:"))
                    BuildServerPartialKey(2, Line.Substring(Line.IndexOf(":") + 2));
                if (Line.Contains("Origin:"))
                    try
                    {
                        Handshake = string.Format(Handshake, Line.Substring(Line.IndexOf(":") + 2));
                    }
                    catch
                    {
                        Handshake = string.Format(Handshake, "null");
                    }
            }
            // Build the response for the client
            byte[] HandshakeText = Encoding.UTF8.GetBytes(Handshake);
            byte[] serverHandshakeResponse = new byte[HandshakeText.Length + 16];
            byte[] serverKey = BuildServerFullKey(last8Bytes);
            Array.Copy(HandshakeText, serverHandshakeResponse, HandshakeText.Length);
            Array.Copy(serverKey, 0, serverHandshakeResponse, HandshakeText.Length, 16);

            logger.Log("Sending handshake...");
            ConnectionSocket.BeginSend(serverHandshakeResponse, 0, HandshakeText.Length + 16, 0, HandshakeFinished, null);
            logger.Log(Handshake);
        }

        public static String ComputeWebSocketHandshakeSecurityHash09(String secWebSocketKey)
        {
            const String MagicKEY = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            String secWebSocketAccept = String.Empty;
            // 1. Combine the request Sec-WebSocket-Key with magic key.
            String ret = secWebSocketKey + MagicKEY;
            // 2. Compute the SHA1 hash
            SHA1 sha = new SHA1CryptoServiceProvider();
            byte[] sha1Hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ret));
            // 3. Base64 encode the hash
            secWebSocketAccept = Convert.ToBase64String(sha1Hash);
            return secWebSocketAccept;
        }

        private void HandshakeFinished(IAsyncResult status)
        {
            ClientDataReceived += new ClientDataReceivedEventHandler(ForwardMessageFromC2S);

            ConnectionSocket.EndSend(status);
            try
            {
                ConnectionSocket.BeginReceive(receivedDataBuffer, 0, receivedDataBuffer.Length, 0, new AsyncCallback(Read), null);
            }
            catch (System.Exception ex)
            {
                logger.Log(ex.Message);
                return;
            }
        }

        void ForwardMessageFromC2S(Object sender, byte[] message, EventArgs e)
        {
            RDPClientSocketConnection sConn = sender as RDPClientSocketConnection;

            try
            {
                if(sConn.rdpServer != null)
                    sConn.rdpServer.ConnectionSocket.Send(message);
            }
            catch (System.Exception ex)
            {
                logger.Log(ex.Message);
            }
        }
    }
}