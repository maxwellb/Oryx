// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace Oryx.Tests.Common
{
    public static class PortHelper
    {
        public static int GetNextPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                // Specifying '0' for port here to get any available port
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port: 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }
    }
}
