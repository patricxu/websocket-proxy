using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;

namespace WebSocketServer
{
    public class RDPServerSocketConnection
    {
        public string ServerAddr;
        public int ServerPort;
        public Socket ConnectionSocket;

        public byte[] ReceivedDataBuffer;
        private int MaxBufferSize;

        public ServerDataReceivedEventHandler ServerDataReceived;
        public Object ConnectedClient;

        public RDPServerSocketConnection(string Addr, int Port)
        {
            MaxBufferSize = 1024 * 10;
            ReceivedDataBuffer = new byte[MaxBufferSize];
            ServerAddr = Addr;
            ServerPort = Port;
            ConnectionSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            ServerDataReceived = new ServerDataReceivedEventHandler(ForwardMessageFromS2C);

        }

        public void Connect()
        {
            ConnectionSocket.BeginConnect(ServerAddr, ServerPort, new AsyncCallback(CallbackConnect), this);
        }

        private void CallbackConnect(IAsyncResult res)
        {
            if (!ConnectionSocket.Connected)
                return;

            byte[] message = {0xff, 0xfe};
            ForwardMessageFromS2C(ConnectedClient, message, null);

            ConnectionSocket.BeginReceive(ReceivedDataBuffer, 0, ReceivedDataBuffer.Length, 0, new AsyncCallback(CallbackReceive), this);
        }

        private void CallbackReceive(IAsyncResult res)
        {
            RDPClientSocketConnection ClientConnection = ConnectedClient as RDPClientSocketConnection;

            int read = -1;
            byte[] tmp;
            try
            {
                read = ConnectionSocket.EndReceive(res);
                tmp = new byte[read];
                for (int i = 0; i < read; ++i)
                    tmp[i] = ReceivedDataBuffer[i];

                //Forward the package to the client
                if (ServerDataReceived != null && read != -1)
                    ServerDataReceived(ConnectedClient, tmp, null);
            }
            catch (System.Exception ex)
            {
            	
            }

            try
            {
                Array.Clear(ReceivedDataBuffer, 0, ReceivedDataBuffer.Length);
                ConnectionSocket.BeginReceive(ReceivedDataBuffer, 0, ReceivedDataBuffer.Length, 0, new AsyncCallback(CallbackReceive), this);
            }
            catch (Exception ex)
            {
                string ExceptionType = ex.Message;
                return;
            }
        }

        void ForwardMessageFromS2C(Object sender, byte[] message, EventArgs e)
        {
            RDPClientSocketConnection sConn = sender as RDPClientSocketConnection;
            try
            {
                //                 if (sConn.IsDataMasked)
                //                 {
                DataFrame dr = new DataFrame(message, true);
                sConn.ConnectionSocket.Send(dr.GetBytes());
                //                 }
                //                 else
                //                 {
                //                     sConn.ConnectionSocket.Send(FirstByte);
                //                     sConn.ConnectionSocket.Send(Encoding.UTF8.GetBytes(message));
                //                     sConn.ConnectionSocket.Send(LastByte);
                //                 }
            }
            catch (System.Exception ex)
            {
            }
        }
    }
}
