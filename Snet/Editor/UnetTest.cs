using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System;

namespace Snet {
    public class UnetTest {

        protected System.Random rand = new System.Random();

        [Test]
        public void RereaderTest() {
            var n = 10000;
            var q = new Queue<byte[]>(n);
            var r = new Rereader();

            for (int i = 0; i < n; i++) {
                var b = RandBytes(100);
                using (var ms = new MemoryStream(b)) {
                    r.Reread(ms, b.Length);
                }
                q.Enqueue(b);
            }

            for (var i = 0; i < n; i++) {
                var raw = q.Dequeue();
                var b = new byte[raw.Length];
                var offset = 0;
                var remind = raw.Length;
                while (remind > 0) {
                    var size = rand.Next(remind + 1);
                    if (size == 0) {
                        continue;
                    }
                    r.Pull(b, offset, size);
                    offset = offset + size;
                    remind = remind - size;
                }
                Assert.True(BytesEquals(raw, b));
            }
        }

        [Test]
        public void RewriterTest() {
            ulong writeCount = 0;
            ulong readCount = 0;

            var w = new Rewriter(100);

            for (var i = 0; i < 100000; i++) {
                var a = RandBytes(100);
                var b = new byte[a.Length];
                w.Push(a, 0, a.Length);
                writeCount += (ulong)a.Length;

                var remind = a.Length;
                var offset = 0;
                while (remind > 0) {
                    var size = rand.Next(remind) + 1;

                    using (MemoryStream ms = new MemoryStream(b, offset, b.Length - offset)) {
                        Assert.True(w.Rewrite(ms, writeCount, readCount));
                    }

                    readCount += (ulong)size;
                    offset += size;
                    remind -= size;
                }

                Assert.True(BytesEquals(a, b));
            }
        }


        [Test]
        public void Test_Stable_NoEncrypt() {
            StreamTest(false, false, 10010);
        }

        [Test]
        public void Test_Stable_Encrypt() {
            StreamTest(true, false, 10011);
        }

        [Test]
        public void Test_Unstable_NoEncrypt() {
            StreamTest(false, false, 10012);
        }

        [Test]
        public void Test_Unstable_Encrypt() {
            StreamTest(true, false, 10013);
        }

        [Test]
        public void Test_Stable_NoEncrypt_Reconn() {
            StreamTest(false, true, 10010);
        }

        [Test]
        public void Test_Stable_Encrypt_Reconn() {
            StreamTest(true, true, 10011);
        }

        [Test]
        public void Test_Unstable_NoEncrypt_Reconn() {
            StreamTest(false, true, 10012);
        }

        [Test]
        public void Test_Unstable_Encrypt_Reconn() {
            StreamTest(true, true, 10013);
        }

        private void StreamTest(bool enableCrypt, bool reconn, int port) {
            var stream = new SnetStream(1024, enableCrypt);

            stream.Connect("127.0.0.1", port);

            for (int i = 0; i < 10000; i++) {
                var a = RandBytes(100);
                var b = a;
                var c = new byte[a.Length];

                if (enableCrypt) {
                    b = new byte[a.Length];
                    Buffer.BlockCopy(a, 0, b, 0, a.Length);
                }

                stream.Write(a, 0, a.Length);

                if (reconn && i % 100 == 0) {
                    if (!stream.TryReconn())
                        Assert.Fail();
                }

                for (int n = c.Length; n > 0; ) {
                    n -= stream.Read(c, c.Length - n, n);
                }

                if (!BytesEquals(b, c))
                    Assert.Fail();
            }

            stream.Close();
        }

        [Test]
        public void Test_Stable_NoEncrypt_Async() {
            StreamTestAsync(false, false, 10010);
        }

        [Test]
        public void Test_Stable_Encrypt_Async() {
            StreamTestAsync(true, false, 10011);
        }

        [Test]
        public void Test_Unstable_NoEncrypt_Async() {
            StreamTestAsync(false, false, 10012);
        }

        [Test]
        public void Test_Unstable_Encrypt_Async() {
            StreamTestAsync(true, false, 10013);
        }

        [Test]
        public void Test_Stable_NoEncrypt_Async_Reconn() {
            StreamTestAsync(false, true, 10010);
        }

        [Test]
        public void Test_Stable_Encrypt_Async_Reconn() {
            StreamTestAsync(true, true, 10011);
        }

        [Test]
        public void Test_Unstable_NoEncrypt_Async_Reconn() {
            StreamTestAsync(false, true, 10012);
        }

        [Test]
        public void Test_Unstable_Encrypt_Async_Reconn() {
            StreamTestAsync(true, true, 10013);
        }

        private void StreamTestAsync(bool enableCrypt, bool reconn, int port) {
            var stream = new SnetStream(1024, enableCrypt);

            var ar = stream.BeginConnect("127.0.0.1", port, null, null);
            stream.WaitConnect(ar);

            for (int i = 0; i < 1000; i++) {
                var a = RandBytes(100);
                var b = a;
                var c = new byte[a.Length];

                if (enableCrypt) {
                    b = new byte[a.Length];
                    Buffer.BlockCopy(a, 0, b, 0, a.Length);
                }

                IAsyncResult ar1 = stream.BeginWrite(a, 0, a.Length, null, null);
                stream.EndWrite(ar1);

                if (reconn && i % 100 == 0) {
                    if (!stream.TryReconn())
                        Assert.Fail();
                }

                IAsyncResult ar2 = stream.BeginRead(c, 0, c.Length, null, null);
                stream.EndRead(ar2);

                if (!BytesEquals(b, c))
                    Assert.Fail();
            }

            stream.Close();
        }

        private byte[] RandBytes(int n) {
            var b = new byte[n];
            rand.NextBytes(b);
            return b;
        }

        private bool BytesEquals(byte[] strA, byte[] strB) {
            int length = strA.Length;
            if (length != strB.Length) {
                return false;
            }
            for (int i = 0; i < length; i++) {
                if (strA[i] != strB[i])
                    return false;
            }
            return true;
        }
    }
}