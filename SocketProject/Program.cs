using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SocketProject
{
    class Program
    {
        static void Main(string[] args)
        {
            NioServer.Start();
        }

        /// <summary>
        /// 缺点:
        /// 只有一次数据读取完成后，才可以接受下一个连接请求
        /// 一个连接，只能接收一次数据
        /// </summary>
        public static void Start()
        {
            //1. 创建Tcp Socket对象
            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 5001);
            //2. 绑定Ip端口
            serverSocket.Bind(ipEndpoint);
            //3. 开启监听，指定最大连接数
            serverSocket.Listen(10);
            Console.WriteLine($"服务端已启动({ipEndpoint})-等待连接...");

            while (true)
            {
                //4. 等待客户端连接
                var clientSocket = serverSocket.Accept();//----------------------------------阻塞
                Console.WriteLine($"{clientSocket.RemoteEndPoint}-已连接");


                Span<byte> buffer = new Span<byte>(new byte[512]);
                Console.WriteLine($"{clientSocket.RemoteEndPoint}-开始接收数据...");
                int readLength = clientSocket.Receive(buffer);//----------------------------------阻塞
                var msg = Encoding.UTF8.GetString(buffer.ToArray(), 0, readLength);
                Console.WriteLine($"{clientSocket.RemoteEndPoint}-接收数据：{msg}");
                var sendBuffer = Encoding.UTF8.GetBytes($"received:{msg}");
                clientSocket.Send(sendBuffer);
            }
        }
        /// <summary>
        /// 缺点:
        /// 为了解决阻塞导致的问题，采用重复轮询，导致无效的系统调用，从而导致CPU持续走高。
        /// </summary>
        public static void Start2()
        {
            //1. 创建Tcp Socket对象
            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 5001);
            //2. 绑定Ip端口
            serverSocket.Bind(ipEndpoint);
            //3. 开启监听，指定最大连接数
            serverSocket.Listen(10);
            Console.WriteLine($"服务端已启动({ipEndpoint})-等待连接...");

            while (true)
            {
                //4. 等待客户端连接
                var clientSocket = serverSocket.Accept();//阻塞
                Task.Run(() => ReceiveData(clientSocket));//因为这里是异步,所以代码直接回到上一行继续等待连接,所以就解除了"只有一次数据读取完成后，才可以接受下一个连接请求"这么一个缺点
            }
        }
        private static void ReceiveData(Socket clientSocket)
        {
            Console.WriteLine($"{clientSocket.RemoteEndPoint}-已连接");
            Span<byte> buffer = new Span<byte>(new byte[512]);

            while (true)
            {
                if (clientSocket.Available == 0) continue;
                Console.WriteLine($"{clientSocket.RemoteEndPoint}-开始接收数据...");
                int readLength = clientSocket.Receive(buffer);//阻塞
                var msg = Encoding.Unicode.GetString(buffer.ToArray(), 0, readLength);
                Console.WriteLine($"{clientSocket.RemoteEndPoint}-接收数据：{msg}");
                var sendBuffer = Encoding.Unicode.GetBytes($"received:{msg}");
                clientSocket.Send(sendBuffer);
            }
        }



    }


    public static class NioServer
    {
        private static ManualResetEvent _acceptEvent = new ManualResetEvent(true);
        private static ManualResetEvent _readEvent = new ManualResetEvent(true);
        public static void Start()
        {
            //1. 创建Tcp Socket对象
            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // serverSocket.Blocking = false;//设置为非阻塞
            var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 5001);
            //2. 绑定Ip端口
            serverSocket.Bind(ipEndpoint);
            //3. 开启监听，指定最大连接数
            serverSocket.Listen(10);
            Console.WriteLine($"服务端已启动({ipEndpoint})-等待连接...");

            while (true)
            {
                _acceptEvent.Reset();//重置信号量
                serverSocket.BeginAccept(OnClientConnected, serverSocket);
                _acceptEvent.WaitOne();//阻塞
            }
        }

        private static void OnClientConnected(IAsyncResult ar)
        {
            _acceptEvent.Set();//当有客户端连接进来后，则释放信号量
            var serverSocket = ar.AsyncState as Socket;
            Debug.Assert(serverSocket != null, nameof(serverSocket) + " != null");

            var clientSocket = serverSocket.EndAccept(ar);
            Console.WriteLine($"{clientSocket.RemoteEndPoint}-已连接");

            while (true)
            {
                _readEvent.Reset();//重置信号量
                var stateObj = new StateObject { ClientSocket = clientSocket };
                clientSocket.BeginReceive(stateObj.Buffer, 0, stateObj.Buffer.Length, SocketFlags.None, OnMessageReceived, stateObj);
                _readEvent.WaitOne();//阻塞等待
            }
        }

        private static void OnMessageReceived(IAsyncResult ar)
        {
            var state = ar.AsyncState as StateObject;
            Debug.Assert(state != null, nameof(state) + " != null");
            var receiveLength = state.ClientSocket.EndReceive(ar);

            if (receiveLength > 0)
            {
                var msg = Encoding.UTF8.GetString(state.Buffer, 0, receiveLength);
                Console.WriteLine($"{state.ClientSocket.RemoteEndPoint}-接收数据：{msg}");

                var sendBuffer = Encoding.UTF8.GetBytes($"received:{msg}");
                state.ClientSocket.BeginSend(sendBuffer, 0, sendBuffer.Length, SocketFlags.None,
                    SendMessage, state.ClientSocket);
            }
        }

        private static void SendMessage(IAsyncResult ar)
        {
            var clientSocket = ar.AsyncState as Socket;
            Debug.Assert(clientSocket != null, nameof(clientSocket) + " != null");
            clientSocket.EndSend(ar);
            _readEvent.Set(); //发送完毕后，释放信号量
        }
    }

    public class StateObject
    {
        // Client  socket.  
        public Socket ClientSocket = null;
        // Size of receive buffer.  
        public const int BufferSize = 1024;
        // Receive buffer.  
        public byte[] Buffer = new byte[BufferSize];
    }
}
