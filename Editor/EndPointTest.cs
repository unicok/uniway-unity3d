using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using Snet;

namespace Uniway {
	
	public class EndPointTest {

        EndPoint endPoint;
        Conn conn;

		[Test]
        public void Test_NoSnet()
		{
			var tcpClient = new TcpClient("127.0.0.1", 10010);
			var netSteam = tcpClient.GetStream();
			DOTest(netSteam, 10086);
		}

		[Test]
		public void Test_NoSnet_2()
		{
			var tcpClient = new TcpClient("127.0.0.1", 10010);
			var netSteam = tcpClient.GetStream();
			DOTest(netSteam, 10087);
		}

        [Test]
        public void Test_Snet_Encrypt() 
        {
            var stream = new SnetStream(1024, true);
            stream.Connect("127.0.0.1", 10010);
			DOTest(stream, 10086);
        }

        [Test]
        public void Test_Snet_NoEncrypt() {
            var stream = new SnetStream(1024, false);
            stream.Connect("127.0.0.1", 10010);
            DOTest(stream, 10086);
        }

        private void DOTest(Stream stream, uint serverID) {
//            System.Action timeoutAction = () => {
//                endPoint.Close();
//                conn.Close();
//                Debug.Log("timeout.");
//            };

            endPoint = new EndPoint(stream, 1000, 5000, null);
			conn = endPoint.Dial(serverID);
            var random = new System.Random();

            Thread.Sleep(1000 * 1);

            Debug.LogFormat("ConnID:{0}, RemoteID:{1}", conn.ID, conn.RemoteID);

            for (int i = 0; i < 100; i++) {
                var n = random.Next(10, 2000);
                var msg1 = new byte[n];
                random.NextBytes(msg1);

                Assert.IsTrue(conn.Send(msg1));

                byte[] msg2 = null;
                for (; ; ) {
                    msg2 = conn.Receive();
                    Assert.NotNull(msg2);

                    if (msg2 == Conn.NoMsg) {
                        continue;
                    }
                    break;
                }

                Assert.AreEqual(msg1.Length, msg2.Length);

                for (var j = 0; j < n; j++) {
                    Assert.AreEqual(msg1[j], msg2[j]);
                }

                Debug.LogFormat("{0}, {1}", i, msg1.Length);
            }

            conn.Close();
        }
	}
}
