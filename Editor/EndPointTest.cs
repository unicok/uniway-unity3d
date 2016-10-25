using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using System.Net.Sockets;
using System.Threading;

namespace Uniway {
	
	public class EndPointTest {

		[Test]
		public void ClinetTest()
		{
			var tpcClient = new TcpClient("127.0.0.1", 10010);
			var netSteam = tpcClient.GetStream();
			var endPoint = new EndPoint(netSteam, 1000, 0, null);
			var conn = endPoint.Dial(10086);
			var random = new System.Random();

			for (int i = 0; i < 100; i++) {
				var n = random.Next(10, 2000);
				var msg1 = new byte[n];
				random.NextBytes(msg1);

				Assert.IsTrue(conn.Send(msg1));

				byte[] msg2 = null;
				for(;;){
					msg2 = conn.Receive();
					Assert.NotNull(msg2);
			
					if(msg2 == Conn.NoMsg) {
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

			conn.Close ();
		}
	}
}
