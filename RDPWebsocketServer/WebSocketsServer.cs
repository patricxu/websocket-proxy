using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections;

namespace WebSocketServer
{
    public class Logger
    {
        public bool LogEvents;

        public Logger()
        {
            LogEvents = true;
        }

        public void Log(string Text)
        {
            if (LogEvents) Console.WriteLine(Text);
        }
    }

    public enum ServerStatusLevel { Off, WaitingConnection, ConnectionEstablished };

    public delegate void ClientDataReceivedEventHandler(Object sender, byte[] message, EventArgs e);
    public delegate void ClientDisconnectedEventHandler(Object sender, EventArgs e);

    public delegate void ServerDataReceivedEventHandler(Object sender, byte[] message, EventArgs e);
    public delegate void ServerDisconnectedEventHandler(Object sender, EventArgs e);

    public class WebSocketServer : IDisposable
    {
        private bool AlreadyDisposed;
        private Socket Listener;
        private int ConnectionsQueueLength;
        private int MaxBufferSize;

        private Logger logger;
        private byte[] FirstByte;
        private byte[] LastByte;

        List<RDPClientSocketConnection> ClientConnectionSocketList = new List<RDPClientSocketConnection>();

        public ServerStatusLevel Status;
        public int ServerPort;
        public string ServerLocation;
        public string ConnectionOrigin;
        public bool LogEvents { 
            get { return logger.LogEvents; }
            set { logger.LogEvents = value; }
        }

        private void Initialize()
        {
            AlreadyDisposed = false;
            logger = new Logger();

            Status = ServerStatusLevel.Off;
            ConnectionsQueueLength = 500;
            MaxBufferSize = 1024 * 100;
            FirstByte = new byte[MaxBufferSize];
            LastByte = new byte[MaxBufferSize];
            FirstByte[0] = 0x00;
            LastByte[0] = 0xFF;
            logger.LogEvents = true;
        }

        public WebSocketServer() 
        {
            ServerPort = 4141;
            ServerLocation = string.Format("ws://{0}:4141", getLocalmachineIPAddress());
            Initialize();
        }

        public WebSocketServer(int serverPort, string serverLocation, string connectionOrigin)
        {
            ServerPort = serverPort;
            ConnectionOrigin = connectionOrigin;
            ServerLocation = serverLocation;
            Initialize();
        }


        ~WebSocketServer()
        {
            Close();
        }


        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            if (!AlreadyDisposed)
            {
                AlreadyDisposed = true;
                if (Listener != null) Listener.Close();
                foreach (RDPClientSocketConnection item in ClientConnectionSocketList)
                {
                    item.ConnectionSocket.Close();
                    item.rdpServer.ConnectionSocket.Close();
                }
                ClientConnectionSocketList.Clear();

                GC.SuppressFinalize(this);
            }
        }

        public static  IPAddress getLocalmachineIPAddress()
        {
            string strHostName = Dns.GetHostName();
            IPHostEntry ipEntry = Dns.GetHostEntry(strHostName);

            foreach (IPAddress ip in ipEntry.AddressList)
            {
                //IPV4
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip;
            }

            return ipEntry.AddressList[0];
        }

        public void StartServer()
        {   
            Char char1 = Convert.ToChar(65533);

            Listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);  
            Listener.Bind(new IPEndPoint(getLocalmachineIPAddress(), ServerPort));  
            
            Listener.Listen(ConnectionsQueueLength);  

            logger.Log(string.Format("Proxy Started, listening on {0} with port：{1}",getLocalmachineIPAddress(),ServerPort));

            while (true)
            {
                Socket sc = Listener.Accept();

                if (sc != null)
                {
                    System.Threading.Thread.Sleep(100);
                    RDPClientSocketConnection socketConn = new RDPClientSocketConnection();
                    socketConn.ConnectionSocket = sc;
                    socketConn.ClientDisconnected += new ClientDisconnectedEventHandler(ClientDisconnected);

                    socketConn.ConnectionSocket.BeginReceive(socketConn.receivedDataBuffer,
                                                             0, socketConn.receivedDataBuffer.Length, 
                                                             0, new AsyncCallback(socketConn.ManageHandshake), 
                                                             socketConn.ConnectionSocket.Available);
                    ClientConnectionSocketList.Add(socketConn);
                }
            }
        }

        void ClientDisconnected(Object sender, EventArgs e)
        {
            RDPClientSocketConnection sConn = sender as RDPClientSocketConnection;
            if (sConn != null)
            {
//                 ForwardMessageFromC2S(sender, string.Format("[{0}] logout！", sConn.Name), e);
                sConn.ConnectionSocket.Close();
                if(sConn.rdpServer != null)
                    sConn.rdpServer.ConnectionSocket.Close();
                ClientConnectionSocketList.Remove(sConn);
            }
        }




    }
}



