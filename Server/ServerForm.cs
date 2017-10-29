using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace Server {
    public partial class FmServer : Form {

        private Bitmap bmp;
        public FmServer() {
            InitializeComponent();
            //关闭文本框的非线性操作
            TextBox.CheckForIllegalCrossThreadCalls = false;
            txtIP.Text = "222.20.99.2";
            txtPort.Text = "666";
            bmp = new Bitmap(1920, 1080);
        }

        private Thread threadWatch = null;      //监听客户端的线程
        private Socket socketWatch = null;      //监听客户端的套接字
        Dictionary<string, List<Point>> trackDictionary = new Dictionary<string, List<Point>>();    //轨迹数据字典

        Dictionary<string, Socket> clientsDict = new Dictionary<string, Socket>();
        private Socket socConnection = null;    //负责和客户端通信的套接字

        private void btnServerStart_Click(object sender, EventArgs e) {
            //定义一个套接字用于监听客户端发来的消息
            socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //服务端发送消息,需要IP地址和端口号

            IPAddress ipAddress = IPAddress.Parse(txtIP.Text.Trim());
            IPEndPoint endPoint = new IPEndPoint(ipAddress, int.Parse(txtPort.Text.Trim()));

            //监听绑定的网络节点
            socketWatch.Bind(endPoint);
            //将套接字的监听队列长度限制为20
            socketWatch.Listen(20);
            //创建一个监听线程
            threadWatch = new Thread(watchConnection);
            //窗体线程设置为与后台同步
            threadWatch.IsBackground = true;
            threadWatch.Start();
            txtMsg.AppendText("开始监听客户端传来的信息!" + "\r\n");

            Thread userListSyncthread = new Thread(syncUserList);
            userListSyncthread.IsBackground = true;
            userListSyncthread.Start();

        }

        private void syncUserList() {
            while (true) {
                this.txtUserList.Clear();
                //取出客户端用户名拼接成列表
                foreach (KeyValuePair<string, Socket> socClient in clientsDict) {
                    this.txtUserList.AppendText(socClient.Key + "\n");
                }
                //发送用户列表给每一个客户端
                foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                    //*##*为标记字段,表示这是用户列表数据
                    ServerSenMsg(keyValuePair.Value, "*#userList#*" + this.txtUserList.Text.Trim());
                    //keyValuePair.Value.Send(Encoding.UTF8.GetBytes("*#userList#*" + this.txtUserList.Text.Trim()));
                }
                Thread.Sleep(2000);
            }
        }


        /// <summary>
        /// 监听客户端发来的请求
        /// </summary>
        private void watchConnection() {
            while (true) {
                try {
                    socConnection = socketWatch.Accept();
                } catch (Exception) {
                    throw;
                }
                //客户端网络节点号
                string remoteEndPointStr = socConnection.RemoteEndPoint.ToString();
                //若当前地址已经存在socket连接,跳过
                if (queryClientInDict(remoteEndPointStr) != null) {
                    continue;
                }
                //获取客户端IP和端口号
                IPAddress clientIP = (socConnection.RemoteEndPoint as IPEndPoint).Address;
                int clientPort = (socConnection.RemoteEndPoint as IPEndPoint).Port;
                this.txtMsg.AppendText("\n客户端:" + socConnection.RemoteEndPoint.ToString() + "上线\n");

                //给客户端发送信息,显示连接成功
                string sendmsg = "连接服务端成功！\r\n" + "本地IP:" + clientIP + "，本地端口" + clientPort.ToString();
                ServerSenMsg(socConnection, sendmsg);

                //添加客户端信息
                clientsDict.Add(remoteEndPointStr, socConnection);

                ParameterizedThreadStart pts = new ParameterizedThreadStart(ServerRecMsg);
                Thread thread = new Thread(pts);
                thread.IsBackground = true;
                thread.Start(socConnection);
            }
        }


        /// <summary>
        /// 接受客户端发来的信息
        /// </summary>
        /// <param name="obj"></param>
        private void ServerRecMsg(object socketClientPara) {

            Socket socketServer = socketClientPara as Socket;
            MemoryStream mStream;
            byte[] serverRecMsgBuffer = new byte[20 * 1024];
            //mStream = new MemoryStream();

            while (true) {
                try {
                    mStream = new MemoryStream();

                    int receiveCount = socketServer.Receive(serverRecMsgBuffer);

                    if (receiveCount == 0) {
                        continue;
                    } else {
                        mStream.Write(serverRecMsgBuffer, 0, receiveCount);
                    }

                    mStream.Flush();
                    mStream.Position = 0;
                    BinaryFormatter bFormatter = new BinaryFormatter();

                    string strRecMsg = null;
                    if (mStream.Capacity > 0) {
                        Object obj = null;
                        obj = bFormatter.Deserialize(mStream);

                        Dictionary<string, List<Point>> dict = obj as Dictionary<string, List<Point>>;

                        if (dict != null) {
                            //实时更新轨迹数据字典
                            trackDictionary = dict;
                            //传输过来的是轨迹数据流

                            //发送给其他所有客户端
                            foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                                
                                ServerSenMsg(keyValuePair.Value, trackDictionary);
                            }
                            //服务器本地显示数据
                            showTracks();

                            //mStream.Close();
                            continue;
                        } else {
                            strRecMsg = (string)obj;
                        }
                    }



                    //否则为文字消息, 将接收到的消息存入缓冲区,返回其字节数组长度
                    //string strRecMsg = Encoding.UTF8.GetString(serverRecMsgBuffer, 0, length);

                    //解析命令
                    int orderStart = strRecMsg.IndexOf("*#");
                    int orderEnd = strRecMsg.IndexOf("#*");
                    int orderLen = orderEnd - orderStart - 2;
                    string order = null;
                    if (orderLen > 0) {
                        order = strRecMsg.Substring(orderStart + 2, orderLen);
                    }
                    if (order == "closed") {
                        clientsDict.Remove(socketServer.RemoteEndPoint.ToString());
                        this.txtMsg.AppendText("\n" + socketServer.RemoteEndPoint.ToString() + "下线!\n");
                        socketServer.Close();
                    }

                    string msg = "*****" + socketServer.RemoteEndPoint + " at " + GetCurrentTime() + "\r\n" +
                                 strRecMsg + "\r\n";

                    //如果是私人消息
                    if (orderLen > 0 && queryClientInDict(order) != null) {
                        Socket socClient = queryClientInDict(order);
                        string sendmsg = "*****" + socketServer.RemoteEndPoint + " -> " + socClient.RemoteEndPoint +
                            " at " + GetCurrentTime() + strRecMsg.Substring(orderEnd + 2) + "\r\n";
                        //给私人消息发送方和接收方发消息
                        ServerSenMsg(socClient, sendmsg);
                        //                        socClient.Send(Encoding.UTF8.GetBytes(sendmsg));
                        if (socClient.RemoteEndPoint.ToString() != socketServer.RemoteEndPoint.ToString()) {
                            //若发送方与接收方是不相同
                            ServerSenMsg(socketServer, sendmsg);
                            //                            socketServer.Send(Encoding.UTF8.GetBytes(sendmsg));
                        }
                        txtMsg.AppendText(sendmsg);
                        continue;
                    }
                    //将收到的信息显示在消息框中
                    txtMsg.AppendText(msg);
                    //不是私人消息,转发消息给其他终端
                    foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                        ServerSenMsg(keyValuePair.Value, msg);
                    }
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            }
        }

        private Graphics graphics;
        private void showTracks() {
            graphics.Clear(Color.Silver);
            foreach (KeyValuePair<string, List<Point>> keyValuePair in trackDictionary) {
                foreach (Point point in keyValuePair.Value) {
                    graphics.FillEllipse(Brushes.Black, point.X, point.Y, 3, 3);

                }
            }
            pictureBox1.Image = bmp;
        }


        /// <summary>
        /// 根据客户端地址查找并返回其对应的socket
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private Socket queryClientInDict(string address) {
            foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                if (keyValuePair.Key == address) {
                    return keyValuePair.Value;
                }
            }
            return null;
        }


        /// <summary>
        /// 发送信息到客户端的方法
        /// </summary>
        /// <param name="sendMsg">发送的字符串信息</param>
        private void ServerSenMsg(Socket socClientItem, object obj) {
            MemoryStream mStream = new MemoryStream();
            BinaryFormatter bfBinaryFormatter = new BinaryFormatter();
            


            bfBinaryFormatter.Serialize(mStream, obj);
            mStream.Flush();
            byte[] buffer = new byte[20 * 1024];
            mStream.Position = 0;
            while (mStream.Read(buffer, 0, buffer.Length) > 0) {

                try {
                    socClientItem.Send(buffer);
                    //Thread.Sleep(100);
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            }
            mStream.Close();
        }


        /// <summary>
        /// 获取当前系统时间
        /// </summary>
        /// <returns>当前时间</returns>
        private DateTime GetCurrentTime() {
            DateTime currenTime = new DateTime();
            currenTime = DateTime.Now;
            return currenTime;
        }


        /// <summary>
        /// 按下按钮发送信息到客户端
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnSendMsg_Click(object sender, EventArgs e) {
            string msg = "*****Server:" + GetCurrentTime() + "\r\n" + txtServerMsg.Text.Trim() + "\r\n";
            foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                ServerSenMsg(keyValuePair.Value, msg);
            }
            this.txtMsg.AppendText(msg);
            //            ServerSenMsg(txtServerMsg.Text.Trim());
        }

        /// <summary>
        /// 关闭服务器时断开所有socket连接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FmServer_FormClosing(object sender, FormClosingEventArgs e) {
            //给每个客户端发送服务器关闭的消息
            foreach (KeyValuePair<string, Socket> keyValuePair in clientsDict) {
                ServerSenMsg(keyValuePair.Value, "*#ServerClosed#*");
                //                socClient.Value.Send(Encoding.UTF8.GetBytes("*#ServerClosed#*"));
                keyValuePair.Value.Close();
            }
            
        }

        private void saveTracks() {
            //Track - 1:32:3,125#5,123#22,117#33,112#49,104#66,98#74,95#77,95#89,92#100,90#111,86#121,82#135,75#150,69#165,57#176,48#183,42#192,36#198,32#204,29#218,22#227,18#236,18#244,18#256,18#265,18#283,14#288,13#292,12#301,7#308,2#316,-4
            //Track - 2:42:1,98#4,98#20,98#37,98#48,97#55,96#59,98#63,99#73,99#86,100#103,100#107,98#116,94#126,89#139,86#149,82#155,82#159,80#166,76#173,71#177,69#189,64#193,61#206,58#225,54#230,53#238,50#243,49#257,46#264,46#270,46#284,43#306,37#318,35#322,34#335,29#349,22#356,19#362,15#373,6#382,2#385,0
            //Track - 3:44:6,171#20,171#29,171#41,168#62,161#69,159#84,152#96,143#107,136#122,127#130,122#146,111#156,104#167,94#178,85#184,81#188,78#194,74#202,70#206,68#213,64#220,61#224,61#236,58#241,56#254,53#265,49#276,42#281,39#294,35#300,33#307,32#319,31#333,31#357,30#377,30#389,28#392,26#398,23#404,20#410,16#414,11#424,3#429,0
            //Track - 4:57:529,1#532,15#532,19#523,30#518,35#510,49#508,55#504,66#499,77#490,92#469,112#464,116#453,124#440,134#431,138#423,143#412,147#399,154#383,160#374,164#365,167#342,171#325,178#305,186#299,190#281,203#275,208#270,213#259,228#253,241#245,259#243,275#243,286#243,300#244,309#249,318#253,334#253,345#253,358#246,371#235,384#228,394#225,397#219,403#206,411#197,415#190,417#182,419#168,423#159,425#147,429#131,434#115,440#103,447#93,452#73,464#57,477
            //Track - 5:59:157,455#157,453#158,443#15
            string path = "C:\\Users\\SEAN\\Desktop\\研一上\\先进软件技术工具\\上机作业\\轨迹分类\\TrackSets";
            string filename = string.Format("{0}\\trackset-{1}.{2}.{3}.txt",
                path, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
            //"d:tracks\\trackset.txt"
            FileStream fileStream = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            StreamWriter streamWriter = new StreamWriter(fileStream);
            if (trackDictionary != null) {
                int i = 0;
                foreach (KeyValuePair<string, List<Point>> trackDictKeyValuePair in trackDictionary) {

                    //对每一条轨迹
                    StringBuilder onetrackStringBuilder = new StringBuilder();
                    //onetrackStringBuilder.Append(trackDictKeyValuePair.Key + ":");
                    onetrackStringBuilder.Append("track-" + ++i + ":");

                    onetrackStringBuilder.Append(trackDictKeyValuePair.Value.Count + ":");
                    foreach (Point point in trackDictKeyValuePair.Value) {
                        onetrackStringBuilder.Append(point.X + "," + point.Y + "#");
                    }
                    string onetrackString = onetrackStringBuilder.ToString();
                    streamWriter.WriteLine(onetrackString.Substring(0, onetrackString.Length - 1));
                }
            }
            //streamWriter.WriteLine("helloworld");
            streamWriter.Close();
        }

        private void btnTextChat_Click(object sender, EventArgs e) {
            this.txtMsg.Enabled = true;
            this.txtMsg.Visible = true;
            this.btnSendMsg.Enabled = true;

            this.pictureBox1.Enabled = false;
            this.pictureBox1.Visible = false;
        }



        private void btnWhiteBoard_Click(object sender, EventArgs e) {
            this.pictureBox1.Enabled = true;
            this.pictureBox1.Visible = true;

            //屏蔽发送消息按钮
            this.btnSendMsg.Enabled = false;
            this.txtMsg.Enabled = false;
            this.txtMsg.Visible = false;
            //            graphics = pictureBox1.CreateGraphics();
            graphics = Graphics.FromImage((Image)this.bmp);
        }

        private void FmServer_SizeChanged(object sender, EventArgs e) {
            //            graphics = pictureBox1.CreateGraphics();

        }

        private void button1_Click(object sender, EventArgs e) {
            saveTracks();
        }
    }
}
