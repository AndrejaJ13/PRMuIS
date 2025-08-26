using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Domain;

namespace Client
{
    internal class Client
    {
        private const int UdpPort = 15001;
        private static Socket _tcpSocket;
        private static readonly Dictionary<int, Zauzece> ActiveRequests = new Dictionary<int, Zauzece>();

        private static void Main(string[] args)
        {
            Console.WriteLine("Parking Client Starting...");

            var serverInfo = GetServerInfo();
            if (serverInfo == null)
            {
                Console.WriteLine("Failed to get server information.");
                return;
            }

        
            _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                _tcpSocket.Connect(IPAddress.Parse(serverInfo.IpAddress), serverInfo.Port);
                Console.WriteLine("Connected to server.");

         
                var parkingInfos = ReceiveParkingInfo();
                DisplayParkingInfo(parkingInfos);

                while (true)
                {
                    Console.WriteLine("\nOptions:");
                    Console.WriteLine("1. Request parking space");
                    Console.WriteLine("2. Release parking space");
                    Console.WriteLine("3. Exit");
                    Console.Write("Choose option: ");

                    var choice = Console.ReadLine();

                    switch (choice)
                    {
                        case "1":
                            RequestParking();
                            break;
                        case "2":
                            ReleaseParking();
                            break;
                        case "3":
                            return;
                        default:
                            Console.WriteLine("Invalid option.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                if (_tcpSocket != null && _tcpSocket.Connected) _tcpSocket.Close();
            }
        }

        private static ServerInfo GetServerInfo()
        {
            using (var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                try
                {
                    var serverEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), UdpPort);

               
                    var requestData = Encoding.UTF8.GetBytes("Hello");
                    udpSocket.SendTo(requestData, serverEndPoint);

                  
                    var buffer = new byte[1024];
                    EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);

                    if (received > 0) return DeserializeObject<ServerInfo>(buffer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP Error: {ex.Message}");
                }

                return null;
            }
        }

        private static Dictionary<int, ParkingLotInfo> ReceiveParkingInfo()
        {
            
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[1024];
                var totalReceived = 0;

              
                var sizeBuffer = new byte[4];
                totalReceived = _tcpSocket.Receive(sizeBuffer);
                var dataSize = BitConverter.ToInt32(sizeBuffer, 0);

        
                while (totalReceived < dataSize)
                {
                    var received = _tcpSocket.Receive(buffer);
                    if (received == 0) break; 
                    ms.Write(buffer, 0, received);
                    totalReceived += received;
                }

             
                ms.Position = 0;
                return DeserializeObject<Dictionary<int, ParkingLotInfo>>(ms.ToArray());
            }
        }

        private static void DisplayParkingInfo(Dictionary<int, ParkingLotInfo> parkingInfos)
        {
            Console.WriteLine("\nCurrent Parking Status:");
            foreach (var pair in parkingInfos)
            {
                Console.WriteLine($"Parking {pair.Key}:");
                Console.WriteLine($"  Total Spaces: {pair.Value.TotalSpaces}");
                Console.WriteLine($"  Occupied Spaces: {pair.Value.OccupiedSpaces}");
                Console.WriteLine($"  Price per Hour: {pair.Value.PricePerHour:C}");
            }
        }

        private static void RequestParking()
        {
            try
            {
                Console.Write("Enter parking number: ");
                var parkingNumber = int.Parse(Console.ReadLine() ?? string.Empty);

                Console.Write("Enter number of spaces needed: ");
                var spacesNeeded = int.Parse(Console.ReadLine() ?? string.Empty);

                Console.Write("Enter expected departure time (HH:mm): ");
                var departureTime = Console.ReadLine();

                var zauzece = new Zauzece
                {
                    BrojParkinga = parkingNumber,
                    BrojMesta = spacesNeeded,
                    VremeNapustanja = departureTime,
                    VremeDolaska = DateTime.Now,
                };

                var data = SerializeObject(zauzece);
                _tcpSocket.Send(data);

                var buffer = new byte[1024];
                var received = _tcpSocket.Receive(buffer);
                var response = Encoding.UTF8.GetString(buffer, 0, received);

              
                if (int.TryParse(response, out var requestId))
                {
                    ActiveRequests[requestId] = zauzece;
                    Console.WriteLine($"Parking request accepted. Your request ID is: {requestId}");
                }
                else if (response.StartsWith("There is only this many free spaces: "))
                {
                    Console.WriteLine(response);
                    Console.Write("Do you want to proceed with allocating the parking spaces? (yes/no): ");
                    var answer = Console.ReadLine()?.ToLower();

                    if (answer == "yes" || answer == "y")
                    {
                        var availableSpaces = int.Parse(response.Substring("There is only this many free spaces: ".Length));
                        zauzece.BrojMesta = availableSpaces;

                        var message = "Yes";
                        data = Encoding.UTF8.GetBytes(message);
                        _tcpSocket.Send(data);

                        received = _tcpSocket.Receive(buffer);
                        var requestIdResponse = Encoding.UTF8.GetString(buffer, 0, received);
                        int.TryParse(requestIdResponse, out var requestId2);

                        Console.WriteLine($"Parking request accepted. Your request ID is: {requestId2}");
                        ActiveRequests[requestId2] = zauzece;
                    }
                    else
                    {
                        var message = "No";
                        data = Encoding.UTF8.GetBytes(message);
                        _tcpSocket.Send(data);
                        Console.WriteLine("Parking request cancelled.");
                    }
                }
                else
                {
                    Console.WriteLine($"Parking request declined. Server response: {response}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error requesting parking: {ex.Message}");
            }
        }

        private static void ReleaseParking()
        {
            Console.Write("Enter request ID to release: ");
            if (!int.TryParse(Console.ReadLine(), out var requestId)) return;

            if (ActiveRequests.ContainsKey(requestId))
            {
                var message = $"Hocu da oslobodim: {requestId}";
                var data = Encoding.UTF8.GetBytes(message);
                _tcpSocket.Send(data);

                var buffer = new byte[1024];
                var received = _tcpSocket.Receive(buffer);
                var response = Encoding.UTF8.GetString(buffer, 0, received);

                
                if (decimal.TryParse(response, out var price))
                {
                    Console.WriteLine($"The parking fee is: {price} RSD");
                    Console.Write("Do you want to proceed with releasing the parking space? (yes/no): ");
                    var answer = Console.ReadLine()?.ToLower();

                    if (answer == "yes" || answer == "y")
                    {
                        
                        message = $"Oslobađam: {requestId}";
                        data = Encoding.UTF8.GetBytes(message);
                        _tcpSocket.Send(data);
                        ActiveRequests.Remove(requestId);
                        Console.WriteLine("Parking space released.");
                    }
                    else
                    {
                        Console.WriteLine("Parking release cancelled.");
                    }
                }
                else
                {
                    
                    Console.WriteLine($"Error: {response}");
                }
            }
            else
            {
                Console.WriteLine("Invalid request ID.");
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
    }
}