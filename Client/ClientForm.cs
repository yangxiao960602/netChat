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

namespace Client {
    public partial class ClientForm : Form {

        private Bitmap bmp;
        public ClientForm() {
            InitializeComponent();
            //关闭对文本框的非法线程操作检查
            TextBox.CheckForIllegalCrossThreadCalls = false;
            this.txtIP.Text = "222.20.99.2";
            this.txtPort.Text = "666";
            //分辨率,TODO
            bmp = new Bitmap(1920, 1080);
        }

        private Socket socketClient = null;
        private Thread threadClient = null;
        private bool isSocketConnected = false;


        /// <summary>
        /// 连接到服务器按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnBeginListen_Click(object sender, EventArgs e) {

            try {
                if (!isSocketConnected) {
                    socketClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPAddress ipAddress = IPAddress.Parse(txtIP.Text.Trim());
                    IPEndPoint endPoint = new IPEndPoint(ipAddress, int.Parse(txtPort.Text.Trim()));
                    socketClient.Connect(endPoint);
                    isSocketConnected = true;
                    //上线
                    this.lb_ID.Text = socketClient.LocalEndPoint.ToString();
                    this.btnSendMsg.Enabled = true;
                    threadClient = new Thread(ClientRecMsg);
                    threadClient.IsBackground = true;
                    threadClient.Start();
                } else {
                    this.txtMsg.AppendText("\r\n请勿重复建立连接\r\n");
                }

            } catch (Exception exception) {
                Console.WriteLine(exception);
                this.txtMsg.AppendText("\r\n连接服务器失败!\r\n");
                return;
            }

        }


