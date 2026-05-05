#nullable disable
namespace DotMatter.Core.Mdns;

/// <summary>
///   DNS based Service Discovery is a way of using standard DNS programming interfaces, servers,
///   and packet formats to browse the network for services.
/// </summary>
/// <seealso href="https://tools.ietf.org/html/rfc6763">RFC 6763 DNS-Based Service Discovery</seealso>
public class ServiceDiscovery : IServiceDiscovery
{
    private static readonly DomainName _localDomain = new("local");
    private static readonly DomainName _subName = new("_sub");

    /// <summary>
    ///   The service discovery service name.
    /// </summary>
    /// <value>
    ///   The service name used to enumerate other services.
    /// </value>
    public static readonly DomainName ServiceName = new("_services._dns-sd._udp.local");

    private bool _disposed;
    private readonly bool _instantiatedMdns;
    private readonly List<ResourceRecord> _records = [];
    private readonly Dictionary<ServiceProfile, List<ResourceRecord>> _advertisedRecords = [];

    /// <summary>
    ///   Creates a new instance of the <see cref="ServiceDiscovery"/> class.
    /// </summary>
    public ServiceDiscovery()
        : this(new MulticastService())
    {
        _instantiatedMdns = true;

        // Auto start.
        Mdns.Start();
    }

    /// <summary>
    ///   Creates a new instance of the <see cref="ServiceDiscovery"/> class with
    ///   the specified <see cref="IMulticastService"/>.
    /// </summary>
    /// <param name="mdns">
    ///   The underlying <see cref="IMulticastService"/> to use.
    /// </param>
    public ServiceDiscovery(IMulticastService mdns)
    {
        ArgumentNullException.ThrowIfNull(mdns);

        Mdns = mdns;
        mdns.QueryReceived += OnQuery;
        mdns.AnswerReceived += OnAnswer;
    }

    /// <summary>
    ///   Gets the multicasting service.
    /// </summary>
    /// <value>
    ///   Is used to send and receive multicast <see cref="Message">DNS messages</see>.
    /// </value>
    public IMulticastService Mdns
    {
        get; private set;
    }

    /// <summary>
    ///   Add the additional records into the answers.
    /// </summary>
    /// <value>
    ///   Defaults to <b>false</b>.
    /// </value>
    /// <remarks>
    ///   Some malformed systems, such as js-ipfs and go-ipfs, only examine
    ///   the <see cref="Message.Answers"/> and not the <see cref="Message.AdditionalRecords"/>.
    ///   Setting this to <b>true</b>, will move the additional records
    ///   into the answers.
    ///   <para>
    ///   This never done for DNS-SD answers.
    ///   </para>
    /// </remarks>
    public bool AnswersContainsAdditionalRecords
    {
        get; set;
    }

    /// <summary>
    ///   Raised when a DNS-SD response is received.
    /// </summary>
    /// <value>
    ///   Contains the service name.
    /// </value>
    /// <remarks>
    ///   <b>ServiceDiscovery</b> passively monitors the network for any answers
    ///   to a DNS-SD query. When an answer is received this event is raised.
    ///   <para>
    ///   Use <see cref="QueryAllServices"/> to initiate a DNS-SD question.
    ///   </para>
    /// </remarks>
    public event EventHandler<DomainName> ServiceDiscovered;

    /// <summary>
    ///   Raised when a service instance is discovered.
    /// </summary>
    /// <value>
    ///   Contains the service instance name.
    /// </value>
    /// <remarks>
    ///   <b>ServiceDiscovery</b> passively monitors the network for any answers.
    ///   When an answer containing a PTR to a service instance is received
    ///   this event is raised.
    /// </remarks>
    public event EventHandler<ServiceInstanceDiscoveryEventArgs> ServiceInstanceDiscovered;

    /// <summary>
    ///   Raised when a service instance is shutting down.
    /// </summary>
    /// <value>
    ///   Contains the service instance name.
    /// </value>
    /// <remarks>
    ///   <b>ServiceDiscovery</b> passively monitors the network for any answers.
    ///   When an answer containing a PTR to a service instance with a
    ///   TTL of zero is received this event is raised.
    /// </remarks>
    public event EventHandler<ServiceInstanceShutdownEventArgs> ServiceInstanceShutdown;

    /// <summary>
    ///    Asks other MDNS services to send their service names.
    /// </summary>
    /// <remarks>
    ///   When an answer is received the <see cref="ServiceDiscovered"/> event is raised.
    /// </remarks>
    public void QueryAllServices()
    {
        ThrowIfDisposed();
        Mdns.SendQuery(ServiceName, type: DnsType.PTR);
    }

