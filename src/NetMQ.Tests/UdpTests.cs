using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetMQ.Sockets;
using Xunit;
using Xunit.Abstractions;

namespace NetMQ.Tests
{
    public class UdpTests : IClassFixture<CleanupAfterFixture>
    {
        private readonly ITestOutputHelper _output;

        public UdpTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RawUdpSendReceive()
        {
            // Verify that raw UDP sockets work on this platform
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.Bind(new IPEndPoint(IPAddress.Any, 15600));
            receiver.Blocking = false;

            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.SendTo(new byte[] { 1, 2, 3 }, new IPEndPoint(IPAddress.Loopback, 15600));

            Thread.Sleep(50);

            Assert.True(receiver.Poll(1000_000, SelectMode.SelectRead), "Data should be available");

            var buffer = new byte[100];
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            int received = receiver.ReceiveFrom(buffer, ref ep);
            Assert.Equal(3, received);
            Assert.Equal(1, buffer[0]);
        }
        [Fact]
        public void RadioSendsUdpDatagram()
        {
            // Verify the Radio socket actually sends a UDP datagram
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.Bind(new IPEndPoint(IPAddress.Any, 15601));
            receiver.Blocking = false;

            using var radio = new RadioSocket();
            radio.Connect("udp://127.0.0.1:15601");

            Thread.Sleep(500);

            radio.Send("test", "Hello");

            // Wait and check if the raw UDP socket received anything
            Thread.Sleep(200);

            bool hasData = receiver.Poll(2_000_000, SelectMode.SelectRead);
            _output.WriteLine($"Has data: {hasData}");

            if (hasData)
            {
                var buffer = new byte[1024];
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                int received = receiver.ReceiveFrom(buffer, ref ep);
                _output.WriteLine($"Received {received} bytes: [{string.Join(",", buffer.AsSpan(0, received).ToArray())}]");
            }

            Assert.True(hasData, "Radio should send a UDP datagram");
        }

        [Fact]
        public void RadioDishUdpBasic()
        {
            using var dish = new DishSocket();
            using var radio = new RadioSocket();

            dish.Join("test");

            int port = 15500;
            dish.Bind($"udp://*:{port}");
            radio.Connect($"udp://127.0.0.1:{port}");

            // Allow time for connection setup
            Thread.Sleep(1000);

            _output.WriteLine("Sending message...");
            radio.Send("test", "Hello UDP");
            _output.WriteLine("Message sent, waiting for receive...");

            bool received = dish.TryReceiveString(TimeSpan.FromSeconds(10), out string? group, out string? message);
            _output.WriteLine($"Received: {received}, group={group}, message={message}");
            Assert.True(received, "Should receive UDP message");
            Assert.Equal("test", group);
            Assert.Equal("Hello UDP", message);
        }

        [Fact]
        public void RadioDishUdpMultipleGroups()
        {
            using var dish = new DishSocket();
            using var radio = new RadioSocket();

            dish.Join("groupA");
            // NOT joining "groupB" - messages to that group should be filtered out

            int port = 15501;
            dish.Bind($"udp://*:{port}");
            radio.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(200);

            // Send to groupB first (should be filtered by Dish)
            radio.Send("groupB", "Filtered");
            // Then send to groupA (should be received)
            radio.Send("groupA", "Received");

            var (group, msg) = dish.ReceiveString();
            Assert.Equal("groupA", group);
            Assert.Equal("Received", msg);
        }

        [Fact]
        public void RadioDishUdpMultipleMessages()
        {
            using var dish = new DishSocket();
            using var radio = new RadioSocket();

            dish.Join("ch");

            int port = 15502;
            dish.Bind($"udp://*:{port}");
            radio.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(200);

            int messageCount = 5;
            for (int i = 0; i < messageCount; i++)
            {
                radio.Send("ch", $"msg-{i}");
            }

            for (int i = 0; i < messageCount; i++)
            {
                bool received = dish.TryReceiveString(TimeSpan.FromSeconds(3), out string? group, out string? message);
                Assert.True(received, $"Should receive message {i}");
                Assert.Equal("ch", group);
                Assert.Equal($"msg-{i}", message);
            }
        }

        [Fact]
        public void UdpProtocolNotSupportedForStreamSocket()
        {
            // Stream socket should not support UDP
            using var stream = new StreamSocket();
            Assert.Throws<ProtocolNotSupportedException>(() => stream.Bind("udp://*:15510"));
        }

        #region REQ/REP UDP Tests

        [Fact]
        public void ReqRepUdpBasic()
        {
            using var rep = new ResponseSocket();
            using var req = new RequestSocket();

            int port = 15520;
            rep.Bind($"udp://*:{port}");
            req.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            req.SendFrame("Hello");

            bool received = rep.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? request);
            Assert.True(received, "REP should receive request");
            Assert.Equal("Hello", request);

            rep.SendFrame("World");