        /// <summary>
        /// 接收服务端发来的信息
        /// </summary>
        private void ClientRecMsg() {
            MemoryStream mStream;
            byte[] serverRecMsgBuffer = new byte[20 * 1024];

            while (true) {
                try {
                    mStream = new MemoryStream();
                    //mStream.Position = 0;

                    int receiveCount = socketClient.Receive(serverRecMsgBuffer);
                    if (receiveCount == 0) {
                        mStream.Close();
                        break;
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
                            //传输过来的是轨迹数据流
                            trackDictionary = dict;
                            string lastTrackName = trackDictionary.Last().Key.ToString();
                            lastTrackIndex = int.Parse(lastTrackName.Last().ToString());
                            showTracks();
                            continue;
                        } else {
                            strRecMsg = (string)obj;
                        }
                    }


                    //否则为文字消息,开始解析命令
                    int orderStart = strRecMsg.IndexOf("*#");
                    int orderEnd = strRecMsg.IndexOf("#*");
                    int orderLen = orderEnd - orderStart - 2;
                    string order = null;
                    if (orderLen > 0) {
                        order = strRecMsg.Substring(orderStart + 2, orderLen);
                    }
                    if (orderLen > 0 && order == "userList") {
                        this.txtUserList.Text = strRecMsg.Substring(12);
                    } else if (orderLen > 0 && order == "ServerClosed") {
                        this.socketClient.Close();
                        isSocketConnected = false;
                    } else if (order == "startWhiteBoard") {
                        btnWhiteBoard_Click(null, null);
                    } else {
                        txtMsg.AppendText(strRecMsg + "\r\n");
                    }

                } catch (Exception e) {
                    txtMsg.AppendText("\n服务器连接中断!\r\n");
                    this.lb_ID.Text = "offline";
                    this.btnSendMsg.Enabled = false;
                    txtUserList.Clear();
                    isSocketConnected = false;
                    Console.WriteLine(e);
                    break;
                }
            }

        }

        private void showTracks() {

            graphics.Clear(Color.Silver);
            foreach (KeyValuePair<string, List<Point>> keyValuePair in trackDictionary) {
                foreach (Point point in keyValuePair.Value) {
                    graphics.FillEllipse(Brushes.Black, point.X, point.Y, 3, 3);

                }
            }
            pictureBox1.Image = bmp;
        }

        private DateTime GetCurrentTime() {
            DateTime currenTime = new DateTime();
            currenTime = DateTime.Now;
            return currenTime;
        }


        /// <summary>
        /// 发送字符串信息到服务端
        /// </summary>
        /// <param name="sendMsg"></param>
        private void ClientSendMsg(object obj) {
            MemoryStream mStream = new MemoryStream();
            BinaryFormatter bfBinaryFormatter = new BinaryFormatter();

            bfBinaryFormatter.Serialize(mStream, obj);


            mStream.Flush();
            mStream.Position = 0;
            byte[] buffer = new byte[20 * 1024];




            while (mStream.Read(buffer, 0, buffer.Length) > 0) {
                try {

                    socketClient.Send(buffer);
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            }
            mStream.Close();
        }

        private void btnSendMsg_Click(object sender, EventArgs e) {
            ClientSendMsg(txtClientMsg.Text.Trim());
        }

        private void ClientForm_FormClosed(object sender, FormClosedEventArgs e) {

        }

        private void ClientForm_FormClosing(object sender, FormClosingEventArgs e) {
            try {
                ClientSendMsg("*#closed#*");
                socketClient.Close();
            } catch (Exception) {

                //throw exception;
            }


        }

        private void btnClearMsg_Click(object sender, EventArgs e) {
            //todo 发送清空记录消息给服务器,并让其转发
            //TODO 无需每次收到轨迹后清空画板->line145
            if (this.txtMsg.Enabled) {
                this.txtMsg.Clear();
            }
            if (this.pictureBox1.Enabled) {
                pictureBox1.Visible = false;

                this.graphics.Clear(Color.Silver);
                pictureBox1.Visible = true;
                trackDictionary.Clear();
            }
        }


        /// <summary>
        /// 开始白板演示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnWhiteBoard_Click(object sender, EventArgs e) {
            this.pictureBox1.Enabled = true;
            this.pictureBox1.Visible = true;

            //屏蔽发送消息按钮
            this.btnSendMsg.Enabled = false;
            this.txtMsg.Enabled = false;
            this.txtMsg.Visible = false;

            graphics = Graphics.FromImage((Image)this.bmp);
            if (trackDictionary.Count != 0)
                trackDictionary.Clear();
            

        }

        //画图工具
        private Graphics graphics;
        private bool onMouseDown = false;
        Point lastPoint = new Point();
        Pen pen = new Pen(Color.Black, 3);
        private Dictionary<string, List<Point>> trackDictionary = new Dictionary<string, List<Point>>();
        private int lastTrackIndex = 0;
        private void pictureBox1_MouseDown(object sender, MouseEventArgs e) {
            onMouseDown = true;
            if (trackDictionary.Count != 0) {
                string lastTrackName = trackDictionary.Last().Key;
                lastTrackIndex = int.Parse(lastTrackName.Substring(6));
            }

            trackDictionary.Add("track-" + (lastTrackIndex + 1), new List<Point>());

        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e) {
            onMouseDown = false;
            lastTrackIndex++;


            syncTracks();
        }


        /// <summary>
        /// 给服务器发送轨迹数据流
        /// </summary>
        private void syncTracks() {
            ClientSendMsg(trackDictionary);
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e) {
            if (lastPoint.Equals(Point.Empty)) {
                lastPoint = new Point(e.X, e.Y);
            }
            if (onMouseDown) {
                Point currentPoint = new Point(e.X, e.Y);
                graphics.DrawLine(pen, currentPoint, lastPoint);
                pictureBox1.Image = bmp;
            }
            //若鼠标按下且在移动
            if (onMouseDown) {
                trackDictionary["track-" + (lastTrackIndex + 1)].Add(lastPoint);
            }
            lastPoint = new Point(e.X, e.Y);
        }

        private void btnTextChat_Click(object sender, EventArgs e) {
            this.txtMsg.Enabled = true;
            this.txtMsg.Visible = true;
            this.btnSendMsg.Enabled = true;

            this.pictureBox1.Enabled = false;
            this.pictureBox1.Visible = false;
        }

        private void ClientForm_SizeChanged(object sender, EventArgs e) {
            
        }

    }

}
