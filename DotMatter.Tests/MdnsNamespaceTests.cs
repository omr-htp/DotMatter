using System.IO;
using System.Net;
using System.Net.Sockets;
using DotMatter.Core.Mdns;

namespace DotMatter.Tests;

[TestFixture]
public class MdnsNamespaceTests
{
    [Test]
    public void DomainName_NullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DomainName((string)null!));
    }

    [TestCase(@"example\")]
    [TestCase(@"example\1")]
    [TestCase(@"example\12")]
    [TestCase(@"example\12x")]
    public void DomainName_InvalidEscapeSequence_ThrowsFormatException(string name)
    {
        Assert.Throws<FormatException>(() => new DomainName(name));
    }

    [Test]
    public void WireReader_ReadDomainName_RejectsInvalidCompressionPointer()
    {
        using var stream = new MemoryStream([0xC0, 0x01]);
        var reader = new WireReader(stream);

        var exception = Assert.Throws<InvalidDataException>(() => reader.ReadDomainName());

        Assert.That(exception!.Message, Does.Contain("pointer"));
    }

    [Test]
    public void Message_Clone_DoesNotMutateOriginalCollections()
    {
        var message = CreateQuery("_matter._tcp.local");
        message.Answers.Add(new PTRRecord
        {
            Name = "_matter._tcp.local",
            DomainName = "lamp._matter._tcp.local"
        });

        var clone = message.Clone<Message>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message.Answers, Has.Count.EqualTo(1));
            Assert.That(clone.Answers, Has.Count.EqualTo(1));
            Assert.That(ReferenceEquals(clone, message), Is.False);
        }
    }

    [Test]
    public void ServiceDiscovery_Unadvertise_DoesNotMutateProfileResources()
    {
        var mdns = new FakeMulticastService();
        using var discovery = new ServiceDiscovery(mdns);
        var profile = CreateProfile();
        var txtRecord = profile.Resources.OfType<TXTRecord>().Single();
        var originalTtl = TimeSpan.FromSeconds(7);
        txtRecord.TTL = originalTtl;

        discovery.Advertise(profile);
        discovery.Unadvertise(profile);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(txtRecord.TTL, Is.EqualTo(originalTtl));
            Assert.That(mdns.SentAnswers, Has.Count.EqualTo(1));
            Assert.That(
                mdns.SentAnswers[0].Message.AdditionalRecords.All(record => record.TTL == TimeSpan.Zero),
                Is.True);
        }
    }

    [Test]
    public void ServiceDiscovery_QueryResponse_DoesNotMutateAdvertisedResources()
    {
        var mdns = new FakeMulticastService();
        using var discovery = new ServiceDiscovery(mdns);
        var profile = CreateProfile();
        var txtRecord = profile.Resources.OfType<TXTRecord>().Single();
        var originalTtl = TimeSpan.FromSeconds(9);
        txtRecord.TTL = originalTtl;

        discovery.Advertise(profile);
        mdns.RaiseQuery(CreateQuery(profile.QualifiedServiceName));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mdns.SentAnswers, Has.Count.EqualTo(1));
            Assert.That(txtRecord.TTL, Is.EqualTo(originalTtl));
            Assert.That(
                mdns.SentAnswers[0].Message.AdditionalRecords.OfType<TXTRecord>().Single().TTL,
                Is.EqualTo(MulticastService.NonHostTTL));
        }
    }

    [Test]
    public void ServiceDiscovery_AdvertiseSameProfileTwice_DoesNotDuplicateAnswers()
    {
        var mdns = new FakeMulticastService();
        using var discovery = new ServiceDiscovery(mdns);
        var profile = CreateProfile();

        discovery.Advertise(profile);
        discovery.Advertise(profile);
        mdns.RaiseQuery(CreateQuery(profile.QualifiedServiceName));

        var response = mdns.SentAnswers.Single().Message;

        Assert.That(
            response.Answers.OfType<PTRRecord>().Count(ptr => ptr.DomainName == profile.FullyQualifiedName),
            Is.EqualTo(1));
    }

    [Test]
    public void MulticastService_SendQuery_ThrowsWhenNotStarted()
    {
        using var mdns = new MulticastService();

        var exception = Assert.Throws<InvalidOperationException>(() => mdns.SendQuery("example.local"));

        Assert.That(exception!.Message, Does.Contain("not been started"));
    }

    private static Message CreateQuery(DomainName name)
    {
        var message = new Message
        {
            QR = false,
            Opcode = MessageOperation.Query
        };

        message.Questions.Add(new Question
        {
            Name = name,
            Class = DnsClass.IN,
            Type = DnsType.PTR
        });

        return message;
    }

    private static ServiceProfile CreateProfile()
        => new("lamp", "_matter._tcp", 5540, [IPAddress.Parse("192.168.1.10")]);

    private sealed class FakeMulticastService : IMulticastService
    {
        public event EventHandler<MessageEventArgs>? QueryReceived;
        public event EventHandler<MessageEventArgs>? AnswerReceived;
        public event EventHandler<byte[]>? MalformedMessage
        {
            add { }
            remove { }
        }

        public event EventHandler<NetworkInterfaceEventArgs>? NetworkInterfaceDiscovered
        {
            add { }
            remove { }
        }

        public bool UseIpv4 { get; set; }
        public bool UseIpv6 { get; set; }
        public bool IgnoreDuplicateMessages { get; set; }

        public List<Message> SentQueries { get; } = [];
        public List<(Message Message, IPEndPoint EndPoint)> SentAnswers { get; } = [];

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void SendQuery(DomainName name, DnsClass @class = DnsClass.IN, DnsType type = DnsType.ANY)
            => SendQuery(CreateQuery(name, @class, type));

        public void SendUnicastQuery(DomainName name, DnsClass @class = DnsClass.IN, DnsType type = DnsType.ANY)
            => SendQuery(CreateQuery(name, (DnsClass)((ushort)@class | MulticastService.UNICAST_RESPONSE_BIT), type));

        public void SendQuery(Message msg)
            => SentQueries.Add(msg.Clone<Message>());

        public void SendAnswer(Message answer, bool checkDuplicate = true, IPEndPoint? unicastEndpoint = null)
            => SentAnswers.Add((PrepareAnswer(answer), unicastEndpoint ?? new IPEndPoint(IPAddress.Loopback, 5353)));

        public void SendAnswer(Message answer, MessageEventArgs query, bool checkDuplicate = true, IPEndPoint? endPoint = null)
            => SentAnswers.Add((PrepareAnswer(answer), endPoint ?? query.RemoteEndPoint));

        public void OnDnsMessage(object sender, UdpReceiveResult result)
        {
        }

        public void RaiseQuery(Message message)
            => QueryReceived?.Invoke(this, new MessageEventArgs
            {
                Message = message,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5353)
            });

        public void RaiseAnswer(Message message)
            => AnswerReceived?.Invoke(this, new MessageEventArgs
            {
                Message = message,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5353)
            });

        public void Dispose()
        {
        }

        private static Message PrepareAnswer(Message answer)
        {
            var clone = answer.Clone<Message>();
            clone.AA = true;
            clone.Id = 0;
            clone.Opcode = MessageOperation.Query;
            clone.RA = false;
            clone.AD = false;
            clone.CD = false;
            clone.Questions.Clear();

            foreach (var record in clone.Answers.Concat(clone.AdditionalRecords).Concat(clone.AuthorityRecords))
            {
                if (record.TTL == TimeSpan.Zero)
                {
                    continue;
                }

                record.TTL = record.Type switch
                {
                    DnsType.A or DnsType.AAAA or DnsType.SRV or DnsType.HINFO or DnsType.PTR => MulticastService.HostRecordTTL,
                    _ => MulticastService.NonHostTTL
                };
            }

            return clone;
        }

        private static Message CreateQuery(DomainName name, DnsClass @class, DnsType type)
        {
            var message = new Message
            {
                QR = false,
                Opcode = MessageOperation.Query
            };

            message.Questions.Add(new Question
            {
                Name = name,
                Class = @class,
                Type = type
            });

            return message;
        }
    }
}
