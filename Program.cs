using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Reflection.Emit;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DrinkOrangeJuiceServer
{
    class Program
    {
        static string ip = "127.0.0.1";
        static int port = 80;

        private static List<Socket> clients = new List<Socket>();
        private static TcpListener server = new TcpListener(
            IPAddress.Parse(ip),
            port
        );

        public static void Main()
        {
            server.Start();
            Console.WriteLine("Server has started on {0}:{1}, Waiting for a connection…", ip, port);
            while (true)
            {
                Socket client = server.AcceptSocket();
                if (client.Connected)
                {
                    clients.Add(client);
                    Thread nuevoHilo = new Thread(() => Listeners(client));
                    nuevoHilo.Start();
                }
            }
        }

        private static void Listeners(Socket client)
        {
            Console.WriteLine("Client:" + client.RemoteEndPoint + " now connected to server.");
            NetworkStream stream = new NetworkStream(client);

            while (true)
            {
                while (!stream.DataAvailable) ;
                while (client.Available < 3) ; // match against "get"

                byte[] bytes = new byte[client.Available];
                stream.Read(bytes, 0, bytes.Length);
                string s = Encoding.UTF8.GetString(bytes);

                if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine("=====Handshaking from client=====\n{0}", s);

                    // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
                    // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
                    // 3. Compute SHA-1 and Base64 hash of the new value
                    // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
                    string swk = Regex.Match(s, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
                    string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                    string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

                    // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                    byte[] response = Encoding.UTF8.GetBytes(
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

                    stream.Write(response, 0, response.Length);
                }
                else
                {
                    var text = DecodeMessage(bytes);

                    Console.WriteLine("{0}", text);

                    var otherClients = clients.Where(
                            c => c.RemoteEndPoint != client.RemoteEndPoint
                        ).ToList();

                    if (otherClients.Count > 0)
                    {
                        foreach (var cli in otherClients)
                        {
                            var sendMessage = EncodeMessageToSend(text);
                            cli.Send(sendMessage);
                        }
                    }

                    Console.WriteLine();
                }
            }
        }

        private static string DecodeMessage(byte[] bytes)
        {
            var secondByte = bytes[1];
            var dataLength = secondByte & 127;
            var indexFirstMask = 2;
            if (dataLength == 126)
                indexFirstMask = 4;
            else if (dataLength == 127)
                indexFirstMask = 10;

            var keys = bytes.Skip(indexFirstMask).Take(4);
            var indexFirstDataByte = indexFirstMask + 4;

            var decoded = new byte[bytes.Length - indexFirstDataByte];
            for (int i = indexFirstDataByte, j = 0; i < bytes.Length; i++, j++)
            {
                decoded[j] = (byte)(bytes[i] ^ keys.ElementAt(j % 4));
            }

            return Encoding.UTF8.GetString(decoded, 0, decoded.Length);
        }

        private static byte[] EncodeMessageToSend(string message)
        {
            byte[] response;
            byte[] bytesRaw = Encoding.UTF8.GetBytes(message);
            byte[] frame = new byte[10];

            var indexStartRawData = -1;
            var length = bytesRaw.Length;

            frame[0] = (byte)129;
            if (length <= 125)
            {
                frame[1] = (byte)length;
                indexStartRawData = 2;
            }
            else if (length >= 126 && length <= 65535)
            {
                frame[1] = (byte)126;
                frame[2] = (byte)((length >> 8) & 255);
                frame[3] = (byte)(length & 255);
                indexStartRawData = 4;
            }
            else
            {
                frame[1] = (byte)127;
                frame[2] = (byte)((length >> 56) & 255);
                frame[3] = (byte)((length >> 48) & 255);
                frame[4] = (byte)((length >> 40) & 255);
                frame[5] = (byte)((length >> 32) & 255);
                frame[6] = (byte)((length >> 24) & 255);
                frame[7] = (byte)((length >> 16) & 255);
                frame[8] = (byte)((length >> 8) & 255);
                frame[9] = (byte)(length & 255);

                indexStartRawData = 10;
            }

            response = new byte[indexStartRawData + length];

            int i, reponseIdx = 0;

            //Add the frame bytes to the reponse
            for (i = 0; i < indexStartRawData; i++)
            {
                response[reponseIdx] = frame[i];
                reponseIdx++;
            }

            //Add the data bytes to the response
            for (i = 0; i < length; i++)
            {
                response[reponseIdx] = bytesRaw[i];
                reponseIdx++;
            }

            return response;
        }
    }
}