    /// <summary>
    ///    Asks other MDNS services to send their service names;
    ///    accepts unicast and/or broadcast answers.
    /// </summary>
    /// <remarks>
    ///   When an answer is received the <see cref="ServiceDiscovered"/> event is raised.
    /// </remarks>
    public void QueryUnicastAllServices()
    {
        ThrowIfDisposed();
        Mdns.SendUnicastQuery(ServiceName, type: DnsType.PTR);
    }

    /// <summary>
    ///   Asks instances of the specified service to send details.
    /// </summary>
    /// <param name="service">
    ///   The service name to query. Typically of the form "_<i>service</i>._tcp".
    /// </param>
    /// <remarks>
    ///   When an answer is received the <see cref="ServiceInstanceDiscovered"/> event is raised.
    /// </remarks>
    /// <seealso cref="ServiceProfile.ServiceName"/>
    public void QueryServiceInstances(DomainName service)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(service);

        Mdns.SendQuery(DomainName.Join(service, _localDomain), type: DnsType.PTR);
    }

    /// <summary>
    ///   Asks instances of the specified service with the subtype to send details.
    /// </summary>
    /// <param name="service">
    ///   The service name to query. Typically of the form "_<i>service</i>._tcp".
    /// </param>
    /// <param name="subtype">
    ///   The feature that is needed.
    /// </param>
    /// <remarks>
    ///   When an answer is received the <see cref="ServiceInstanceDiscovered"/> event is raised.
    /// </remarks>
    /// <seealso cref="ServiceProfile.ServiceName"/>
    public void QueryServiceInstances(DomainName service, string subtype)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtype);

        var name = DomainName.Join(
            new DomainName(subtype),
            _subName,
            service,
            _localDomain);
        Mdns.SendQuery(name, type: DnsType.PTR);
    }

    /// <summary>
    ///   Asks instances of the specified service to send details.
    ///   accepts unicast and/or broadcast answers.
    /// </summary>
    /// <param name="service">
    ///   The service name to query. Typically of the form "_<i>service</i>._tcp".
    /// </param>
    /// <remarks>
    ///   When an answer is received the <see cref="ServiceInstanceDiscovered"/> event is raised.
    /// </remarks>
    /// <seealso cref="ServiceProfile.ServiceName"/>
    public void QueryUnicastServiceInstances(DomainName service)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(service);

        Mdns.SendUnicastQuery(DomainName.Join(service, _localDomain), type: DnsType.PTR);
    }

    /// <summary>
    ///   Advertise a service profile.
    /// </summary>
    /// <param name="service">
    ///   The service profile.
    /// </param>
    /// <remarks>
    ///   Any queries for the service or service instance will be answered with
    ///   information from the profile.
    ///   <para>
    ///   Besides adding the profile's resource records, PTR records are
    ///   created to support DNS-SD and reverse address mapping (DNS address lookup).
    ///   </para>
    /// </remarks>
    public void Advertise(ServiceProfile service)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(service);

        if (_advertisedRecords.TryGetValue(service, out var existingRecords))
        {
            RemoveAdvertisedRecords(service, existingRecords);
        }

        var advertisedRecords = CreateAdvertisedRecords(service);
        _records.AddRange(advertisedRecords);
        _advertisedRecords[service] = advertisedRecords;
    }

    /// <summary>
    /// Probe the network to ensure the service is unique. Shared profiles should skip this step.
    /// </summary>
    /// <param name="profile"></param>
    /// <returns>True if this service conflicts with an existing network service</returns>
    /// <exception cref="InvalidOperationException">Thrown if a shared profile is probed</exception>
    public async Task<bool> ProbeAsync(ServiceProfile profile)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SharedProfile)
        {
            throw new InvalidOperationException("Shared profiles should not be probed");
        }

        bool conflict = false;
        void handler(object s, MessageEventArgs e)
        {
            foreach (ResourceRecord answer in e.Message.Answers)
            {
                if ((DnsClass)((ushort)answer.Class & ~MulticastService.CACHE_FLUSH_BIT) == DnsClass.IN && answer.Name.Equals(profile.HostName))
                {
                    conflict = true;
                    return;
                }
            }
        }

        Mdns.AnswerReceived += handler;
        try
        {
            await Task.Delay(Random.Shared.Next(0, 250)).ConfigureAwait(false);
            for (var attempt = 0; attempt < 3 && !conflict; attempt++)
            {
                Mdns.SendUnicastQuery(profile.HostName);
                await Task.Delay(250).ConfigureAwait(false);
            }

            return conflict;
        }
        finally
        {
            Mdns.AnswerReceived -= handler;
        }
    }

    /// <summary>
    ///    Sends an unsolicited MDNS response describing the
    ///    service profile.
    /// </summary>
    /// <param name="profile">
    ///   The profile to describe.
    /// </param>
    /// <param name="numberOfTimes">
    ///     How many times to announce this service profile. Range [2 - 8]
    /// </param>
    /// <remarks>
    ///   Sends a MDNS response <see cref="Message"/> containing the pointer
    ///   and resource records of the <paramref name="profile"/>.
    ///   <para>
    ///   To provide increased robustness against packet loss,
    ///   two unsolicited responses are sent one second apart.
    ///   </para>
    /// </remarks>
    public async Task AnnounceAsync(ServiceProfile profile, int numberOfTimes = 2)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);

        numberOfTimes = Math.Max(Math.Min(numberOfTimes, 8), 2);
        var message = new Message { QR = true };

        // Add the shared records.
        var ptrRecord = new PTRRecord { Name = profile.QualifiedServiceName, DomainName = profile.FullyQualifiedName };
        ptrRecord.Class = (DnsClass)((ushort)ptrRecord.Class);
        message.Answers.Add(ptrRecord);

        // Add the resource records.
        foreach (var resource in profile.Resources)
        {
            var newResource = resource.Clone() as ResourceRecord;
            if (profile.SharedProfile == false)
            {
                newResource.Class = (DnsClass)((ushort)newResource.Class | MulticastService.CACHE_FLUSH_BIT);
            }

            message.Answers.Add(newResource);
        }

        for (int i = 0; i < numberOfTimes; i++)
        {
            if (i > 0)
            {
                await Task.Delay(501 * (1 << i));
            }

            Mdns.SendAnswer(message, false);
        }
    }

    /// <summary>
    /// Sends a goodbye message for each announced service
    /// and removes its pointer from the name sever.
    /// </summary>
    public void Unadvertise()
    {
        ThrowIfDisposed();

        foreach (var profile in _advertisedRecords.Keys.ToList())
        {
            Unadvertise(profile);
        }
    }

    /// <summary>
    /// Sends a goodbye message for the provided
    /// profile and removes its pointer from the name sever.
    /// </summary>
    /// <param name="profile">The profile to send a goodbye message for.</param>
    public void Unadvertise(ServiceProfile profile)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);

        if (!_advertisedRecords.TryGetValue(profile, out var advertisedRecords))
        {
            return;
        }

        var message = CreateGoodbyeMessage(profile, advertisedRecords);
        if (message.Answers.Count > 0 || message.AdditionalRecords.Count > 0)
        {
            Mdns.SendAnswer(message);
        }

        RemoveAdvertisedRecords(profile, advertisedRecords);
    }

    private void OnAnswer(object sender, MessageEventArgs e)
    {
        var msg = e.Message;

        // Any DNS-SD answers?
        var sd = msg.Answers
            .OfType<PTRRecord>()
            .Where(ptr => ptr.Name.IsSubdomainOf(_localDomain));
        foreach (var ptr in sd)
        {
            if (ptr.Name == ServiceName)
            {
                ServiceDiscovered?.Invoke(this, ptr.DomainName);
            }
            else if (ptr.TTL == TimeSpan.Zero)
            {
                var args = new ServiceInstanceShutdownEventArgs
                {
                    ServiceInstanceName = ptr.DomainName,
                    Message = msg,
                    RemoteEndPoint = e.RemoteEndPoint
                };
                ServiceInstanceShutdown?.Invoke(this, args);
            }
            else
            {
                var args = new ServiceInstanceDiscoveryEventArgs
                {
                    ServiceInstanceName = ptr.DomainName,
                    Message = msg,
                    RemoteEndPoint = e.RemoteEndPoint
                };
                ServiceInstanceDiscovered?.Invoke(this, args);
            }
        }
    }

    private void OnQuery(object sender, MessageEventArgs e)
    {
        var request = e.Message;

        // Determine if this query is requesting a unicast response
        // and normalise the Class.
        var QU = false; // unicast query response?
        foreach (var r in request.Questions)
        {
            if (((ushort)r.Class & MulticastService.UNICAST_RESPONSE_BIT) != 0)
            {
                QU = true;
                r.Class = (DnsClass)((ushort)r.Class & ~MulticastService.UNICAST_RESPONSE_BIT);
            }
        }

        var response = request.CreateResponse();
        response.AA = true;

        foreach (var question in request.Questions)
        {
            var answers = _records.Where(r => r.Name == question.Name
                && (question.Type == DnsType.ANY || r.Type == question.Type)
                && (question.Class == DnsClass.ANY || r.Class == question.Class));
            foreach (var answer in answers)
            {
                response.Answers.Add(CloneRecord(answer));
            }
        }

        if (response.Answers.Count == 0)
        {
            return;
        }

        // Add additional records for service instances
        foreach (var answer in response.Answers.ToList())
        {
            if (answer is PTRRecord ptr)
            {
                var additionals = _records.Where(r => r.Name == ptr.DomainName);
                foreach (var additional in additionals)
                {
                    response.AdditionalRecords.Add(CloneRecord(additional));
                }
                // Add address records for SRV targets
                foreach (var srv in response.AdditionalRecords.OfType<SRVRecord>().ToList())
                {
                    var addresses = _records.Where(r => r.Name == srv.Target && (r.Type == DnsType.A || r.Type == DnsType.AAAA))
                        .Select(record => CloneRecord(record));
                    foreach (var addr in addresses)
                    {
                        if (!response.AdditionalRecords.Contains(addr))
                        {
                            response.AdditionalRecords.Add(addr);
                        }
                    }
                }
            }
        }

        RemoveDuplicates(response.Answers);
        RemoveDuplicates(response.AdditionalRecords);

        // Many bonjour browsers don't like DNS-SD response
        // with additional records.
        if (response.Answers.Any(a => a.Name == ServiceName))
        {
            response.AdditionalRecords.Clear();
        }

        if (AnswersContainsAdditionalRecords)
        {
            response.Answers.AddRange(response.AdditionalRecords);
            response.AdditionalRecords.Clear();
        }

        if (QU && MulticastService.EnableUnicastAnswers)
        {
            Mdns.SendAnswer(response, e, false, e.RemoteEndPoint); //Send a unicast response
        }
        else
        {
            Mdns.SendAnswer(response, e, !QU);
        }
    }

    #region IDisposable Support

    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        Mdns.QueryReceived -= OnQuery;
        Mdns.AnswerReceived -= OnAnswer;
        if (_instantiatedMdns)
        {
            Mdns.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ResourceRecord CloneRecord(ResourceRecord record, TimeSpan? ttl = null)
    {
        var clone = record.Clone<ResourceRecord>();
        if (ttl.HasValue)
        {
            clone.TTL = ttl.Value;
        }

        return clone;
    }

    private static Message CreateGoodbyeMessage(ServiceProfile profile, IEnumerable<ResourceRecord> advertisedRecords)
    {
        var message = new Message { QR = true };

        foreach (var ptrRecord in advertisedRecords.OfType<PTRRecord>().Where(r => r.DomainName == profile.FullyQualifiedName))
        {
            message.Answers.Add(CloneRecord(ptrRecord, TimeSpan.Zero));
        }

        foreach (var resource in profile.Resources)
        {
            message.AdditionalRecords.Add(CloneRecord(resource, TimeSpan.Zero));
        }

        return message;
    }

    private static List<ResourceRecord> CreateAdvertisedRecords(ServiceProfile service)
    {
        var advertisedRecords = new List<ResourceRecord>
        {
            new PTRRecord { Name = ServiceName, DomainName = service.QualifiedServiceName },
            new PTRRecord { Name = service.QualifiedServiceName, DomainName = service.FullyQualifiedName }
        };

        foreach (var subtype in service.Subtypes)
        {
            advertisedRecords.Add(new PTRRecord
            {
                Name = DomainName.Join(
                    new DomainName(subtype),
                    _subName,
                    service.QualifiedServiceName),
                DomainName = service.FullyQualifiedName
            });
        }

        advertisedRecords.AddRange(service.Resources);
        return advertisedRecords;
    }

    private void RemoveAdvertisedRecords(ServiceProfile service, IEnumerable<ResourceRecord> advertisedRecords)
    {
        foreach (var record in advertisedRecords)
        {
            while (_records.Remove(record))
            {
            }
        }

        _advertisedRecords.Remove(service);
    }

    private static void RemoveDuplicates(List<ResourceRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var current = records[i];
            for (var j = 0; j < i; j++)
            {
                if (records[j].Equals(current))
                {
                    records.RemoveAt(i);
                    break;
                }
            }
        }
    }

    #endregion IDisposable Support
}
