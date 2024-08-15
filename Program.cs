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
using Newtonsoft.Json;
using System.Reflection;

namespace DrinkOrangeJuiceServer
{
    class WinningInfo
    {
        public int score = 0;
        public string type = "winningInfo";
        public Rank rank;

        public WinningInfo(int score, Rank rank)
        {
            this.score = score;
            this.rank = rank;
        }
    }

    class Rank
    {
        public int score = 0;
        public string rankName = "";
        public Rank(string rankName, int maxScore)
        {
            this.score = maxScore;
            this.rankName = rankName;
        }
    }
    

    class ClientInfo
    {
        int[] drinksSeen = new int[0];
        int highScore = 0;
        int currentScore = 0;
        public ClientInfo() { }

        void IncreaseScore()
        {
            currentScore++;
        }

        public int getScore()
        {
            return currentScore;
        }
        public int getHighscore()
        {
            return highScore;
        }

        public int getCurrentDrinkIndex()
        {
            if(drinksSeen.Length > 0)
            {
                return drinksSeen[drinksSeen.Length - 1];
            }
            return -1;
        }

        public void AddDrinkToList(int index)
        {
            List<int> list = drinksSeen.ToList();
            list.Add(index);
            drinksSeen = list.ToArray();
        }
        bool HasSeenDrink(int index)
        {
            if (drinksSeen.Contains(index))
            {
                return true;
            }
            return false;
        }

        public int NextDrink(int drinkListLength)
        {
            Random rand = new Random();
            var range = Enumerable.Range(1, drinkListLength).Where(i => !drinksSeen.Contains(i));
            int index = rand.Next(0, drinkListLength - drinksSeen.Length);
            int actualIndex = range.ElementAt(index);

            AddDrinkToList(actualIndex);
            IncreaseScore();
            return actualIndex;
        }
        // returns true if its a new highscore
        public bool Restart()
        {
            if(currentScore > highScore)
            {
                highScore = currentScore;
                currentScore = 0;
                return true;
            }
            else
            {
                currentScore = 0;
                return false;
            }
        }
    }

    public class Drink
    {
        public string title;
        public string link;
        public string image_url;
        public string summary;
        public List<string> categories;
        public string type = "drink";
    }

    class Program
    {
        static string ip = "127.0.0.1";
        static int port = 80;
        static string drinkFileName = "filteredlist09124.json";
        static string needsPicFileName = "needspic.json";

        private static List<Socket> clients = new List<Socket>();
        public static Drink[] drinks;
        static Dictionary<Socket, ClientInfo> clientInfos = new Dictionary<Socket, ClientInfo>();
        private static TcpListener server = new TcpListener(
            IPAddress.Parse(ip),
            port
        );

        public static Rank[] ranks = new Rank[]
        {
            new Rank("Oh No J", 1600),
            new Rank("Juice Rookie", 1400),
            new Rank("OK OJ", 1200),
            new Rank("Average Squeezer", 1000),
            new Rank("OJ Guru", 800),
            new Rank("Citrus Champ", 600),
            new Rank("Super Sipper", 400),
            new Rank("Pulp Paladin", 200),
            new Rank("Vitamin C Virtuoso", 100),
            new Rank("Masterful Mixer", 50),
            new Rank("Sultan of Squeeze", 25)
        };

