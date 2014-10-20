using System;
using System.IO;
using System.Timers;
namespace WebSocketServer
{
    class WebSocketServerTest : IDisposable
    {
        private WebSocketServer WSServer;
        public WebSocketServerTest()
        {
            WSServer = new WebSocketServer();  
        }

        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            WSServer.Dispose();
            GC.SuppressFinalize(this);
        }

        ~WebSocketServerTest()
        {
            Close();
        }

        public void Start()
        {
            WSServer.StartServer();
        }
}
}
