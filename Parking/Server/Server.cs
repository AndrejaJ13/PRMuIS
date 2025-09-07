using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly Dictionary<int, decimal> ParkingEarnings = new Dictionary<int, decimal>();


        private static void Main(string[] args)
        {
            Console.WriteLine("Parking Server Starting...");


            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                DisplayTotalEarnings();
                Environment.Exit(0);
            };


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

                if (udpSocket.Poll(1000, SelectMode.SelectRead))
                {
                    HandleUdpClient(udpSocket);
                }

             
                if (tcpSocket.Poll(1000, SelectMode.SelectRead))
                {
                    var clientSocket = tcpSocket.Accept();
                    ClientSockets.Add(clientSocket);
                    SendParkingInfo(clientSocket);
                }

                for (var i = ClientSockets.Count - 1; i >= 0; i--)
                {
                    var socket = ClientSockets[i];
                    try
                    {
                        if (socket.Poll(1000, SelectMode.SelectRead))
                        {
                            HandleTcpClient(socket);
                        }
                    }
                    catch (Exception )
                    {
                        socket.Close();
                        ClientSockets.RemoveAt(i);
                    }
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
            foreach (var parkingId in ParkingInfos.Keys) ParkingEarnings[parkingId] = 0;
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

                if (message.StartsWith("Hocu da oslobodim: "))
                {
                    HandleParkingReleaseRequest(clientSocket, message);
                }
                else if (message.StartsWith("Oslobađam: "))
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

            var availableSpaces = parkingLotInfo.TotalSpaces - parkingLotInfo.OccupiedSpaces;

            if (availableSpaces == 0)
            {
                var badRequestResponse = Encoding.UTF8.GetBytes("No free parking spaces.");
                clientSocket.Send(badRequestResponse);
                return;
            }

            
            DateTime departureTime;
            if (!DateTime.TryParseExact(zauzece.VremeNapustanja, "HH:mm", null, DateTimeStyles.None, out departureTime))
            {
                var invalidTimeFormatResponse = Encoding.UTF8.GetBytes("Invalid time format. Use HH:mm");
                clientSocket.Send(invalidTimeFormatResponse);
                return;
            }

            var todayWithDepartureTime = DateTime.Today.Add(departureTime.TimeOfDay);
            var currentTime = DateTime.Now;

            if (currentTime.TimeOfDay >= departureTime.TimeOfDay)
            {
                var invalidTimeResponse =
                    Encoding.UTF8.GetBytes("Invalid departure time. Departure time must be in the future.");
                clientSocket.Send(invalidTimeResponse);
                return;
            }

            if (availableSpaces - zauzece.BrojMesta < 0)
            {
                var okResponse = Encoding.UTF8.GetBytes($"There is only this many free spaces: {availableSpaces}");
                clientSocket.Send(okResponse);

                var buffer = new byte[1024];
                var received = clientSocket.Receive(buffer);

                if (received == 0)
                {
                    clientSocket.Close();
                    ClientSockets.Remove(clientSocket);
                    return;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, received);
                if (message == "Yes")
                {
                    var requestId = _nextRequestId++;
                    zauzece.BrojMesta = availableSpaces;
                    zauzeca[requestId] = zauzece;
                    parkingLotInfo.OccupiedSpaces += zauzece.BrojMesta;

                    okResponse = Encoding.UTF8.GetBytes(requestId.ToString());
                    clientSocket.Send(okResponse);

                    Console.WriteLine(
                        $"Parking {zauzece.BrojParkinga} now has {parkingLotInfo.OccupiedSpaces} occupied spaces");
                }

                return;
            }

            var requestId2 = _nextRequestId++;
            zauzeca[requestId2] = zauzece;
            parkingLotInfo.OccupiedSpaces += zauzece.BrojMesta;

            var okResponse2 = Encoding.UTF8.GetBytes(requestId2.ToString());
            clientSocket.Send(okResponse2);

            Console.WriteLine(
                $"Parking {zauzece.BrojParkinga} now has {parkingLotInfo.OccupiedSpaces} occupied spaces");
        }

        private static void HandleParkingReleaseRequest(Socket clientSocket, string message)
        {
            if (!int.TryParse(message.Substring("Hocu da oslobodim: ".Length), out var requestId))
            {
                Console.WriteLine("Invalid request ID format received");
                return;
            }

            if (!zauzeca.TryGetValue(requestId, out var zauzece))
            {
                Console.WriteLine($"Request ID {requestId} not found");
                return;
            }

            var price = CalculatePrice(zauzece);
            clientSocket.Send(Encoding.UTF8.GetBytes(price.ToString()));

            Console.WriteLine(
                $"Client {clientSocket.RemoteEndPoint} has been delivered the receipt for parking of {price} RSD");
        }

        private static void HandleParkingRelease(string message)
        {
            if (!int.TryParse(message.Substring("Oslobađam: ".Length), out var requestId))
            {
                Console.WriteLine("Invalid request ID format received");
                return;
            }

            if (!zauzeca.TryGetValue(requestId, out var zauzece))
            {
                Console.WriteLine($"Request ID {requestId} not found");
                return;
            }

            var price = CalculatePrice(zauzece);
            ParkingEarnings[zauzece.BrojParkinga] += price;

            var parkingInfo = ParkingInfos[zauzece.BrojParkinga];
            parkingInfo.OccupiedSpaces -= zauzece.BrojMesta;

            Console.WriteLine($"Parking {zauzece.BrojParkinga} now has {parkingInfo.OccupiedSpaces} occupied spaces");
            Console.WriteLine(zauzece.ToString());
        }
        private static decimal CalculatePrice(Zauzece zauzece)
        {
            var parkingInfo = ParkingInfos[zauzece.BrojParkinga];

            DateTime departureTime;
            if (!DateTime.TryParseExact(zauzece.VremeNapustanja, "HH:mm", null, DateTimeStyles.None, out departureTime))
                departureTime = DateTime.Now;

            var departureDateTime = departureTime;
            while (departureDateTime <= zauzece.VremeDolaska) departureDateTime = departureDateTime.AddDays(1);
            var timeDifference = departureDateTime - zauzece.VremeDolaska;
            var hours = Math.Ceiling(timeDifference.TotalHours);
            var hoursInt = (int)hours;

            var price = hoursInt * parkingInfo.PricePerHour * zauzece.BrojMesta;
            return price;
        }
        private static void SendParkingInfo(Socket clientSocket)
        {
            try
            {
                
                var parkingData = SerializeObject(ParkingInfos);

                var dataSize = BitConverter.GetBytes(parkingData.Length);
                if (!SendAll(clientSocket, dataSize))
                {
                    return;
                }

                SendAll(clientSocket, parkingData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending parking info: {ex.Message}");
                clientSocket.Close();
                ClientSockets.Remove(clientSocket);
            }
        }

        private static bool SendAll(Socket socket, byte[] data)
        {
            try
            {
                var totalSent = 0;
                while (totalSent < data.Length)
                {
                    var remaining = data.Length - totalSent;
                    var sent = socket.Send(data, totalSent, remaining, SocketFlags.None);

                    if (sent <= 0) return false;

                    totalSent += sent;
                }

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
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
        private static void DisplayTotalEarnings()
        {
            foreach (var parking in ParkingEarnings) Console.WriteLine($"Parking {parking.Key}: {parking.Value} RSD");
        }

    }
}