        public static void Main()
        {
            server.Start();
            string workingDirectory = Environment.CurrentDirectory;
            string projectDirectory = Directory.GetParent(workingDirectory).Parent.Parent.FullName;

            string jsonString = File.ReadAllText(projectDirectory + "\\" + drinkFileName);
            drinks = JsonConvert.DeserializeObject<Drink[]>(jsonString);

            Console.WriteLine("Server has started on {0}:{1}, Waiting for a connection…", ip, port);
            while (true)
            {
                Socket client = server.AcceptSocket();
                if (client.Connected)
                {
                    clients.Add(client);
                    clientInfos.Add(client, new ClientInfo());
                    Thread newThread = new Thread(() => Listeners(client));
                    newThread.Start();
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
                    string text = DecodeMessage(bytes);

                    Console.WriteLine("{0}", text);

                    //List<Socket> otherClients = clients.Where(
                    //        c => c.RemoteEndPoint != client.RemoteEndPoint
                    //    ).ToList();

                    if (text == "gimme a funny drink")
                    {
                        //int index = clientInfos[client].getCurrentDrinkIndex();
                        int index = 0;
                        bool notValid = true;
                        while (notValid)
                        {
                            Random rand = new Random();
                            index = rand.Next(0, drinks.Length);
                            if(drinks[index].image_url != null)
                            {
                                notValid = false;
                            }
                        }

                        GetClientADrink(client, index);
                    }
                    else if(text == "this is def oj")
                    {
                        int index = clientInfos[client].getCurrentDrinkIndex();
                        string title = drinks[index].title;

                        if (title == "Orange juice")
                        {
                            int score = clientInfos[client].getScore();

                            Rank currentRank = ranks[0];

                            for(int i = 0; i < ranks.Length-1; i++)
                            {
                                int rankIndex = ranks.Length - i;
                                if (score <= ranks[rankIndex].score)
                                {
                                    currentRank = ranks[rankIndex];
                                    break;
                                }
                            }

                            WinningInfo winningInfo = new WinningInfo(clientInfos[client].getScore(), currentRank);

                            byte[] message = EncodeMessageToSend(JsonConvert.SerializeObject(winningInfo));
                            client.Send(message);
                        }
                        else
                        {
                            WinningInfo winningInfo = new WinningInfo(-1, null);
                            byte[] message = EncodeMessageToSend(JsonConvert.SerializeObject(winningInfo));
                            client.Send(message);

                            clientInfos[client].Restart();
                        }
                    }
                    //else if(text == "this isnt a drink")
                    //{
                    //    int index = dictSocketClientInfos[client].getCurrentDrinkIndex();
                    //    RemoveDrinkByIndexAndSave(index);
                    //    GetClientADrink(client, index);
                    //}
                    //else if(text == "this needs a new pic")
                    //{
                    //    int index = dictSocketClientInfos[client].getCurrentDrinkIndex();
                    //    AddDrinkToNeedsPicFile(drinks[index].title);
                    //}

                    //if (otherClients.Count > 0)
                    //{
                    //    foreach (Socket cli in otherClients)
                    //    {
                    //        byte[] sendMessage = EncodeMessageToSend(text);
                    //        cli.Send(sendMessage);
                    //    }
                    //}

                    Console.WriteLine();
                }
            }
        }

        static void RemoveDrinkByIndexAndSave(int index)
        {
            List<Drink> list = drinks.ToList();
            list.RemoveAt(index);
            drinks = list.ToArray();
            SaveDrinks();
        }

        static void GetClientADrink(Socket client)
        {
            int index = clientInfos[client].NextDrink(drinks.Length);
            GetClientADrink(client, index);
        }

        static void GetClientADrink(Socket client, int index)
        {
            clientInfos[client].AddDrinkToList(index);
            byte[] message = EncodeMessageToSend(JsonConvert.SerializeObject(drinks[index]));
            client.Send(message);

        }

        private static void SaveDrinks()
        {
            string jsonString = JsonConvert.SerializeObject(drinks);

            string workingDirectory = Environment.CurrentDirectory;
            string projectDirectory = Directory.GetParent(workingDirectory).Parent.Parent.FullName;

            File.WriteAllText(projectDirectory + "\\" + drinkFileName, jsonString);
        }

        private static void AddDrinkToNeedsPicFile(string name)
        {
            string workingDirectory = Environment.CurrentDirectory;
            string projectDirectory = Directory.GetParent(workingDirectory).Parent.Parent.FullName;
            string fileString = File.ReadAllText(projectDirectory + "\\" + needsPicFileName);

            fileString += "\n" + name;

            File.WriteAllText(projectDirectory + "\\" + needsPicFileName, fileString);
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