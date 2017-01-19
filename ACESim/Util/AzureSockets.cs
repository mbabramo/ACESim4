using Microsoft.WindowsAzure.ServiceRuntime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Concurrent;

namespace ACESim
{
    public static class AzureSockets
    {
        public static bool ServerIsInitialized = false;
        public static ConcurrentDictionary<string, byte[]> HostedItems = new ConcurrentDictionary<string, byte[]>();
        public static IPEndPoint ServerAddress;

        public static void ClearHostedItems()
        {
            var existingItems = HostedItems.ToList();
            foreach (var item in existingItems)
            {
                byte[] outVal = null;
                HostedItems.TryRemove(item.Key, out outVal);
            }
        }

        public static void StartServer(string endPointName)
        {
            if (ServerIsInitialized)
                return;
            ServerIsInitialized = true;
            ServerAddress = GetIPEndPoint(endPointName);
            Thread newThread = new Thread(new ThreadStart(StartServerHelper));
            newThread.Start();
        }

        public static object ClientGetItem(IPEndPoint remoteEP, string itemID, int numBytes)
        {
            Socket sender = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // Connect the socket to the remote endpoint. Catch any errors.
            try
            {
                sender.Connect(remoteEP);

                Console.WriteLine("Socket connected to {0}",
                    sender.RemoteEndPoint.ToString());

                // Encode the data string into a byte array.
                byte[] msg = Encoding.ASCII.GetBytes(itemID);

                // Send the data through the socket.
                int bytesSent = sender.Send(msg);

                // Receive the response from the remote device.
                byte[] bytes = new byte[numBytes];
                int bytesRec = sender.Receive(bytes);

                // Release the socket.
                sender.Shutdown(SocketShutdown.Both);
                sender.Close();

                return BinarySerialization.GetObjectFromByteArray(bytes);

            }
            catch
            {
                Trace.TraceInformation("ClientGetItem failed for " + itemID);
            }

            return null;
        }

        private static void StartServerHelper()
        {

            // Create socket listener
            var listener = new Socket(ServerAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Bind socket listener to internal endpoint and listen
            listener.Bind(ServerAddress);
            listener.Listen(10);
            //Trace.TraceInformation("Listening on IP:{0},Port: {1}", myInternalEp.Address, myInternalEp.Port);

            while (true)
            {
                // Block the thread and wait for a client request
                Socket handler = listener.Accept();
                //Trace.TraceInformation("Client request received.");

                // Define body of socket handler
                var handlerThread = new Thread(
                  new ParameterizedThreadStart(h =>
                  {
                      var socket = h as Socket;
                      //Trace.TraceInformation("Local:{0} Remote{1}", socket.LocalEndPoint, socket.RemoteEndPoint);

                      string data = null;
                      byte[] bytes = new byte[1024];
                      int bytesRec = handler.Receive(bytes);
                      data += Encoding.ASCII.GetString(bytes, 0, bytesRec);

                      byte[] hostedItem = null;
                      HostedItems.TryGetValue(data, out hostedItem);
                      handler.Send(hostedItem);

                      // Shut down and close socket
                      socket.Shutdown(SocketShutdown.Both);
                      socket.Close();
                  }
                ));

                // Start socket handler on new thread
                handlerThread.Start(handler);
            }
        }

        public static IPEndPoint GetIPEndPoint(string epName)
        {
            var roleInstance = RoleEnvironment.CurrentRoleInstance;
            var myInternalEp = roleInstance.InstanceEndpoints[epName].IPEndpoint;
            return myInternalEp;
        }
    }
}