            received = req.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? reply);
            Assert.True(received, "REQ should receive reply");
            Assert.Equal("World", reply);
        }

        [Fact]
        public void ReqRepUdpMultipleExchanges()
        {
            using var rep = new ResponseSocket();
            using var req = new RequestSocket();

            int port = 15521;
            rep.Bind($"udp://*:{port}");
            req.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            for (int i = 0; i < 3; i++)
            {
                req.SendFrame($"request-{i}");

                bool received = rep.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? request);
                Assert.True(received, $"REP should receive request {i}");
                Assert.Equal($"request-{i}", request);

                rep.SendFrame($"reply-{i}");

                received = req.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? reply);
                Assert.True(received, $"REQ should receive reply {i}");
                Assert.Equal($"reply-{i}", reply);
            }
        }

        #endregion

        #region PUSH/PULL UDP Tests

        [Fact]
        public void PushPullUdpBasic()
        {
            using var pull = new PullSocket();
            using var push = new PushSocket();

            int port = 15530;
            pull.Bind($"udp://*:{port}");
            push.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            push.SendFrame("task-1");

            bool received = pull.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? message);
            Assert.True(received, "PULL should receive message");
            Assert.Equal("task-1", message);
        }

        [Fact]
        public void PushPullUdpMultipleMessages()
        {
            using var pull = new PullSocket();
            using var push = new PushSocket();

            int port = 15531;
            pull.Bind($"udp://*:{port}");
            push.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            for (int i = 0; i < 5; i++)
                push.SendFrame($"task-{i}");

            for (int i = 0; i < 5; i++)
            {
                bool received = pull.TryReceiveFrameString(TimeSpan.FromSeconds(3), out string? message);
                Assert.True(received, $"PULL should receive message {i}");
                Assert.Equal($"task-{i}", message);
            }
        }

        #endregion

        #region PAIR UDP Tests

        [Fact]
        public void PairUdpBidirectional()
        {
            using var pairA = new PairSocket();
            using var pairB = new PairSocket();

            int port = 15540;
            pairA.Bind($"udp://*:{port}");
            pairB.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            // B sends to A
            pairB.SendFrame("from-B");
            bool received = pairA.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? msgA);
            Assert.True(received, "PairA should receive from PairB");
            Assert.Equal("from-B", msgA);

            // A replies to B
            pairA.SendFrame("from-A");
            received = pairB.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? msgB);
            Assert.True(received, "PairB should receive from PairA");
            Assert.Equal("from-A", msgB);
        }

        #endregion

        #region DEALER/ROUTER UDP Tests

        [Fact]
        public void DealerRouterUdpBasic()
        {
            using var router = new RouterSocket();
            using var dealer = new DealerSocket();

            int port = 15550;
            router.Bind($"udp://*:{port}");
            dealer.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            // Dealer sends (no identity frame needed)
            dealer.SendFrame("hello");

            // Router receives: [identity][empty][message]
            bool received = router.TryReceiveFrameBytes(TimeSpan.FromSeconds(5), out byte[]? identity);
            Assert.True(received, "Router should receive identity frame");
            Assert.NotNull(identity);

            string message = router.ReceiveFrameString();
            Assert.Equal("hello", message);

            // Router replies
            router.SendMoreFrame(identity!);
            router.SendFrame("world");

            received = dealer.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? reply);
            Assert.True(received, "Dealer should receive reply");
            Assert.Equal("world", reply);
        }

        #endregion

        [Fact]
        public void PubSubUdpBasic()
        {
            using var sub = new SubscriberSocket();
            using var pub = new PublisherSocket();

            sub.Subscribe("weather");

            int port = 15510;
            sub.Bind($"udp://*:{port}");
            pub.Connect($"udp://127.0.0.1:{port}");

            // Allow time for connection setup
            Thread.Sleep(1000);

            pub.SendMoreFrame("weather").SendFrame("sunny 25C");

            bool received = sub.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? topic);
            Assert.True(received, "Should receive UDP pub/sub topic");
            Assert.Equal("weather", topic);

            string body = sub.ReceiveFrameString();
            Assert.Equal("sunny 25C", body);
        }

        [Fact]
        public void PubSubUdpTopicFiltering()
        {
            using var sub = new SubscriberSocket();
            using var pub = new PublisherSocket();

            sub.Subscribe("sports");  // only subscribe to "sports"

            int port = 15511;
            sub.Bind($"udp://*:{port}");
            pub.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            // Send weather first (should be filtered by SUB)
            pub.SendMoreFrame("weather").SendFrame("rainy");
            // Then send sports (should be received)
            pub.SendMoreFrame("sports").SendFrame("goal scored");

            bool received = sub.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? topic);
            Assert.True(received, "Should receive sports message");
            Assert.Equal("sports", topic);

            string body = sub.ReceiveFrameString();
            Assert.Equal("goal scored", body);
        }

        [Fact]
        public void PubSubUdpSubscribeAll()
        {
            using var sub = new SubscriberSocket();
            using var pub = new PublisherSocket();

            sub.Subscribe("");  // subscribe to everything

            int port = 15512;
            sub.Bind($"udp://*:{port}");
            pub.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(1000);

            pub.SendMoreFrame("anything").SendFrame("hello");

            bool received = sub.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? topic);
            Assert.True(received, "Should receive message with subscribe-all");
            Assert.Equal("anything", topic);

            string body = sub.ReceiveFrameString();
            Assert.Equal("hello", body);
        }

        [Fact]
        public void RadioDishUdpEmptyMessage()
        {
            using var dish = new DishSocket();
            using var radio = new RadioSocket();

            dish.Join("g");

            int port = 15503;
            dish.Bind($"udp://*:{port}");
            radio.Connect($"udp://127.0.0.1:{port}");

            Thread.Sleep(200);

            radio.Send("g", "");

            bool received = dish.TryReceiveString(TimeSpan.FromSeconds(3), out string? group, out string? message);
            Assert.True(received, "Should receive empty UDP message");
            Assert.Equal("g", group);
            Assert.Equal("", message);
        }
    }
}
