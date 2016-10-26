using System;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Snet {
    public class SnetStream : Stream {

        private ulong     _ID;
        private string    _Host;
        private int       _Port;
        private byte[]    _Key = new byte[8];
        private bool      _EnableCrypt;
        private RC4Cipher _ReadCipher;
        private RC4Cipher _WriteCipher;

        private Mutex            _ReadLock   = new Mutex();
        private Mutex            _WriteLock  = new Mutex();
        private ReaderWriterLock _ReconnLock = new ReaderWriterLock();

        private NetworkStream _BaseStream;
        private Rewriter      _Rewriter;
        private Rereader      _Rereader;

        private ulong _ReadCount;
        private ulong _WriteCount;

        private bool _Closed;


        public SnetStream(int size, bool enableCrypt) {
            _EnableCrypt = enableCrypt;
            _Rewriter = new Rewriter(size);
            _Rereader = new Rereader();
        }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return false; }
        }

        public override bool CanWrite {
            get { return true; }
        }

        public override long Length {
            get { throw new NotSupportedException(); }
        }

        public override long Position {
            get {
                throw new NotSupportedException();
            }
            set {
                throw new NotSupportedException();
            }
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        internal class AsyncResult : IAsyncResult {

            internal AsyncCallback Callback { get; set; }
            public object AsyncState { get; internal set; }

            public WaitHandle AsyncWaitHandle { get; internal set; }
            public bool CompletedSynchronously { get { return false; } }
            public bool IsCompleted { get; internal set; }
            internal int ReadCount { get; set; }
            internal Exception Error { get; set; }

            internal AsyncResult(AsyncCallback callback, object state) {
                this.Callback = callback;
                this.AsyncState = state;
                this.IsCompleted = false;
                this.AsyncWaitHandle = new ManualResetEvent(false);
            }

            internal int Wait() {
                AsyncWaitHandle.WaitOne();
                if (Error != null)
                    throw Error;
                return ReadCount;
            }
        }

        public IAsyncResult BeginConnect(string host, int port, AsyncCallback callback, object state) {
            if (_BaseStream != null)
                throw new InvalidOperationException();

            AsyncResult ar1 = new AsyncResult(callback, state);
            ThreadPool.QueueUserWorkItem((object ar2) => {
                AsyncResult ar3 = (AsyncResult)ar2;
                try {
                    Connect(host, port);
                } catch (Exception ex) {
                    ar3.Error = ex;
                }
                ((ManualResetEvent)ar3.AsyncWaitHandle).Set();
                if (ar3.Callback != null)
                    ar3.Callback(ar3);
            }, ar1);

            return ar1;
        }

        public void WaitConnect(IAsyncResult asyncResult) {
            ((AsyncResult)asyncResult).Wait();
        }

        public void Connect(string host, int port) {
            if (_BaseStream != null)
                throw new InvalidCastException();

            _Host = host;
            _Port = port;
            Handshake();
        }

        private void Handshake() {
            byte[] request = new byte[24 + 16];
            byte[] response = request;

            ulong privateKey;
            ulong publicKey;
            DH64 dh64 = new DH64();
            dh64.KeyPair(out privateKey, out publicKey);

            using (MemoryStream ms = new MemoryStream(request, 8, 8)) {
                using (BinaryWriter bw = new BinaryWriter(ms)) {
                    bw.Write(publicKey);
                }
            }

            TcpClient client = new TcpClient(_Host, _Port);
            SetBaseStream(client.GetStream());
            _BaseStream.Write(request, 0, request.Length);

            for (int n = 16; n > 0; ) {
                n -= _BaseStream.Read(response, 16 - n, n);
            }

            using (MemoryStream ms = new MemoryStream(response, 0, 16)) {
                using (BinaryReader br = new BinaryReader(ms)) {
                    ulong serverPublicKey = br.ReadUInt64();
                    ulong secret = dh64.Secret(privateKey, serverPublicKey);

                    using (MemoryStream ms2 = new MemoryStream(_Key)) {
                        using (BinaryWriter bw = new BinaryWriter(ms2)) {
                            bw.Write(secret);
                        }
                    }

                    _ReadCipher = new RC4Cipher(_Key);
                    _WriteCipher = new RC4Cipher(_Key);
                    _ReadCipher.XORKeyStream(response, 8, response, 8, 8);
                    _ID = br.ReadUInt64();
                }
            }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) {
            AsyncResult ar1 = new AsyncResult(callback, state);
            ThreadPool.QueueUserWorkItem((object ar2) => {
                AsyncResult ar3 = (AsyncResult)ar2;
                try {
                    while (ar3.ReadCount != count) {
                        ar3.ReadCount += Read(buffer, offset + ar3.ReadCount, count - ar3.ReadCount);
                    }
                    ar3.IsCompleted = true;
                } catch (Exception ex) {
                    ar3.Error = ex;
                }
                ((ManualResetEvent)ar3.AsyncWaitHandle).Set();
                if (ar3.Callback != null)
                    ar3.Callback(ar3);
            }, ar1);
            return ar1;
        }

        public override int EndRead(IAsyncResult asyncResult) {
            return ((AsyncResult)asyncResult).Wait();
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) {
            AsyncResult ar1 = new AsyncResult(callback, state);
            ThreadPool.QueueUserWorkItem((object ar2) => {
                AsyncResult ar3 = (AsyncResult)ar2;
                try {
                    Write(buffer, offset, count);
                    ar3.IsCompleted = true;
                } catch (Exception ex) {
                    ar3.Error = ex;
                }
                ((ManualResetEvent)ar3.AsyncWaitHandle).Set();
                if (ar3.Callback != null)
                    ar3.Callback(ar3);
            }, ar1);
            return ar1;
        }

        public override void EndWrite(IAsyncResult asyncResult) {
            ((AsyncResult)asyncResult).Wait();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            _ReadLock.WaitOne();
            _ReconnLock.AcquireReaderLock(-1);
            int n = 0;
            try {
                for (; ; ) {
                    n = _Rereader.Pull(buffer, offset, count);
                    if (n > 0) {
                        return n;
                    }

                    try {
                        n = _BaseStream.Read(buffer, offset + n, count);
                        if (n == 0) {
                            if (!TryReconnInternal())
                                throw new IOException();
                            continue;
                        }
                    } catch {
                        if(!TryReconnInternal())
                            throw;
                        continue;
                    }
                    break;
                }
            } finally {
                if (n > 0 && _EnableCrypt) {
                    _ReadCipher.XORKeyStream(buffer, offset, buffer, offset, n);
                }
                _ReadCount += (ulong)n;
                _ReconnLock.ReleaseReaderLock();
                _ReadLock.ReleaseMutex();
            }
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            if (count == 0)
                return;

            _WriteLock.WaitOne();
            _ReconnLock.AcquireReaderLock(-1);

            try {
                if (_EnableCrypt) {
                    _WriteCipher.XORKeyStream(buffer, offset, buffer, offset, count);
                }
                _Rewriter.Push(buffer, offset, count);
                _WriteCount += (ulong)count;

                try {
                    _BaseStream.Write(buffer, offset, count);
                } catch {
                    if(!TryReconnInternal())
                        throw;
                }
            } finally {
                _ReconnLock.ReleaseReaderLock();
                _WriteLock.ReleaseMutex();
            }
        }

        public bool TryReconn() {
            _ReconnLock.AcquireReaderLock(-1);
            bool result = TryReconnInternal();
            _ReconnLock.ReleaseReaderLock();
            return result;
        }

        private bool TryReconnInternal() {
            _BaseStream.Close();
            NetworkStream badStream = _BaseStream;

            _ReconnLock.ReleaseReaderLock();
            _ReconnLock.AcquireWriterLock(-1);

            try {
                if (badStream != _BaseStream)
                    return true;

                byte[] request = new byte[24 + 16];
                byte[] response = new byte[16];
                using (MemoryStream ms = new MemoryStream(request)) {
                    using (BinaryWriter bw = new BinaryWriter(ms)) {
                        bw.Write(_ID);
                        bw.Write(_WriteCount);
                        bw.Write(_ReadCount);
                        bw.Write(_Key);
                    }
                }

                MD5 md5 = MD5CryptoServiceProvider.Create();
                byte[] hash = md5.ComputeHash(request, 0, 32);
                Buffer.BlockCopy(hash, 0, request, 24, hash.Length);

                for (int i = 0; !_Closed; i++) {
                    if (i > 0) {
                        Thread.Sleep(3000);
                    }

                    try {
                        TcpClient client = new TcpClient(_Host, _Port);
                        NetworkStream stream = client.GetStream();
                        stream.Write(request, 0, request.Length);

                        for (int n = response.Length; n > 0;) {
                            n -= stream.Read(response, response.Length - n, n);
                        }

                        ulong writeCount = 0;
                        ulong readCount = 0;
                        using (MemoryStream ms = new MemoryStream(response)) {
                            using (BinaryReader br = new BinaryReader(ms)) {
                                writeCount = br.ReadUInt64();
                                readCount = br.ReadUInt64();
                            }
                        }

                        if (DoReconn(stream, writeCount, readCount))
                            return true;
                    } catch {
                        continue;
                    }
                }

            } finally {
                _ReconnLock.ReleaseWriterLock();
                _ReconnLock.AcquireReaderLock(-1);
            }
            return false;
        }

        private bool DoReconn(NetworkStream stream, ulong writeCount, ulong readCount) {
            if (writeCount < _ReadCount)
                return false;

            if (_WriteCount < readCount)
                return false;

            Thread thread = null;
            bool rereadSuccess = false;

            if (writeCount != _ReadCount) {
                thread = new Thread(() => {
                    int n = (int)writeCount - (int)_ReadCount;
                    rereadSuccess = _Rereader.Reread(stream, n);
                });
                thread.Start();
            }

            if (_WriteCount != readCount) {
                if (!_Rewriter.Rewrite(stream, _WriteCount, readCount))
                    return false;
            }

            if (thread != null) {
                thread.Join();
                if (!rereadSuccess)
                    return false;
            }

            SetBaseStream(stream);
            return true;
        }

        public override void Flush() {
            _WriteLock.WaitOne();
            _ReconnLock.AcquireReaderLock(-1);
            try {
                _BaseStream.Flush();
            } catch {
                if (!TryReconnInternal())
                    throw;
            } finally {
                _ReconnLock.ReleaseReaderLock();
                _WriteLock.ReleaseMutex();
            }
        }

        public override void Close() {
            _Closed = true;
            _BaseStream.Close();
        }

        public override int WriteTimeout {
            get {
                return _BaseStream.WriteTimeout;
            }
            set {
                _BaseStream.WriteTimeout = value;
            }
        }

        public override int ReadTimeout {
            get {
                return _BaseStream.ReadTimeout;
            }
            set {
                _BaseStream.ReadTimeout = value;
            }
        }

        private void SetBaseStream(NetworkStream stream) {
            _BaseStream = stream;
        }

    }
}
