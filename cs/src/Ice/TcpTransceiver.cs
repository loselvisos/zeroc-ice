// **********************************************************************
//
// Copyright (c) 2003-2008 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

namespace IceInternal
{

    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;

    sealed class TcpTransceiver : Transceiver
    {
        public Socket fd()
        {
            Debug.Assert(_fd != null);
            return _fd;
        }

        public bool restartable()
        {
            return true;
        }

        public bool initialize(AsyncCallback callback)
        {
            try
            {
                if(_state == StateNeedBeginConnect)
                {
                    Debug.Assert(callback != null);
                    Debug.Assert(_addr != null);

                    _state = StateNeedEndConnect;
                    _result = Network.doBeginConnectAsync(_fd, _addr, callback);

                    if(!_result.CompletedSynchronously)
                    {
                        //
                        // Return now if the I/O request needs an asynchronous callback.
                        //
                        return false;
                    }
                }

                if(_state == StateNeedEndConnect)
                {
                    Debug.Assert(_result != null);
                    Network.doEndConnectAsync(_result);
                    _result = null;
                    _state = StateConnected;
                    _desc = Network.fdToString(_fd);
                    if(_traceLevels.network >= 1)
                    {
                        string s = "tcp connection established\n" + _desc;
                        _logger.trace(_traceLevels.networkCat, s);
                    }
                }

                Debug.Assert(_state == StateConnected);
                return true;
            }
            catch(Ice.LocalException ex)
            {
                if(_traceLevels.network >= 2)
                {
                    string s = "failed to establish tcp connection\n" + Network.fdToString(_fd) + "\n" + ex;
                    _logger.trace(_traceLevels.networkCat, s);
                }
                throw;
            }
        }

        public void close()
        {
            //
            // If the transceiver is not connected, its description is simply "not connected",
            // which isn't very helpful.
            //
            if(_state == StateConnected && _traceLevels.network >= 1)
            {
                string s = "closing tcp connection\n" + ToString();
                _logger.trace(_traceLevels.networkCat, s);
            }

            Debug.Assert(_fd != null);
            try
            {
                _fd.Close();
            }
            catch(SocketException ex)
            {
                throw new Ice.SocketException(ex);
            }
            finally
            {
                _fd = null;
            }
        }

        public bool write(Buffer buf)
        {
            int size = buf.b.limit();
            int packetSize = size - buf.b.position();
            if(_maxPacketSize > 0 && packetSize > _maxPacketSize)
            {
                packetSize = _maxPacketSize;
                buf.b.limit(buf.b.position() + packetSize);
            }

            while(buf.b.hasRemaining())
            {
                try
                {
                    Debug.Assert(_fd != null);

                    int ret;
                    try
                    {
                        ret = _fd.Send(buf.b.rawBytes(), buf.b.position(), buf.b.remaining(), SocketFlags.None);
                    }
                    catch(Win32Exception e)
                    {
                        if(Network.wouldBlock(e))
                        {
                            //
                            // Writing would block, so we reset the limit (if necessary) and return true to indicate
                            // that more data must be sent.
                            //
                            if(packetSize == _maxPacketSize)
                            {
                                buf.b.limit(size);
                            }
                            return false;
                        }
                        throw;
                    }

                    Debug.Assert(ret > 0);

                    if(_traceLevels.network >= 3)
                    {
                        string s = "sent " + ret + " of " + buf.b.remaining() + " bytes via tcp\n" + ToString();
                        _logger.trace(_traceLevels.networkCat, s);
                    }

                    if(_stats != null)
                    {
                        _stats.bytesSent(type(), ret);
                    }

                    buf.b.position(buf.b.position() + ret);

                    if(packetSize == _maxPacketSize)
                    {
                        Debug.Assert(buf.b.position() == buf.b.limit());
                        packetSize = size - buf.b.position();
                        if(packetSize > _maxPacketSize)
                        {
                            packetSize = _maxPacketSize;
                        }
                        buf.b.limit(buf.b.position() + packetSize);
                    }
                }
                catch(SocketException ex)
                {
                    if(Network.connectionLost(ex))
                    {
                        throw new Ice.ConnectionLostException(ex);
                    }

                    throw new Ice.SocketException(ex);
                }
            }

            return true; // No more data to send.
        }

        public bool read(Buffer buf)
        {
            int remaining = buf.b.remaining();
            int position = buf.b.position();

            while(buf.b.hasRemaining())
            {
                try
                {
                    Debug.Assert(_fd != null);

                    int ret;
                    try
                    {
                        //
                        // Try to receive first. Much of the time, this will work and we
                        // avoid the cost of calling Poll().
                        //
                        ret = _fd.Receive(buf.b.rawBytes(), position, remaining, SocketFlags.None);
                        if(ret == 0)
                        {
                            throw new Ice.ConnectionLostException();
                        }
                    }
                    catch(Win32Exception e)
                    {
                        if(Network.wouldBlock(e))
                        {
                            return false;
                        }
                        throw;
                    }

                    Debug.Assert(ret > 0);

                    if(_traceLevels.network >= 3)
                    {
                        string s = "received " + ret + " of " + remaining + " bytes via tcp\n" + ToString();
                        _logger.trace(_traceLevels.networkCat, s);
                    }

                    if(_stats != null)
                    {
                        _stats.bytesReceived(type(), ret);
                    }

                    remaining -= ret;
                    buf.b.position(position += ret);
                }
                catch(Win32Exception ex)
                {
                    //
                    // On Mono, calling shutdownReadWrite() followed by read() causes Socket.Receive() to
                    // raise a socket exception with the "interrupted" error code. We need to check the
                    // socket's Connected status before checking for the interrupted case.
                    //
                    if(!_fd.Connected)
                    {
                        throw new Ice.ConnectionLostException(ex);
                    }

                    if(Network.interrupted(ex))
                    {
                        continue;
                    }

                    if(Network.connectionLost(ex))
                    {
                        throw new Ice.ConnectionLostException(ex);
                    }

                    throw new Ice.SocketException(ex);
                }
            }

            return true;
        }

