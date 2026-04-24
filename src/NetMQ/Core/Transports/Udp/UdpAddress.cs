using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace NetMQ.Core.Transports.Udp
{
    internal sealed class UdpAddress : Address.IZAddress
    {
        public override string ToString()
        {
            if (Address == null)
                return string.Empty;

            var endpoint = Address;

            return endpoint.AddressFamily == AddressFamily.InterNetworkV6
                ? Protocol + "://[" + endpoint.Address + "]:" + endpoint.Port
                : Protocol + "://" + endpoint.Address + ":" + endpoint.Port;
        }

        public void Resolve(string name, bool ip4Only)
        {
            int delimiter = name.LastIndexOf(':');
            if (delimiter < 0)
                throw new InvalidException($"UdpAddress.Resolve, delimiter ({delimiter}) must be non-negative.");

            string addrStr = name.Substring(0, delimiter);
            string portStr = name.Substring(delimiter + 1);

            if (addrStr.Length >= 2 && addrStr[0] == '[' && addrStr[addrStr.Length - 1] == ']')
                addrStr = addrStr.Substring(1, addrStr.Length - 2);

            int port;
            if (portStr == "*" || portStr == "0")
            {
                port = 0;
            }
            else
            {
                port = Convert.ToInt32(portStr);
                if (port == 0)
                    throw new InvalidException($"UdpAddress.Resolve, port ({portStr}) must be a valid nonzero integer.");
            }

            IPAddress? ipAddress;

            if (addrStr == "*")
            {
                ipAddress = ip4Only ? IPAddress.Any : IPAddress.IPv6Any;
            }
            else if (!IPAddress.TryParse(addrStr, out ipAddress))
            {
                var availableAddresses = Dns.GetHostEntry(addrStr).AddressList;

                ipAddress = ip4Only
                    ? availableAddresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    : availableAddresses.FirstOrDefault(ip =>
                        ip.AddressFamily == AddressFamily.InterNetwork ||
                        ip.AddressFamily == AddressFamily.InterNetworkV6);

                if (ipAddress == null)
                    throw new InvalidException($"UdpAddress.Resolve, unable to find an IP address for {name}");
            }

            Address = new IPEndPoint(ipAddress, port);
        }

        public IPEndPoint? Address { get; private set; }

        public string Protocol => Core.Address.UdpProtocol;
    }
}
