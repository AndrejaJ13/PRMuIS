using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Domain;

namespace Server
{
    internal class Server
    {
        private const int UdpPort = 15001;
        private const int TcpPort = 15002;
        private static readonly Dictionary<int, Zauzece> zauzeca = new Dictionary<int, Zauzece>();
        private static readonly List<Socket> ClientSockets = new List<Socket>();
        private static int _nextRequestId = 1;
        private static readonly Dictionary<int, ParkingLotInfo> ParkingInfos = new Dictionary<int, ParkingLotInfo>();

        private static void Main(string[] args)
        {
            Console.WriteLine("Parking Server Starting...");

            InitializeParkingData();

            var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var udpEndPoint = new IPEndPoint(IPAddress.Any, UdpPort);
            var tcpEndPoint = new IPEndPoint(IPAddress.Any, TcpPort);

            udpSocket.Bind(udpEndPoint);
            tcpSocket.Bind(tcpEndPoint);
            tcpSocket.Listen(10);

            Console.WriteLine("Server is listening for connections...");

            while (true)
            {
                var checkRead = new List<Socket> { udpSocket, tcpSocket };
                checkRead.AddRange(ClientSockets);

                try
                {
                    Socket.Select(checkRead, null, null, 1000000); // 1 second timeout

                    foreach (var socket in checkRead)
                        if (socket == udpSocket)
                        {
                            HandleUdpClient(udpSocket);
                        }
                        else if (socket == tcpSocket)
                        {
                            var clientSocket = tcpSocket.Accept();
                            ClientSockets.Add(clientSocket);
                            SendParkingInfo(clientSocket);
                        }
                        else
                        {
                            HandleTcpClient(socket);
                        }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static void InitializeParkingData()
        {
            Console.Write("Enter number of parking lots: ");
            var numParkings = int.Parse(Console.ReadLine() ?? string.Empty);

            for (var i = 1; i <= numParkings; i++)
            {
                Console.WriteLine($"\nParking {i}:");
                Console.Write("Total spaces: ");
                var totalSpaces = int.Parse(Console.ReadLine() ?? string.Empty);
                Console.Write("Occupied spaces: ");
                var occupiedSpaces = int.Parse(Console.ReadLine() ?? string.Empty);
                Console.Write("Price per hour: ");
                var pricePerHour = decimal.Parse(Console.ReadLine() ?? string.Empty);

                ParkingInfos[i] = new ParkingLotInfo
                {
                    TotalSpaces = totalSpaces,
                    OccupiedSpaces = occupiedSpaces,
                    PricePerHour = pricePerHour
                };
            }
        }

        private static void HandleUdpClient(Socket udpSocket)
        {
            var buffer = new byte[1024];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);

            if (received <= 0) return;

            var serverInfo = new ServerInfo
            {
                IpAddress = "127.0.0.1",
                Port = TcpPort
            };

            var response = SerializeObject(serverInfo);
            udpSocket.SendTo(response, remoteEp);
        }

        private static void HandleTcpClient(Socket clientSocket)
        {
            try
            {
                var buffer = new byte[1024];
                var received = clientSocket.Receive(buffer);

                if (received == 0)
                {
                    clientSocket.Close();
                    ClientSockets.Remove(clientSocket);
                    return;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, received);

                if (message.StartsWith("Oslobađam: "))
                {
                    HandleParkingRelease(message);
                }
                else
                {
                    var zauzece = DeserializeObject<Zauzece>(buffer);
                    HandleParkingRequest(clientSocket, zauzece);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling TCP client: {ex.Message}");
                clientSocket.Close();
                ClientSockets.Remove(clientSocket);
            }
        }

        private static void HandleParkingRequest(Socket clientSocket, Zauzece zauzece)
        {
            if (!ParkingInfos.TryGetValue(zauzece.BrojParkinga, out var parkingLotInfo))
            {
                var notFoundResponse = Encoding.UTF8.GetBytes("Parking Lot not found.");
                clientSocket.Send(notFoundResponse);
                return;
            }

            if (parkingLotInfo.TotalSpaces - parkingLotInfo.OccupiedSpaces < zauzece.BrojMesta)
            {
                var badRequestResponse = Encoding.UTF8.GetBytes("Not enough free parking spaces.");
                clientSocket.Send(badRequestResponse);
                return;
            }

            var requestId = _nextRequestId++;
            zauzeca[requestId] = zauzece;
            parkingLotInfo.OccupiedSpaces += zauzece.BrojMesta;

            var okResponse = Encoding.UTF8.GetBytes(requestId.ToString());
            clientSocket.Send(okResponse);

            Console.WriteLine(
                $"Parking {zauzece.BrojParkinga} now has {parkingLotInfo.OccupiedSpaces} occupied spaces");
        }

        private static void HandleParkingRelease(string message)
        {
            var requestId = int.Parse(message.Substring("Oslobađam: ".Length));

            if (!zauzeca.TryGetValue(requestId, out var zauzece)) return;

            var parkingInfo = ParkingInfos[zauzece.BrojParkinga];
            parkingInfo.OccupiedSpaces -= zauzece.BrojMesta;

            Console.WriteLine(
                $"Parking {zauzece.BrojParkinga} now has {parkingInfo.OccupiedSpaces} occupied spaces");

            zauzeca.Remove(requestId);
        }

        private static void SendParkingInfo(Socket clientSocket)
        {
          
            var parkingData = SerializeObject(ParkingInfos);

            var dataSize = BitConverter.GetBytes(parkingData.Length);
            clientSocket.Send(dataSize);

            clientSocket.Send(parkingData);
        }

        private static byte[] SerializeObject(object obj)
        {
            using (var ms = new MemoryStream())
            {
                var bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        private static T DeserializeObject<T>(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                var bf = new BinaryFormatter();
                return (T)bf.Deserialize(ms);
            }
        }
    }
}