        public IAsyncResult beginRead(Buffer buf, AsyncCallback callback, object state)
        {
            Debug.Assert(_fd != null);

            try
            {
                return _fd.BeginReceive(buf.b.rawBytes(), buf.b.position(), buf.b.remaining(), SocketFlags.None,
                                        callback, state);
            }
            catch(Win32Exception ex)
            {
                if(Network.connectionLost(ex))
                {
                    throw new Ice.ConnectionLostException(ex);
                }

                throw new Ice.SocketException(ex);
            }
        }

        public void endRead(Buffer buf, IAsyncResult result)
        {
            Debug.Assert(_fd != null);

            try
            {
                int ret = _fd.EndReceive(result);
                if(ret == 0)
                {
                    throw new Ice.ConnectionLostException();
                }

                Debug.Assert(ret > 0);

                if(_traceLevels.network >= 3)
                {
                    string s = "received " + ret + " of " + buf.b.remaining() + " bytes via tcp\n" + ToString();
                    _logger.trace(_traceLevels.networkCat, s);
                }

                if(_stats != null)
                {
                    _stats.bytesReceived(type(), ret);
                }

                buf.b.position(buf.b.position() + ret);
            }
            catch(Win32Exception ex)
            {
                if(Network.connectionLost(ex))
                {
                    throw new Ice.ConnectionLostException(ex);
                }

                if(Network.operationAborted(ex))
                {
                    throw new ReadAbortedException(ex);
                }

                throw new Ice.SocketException(ex);
            }
            catch(ObjectDisposedException ex)
            {
                throw new Ice.ConnectionLostException(ex);
            }
        }

        public IAsyncResult beginWrite(Buffer buf, AsyncCallback callback, object state)
        {
            Debug.Assert(_fd != null);

            try
            {
                return _fd.BeginSend(buf.b.rawBytes(), buf.b.position(), buf.b.remaining(), SocketFlags.None, callback,
                                     state);
            }
            catch(Win32Exception ex)
            {
                if(Network.connectionLost(ex))
                {
                    throw new Ice.ConnectionLostException(ex);
                }

                throw new Ice.SocketException(ex);
            }
            catch(ObjectDisposedException ex)
            {
                throw new Ice.ConnectionLostException(ex);
            }
        }

        public void endWrite(Buffer buf, IAsyncResult result)
        {
            Debug.Assert(_fd != null);

            try
            {
                int ret = _fd.EndSend(result);
                if(ret == 0)
                {
                    throw new Ice.ConnectionLostException();
                }

                Debug.Assert(ret > 0);

                if(_traceLevels.network >= 3)
                {
                    string s = "sent " + ret + " of " + buf.b.remaining() + " bytes via tcp\n" + ToString();
                    _logger.trace(_traceLevels.networkCat, s);
                }

                if(_stats != null)
                {
                    _stats.bytesSent(type(), ret);
                }

                buf.b.position(buf.b.position() + ret);
            }
            catch(Win32Exception ex)
            {
                if(Network.connectionLost(ex))
                {
                    throw new Ice.ConnectionLostException(ex);
                }

                throw new Ice.SocketException(ex);
            }
            catch(ObjectDisposedException ex)
            {
                throw new Ice.ConnectionLostException(ex);
            }
        }

        public string type()
        {
            return "tcp";
        }

        public void checkSendSize(Buffer buf, int messageSizeMax)
        {
            if(buf.size() > messageSizeMax)
            {
                throw new Ice.MemoryLimitException();
            }
        }

        public override string ToString()
        {
            return _desc;
        }

        //
        // Only for use by TcpConnector, TcpAcceptor
        //
        internal TcpTransceiver(Instance instance, Socket fd, IPEndPoint addr, bool connected)
        {
            _fd = fd;
            _addr = addr;
            _traceLevels = instance.traceLevels();
            _logger = instance.initializationData().logger;
            _stats = instance.initializationData().stats;
            _state = connected ? StateConnected : StateNeedBeginConnect;
            _desc = connected ? Network.fdToString(_fd) : "<not connected>";

            _maxPacketSize = 0;
            if(AssemblyUtil.platform_ == AssemblyUtil.Platform.Windows)
            {
		//
		// On Windows, limiting the buffer size is important to prevent
		// poor throughput performances when transfering large amount of
		// data. See Microsoft KB article KB823764.
		//
                _maxPacketSize = Network.getSendBufferSize(fd) / 2;
                if(_maxPacketSize < 512)
                {
                    _maxPacketSize = 0;
                }
            }
        }

        private Socket _fd;
        private IPEndPoint _addr;
        private TraceLevels _traceLevels;
        private Ice.Logger _logger;
        private Ice.Stats _stats;
        private string _desc;
        private int _state;
        private int _maxPacketSize;
        private IAsyncResult _result;

        private const int StateNeedBeginConnect = 0;
        private const int StateNeedEndConnect = 1;
        private const int StateConnected = 2;
    }

}