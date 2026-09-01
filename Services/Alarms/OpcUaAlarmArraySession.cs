using Opc.Ua;
using Opc.Ua.Client;
using System.IO;

namespace Small_square_cavity_coating_machine.Services.Alarms;

/// <summary>Test seam exposing only browse, read and subscribe; no PLC write surface.</summary>
public interface IAlarmArraySession : IAsyncDisposable
{
    Task FindArrayAsync(string browseName, CancellationToken token);
    Task<DataValue> ReadAsync(CancellationToken token);
    Task SubscribeAsync(Action<DataValue> onValue, CancellationToken token);
    Task WaitForDisconnectAsync(CancellationToken token);
}

public interface IAlarmArraySessionFactory
{
    Task<IAlarmArraySession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token);
}

public sealed class OpcUaAlarmArraySessionFactory(string pkiDirectory) : IAlarmArraySessionFactory
{
    public async Task<IAlarmArraySession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token)
    {
        options.Validate();
        var telemetry = DefaultTelemetry.Create(_ => { });
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "SmallSquareCavity.AlarmReader",
            ApplicationUri = $"urn:{System.Net.Dns.GetHostName()}:SmallSquareCavity:AlarmReader",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory",
                    StorePath = Path.Combine(pkiDirectory, "own"), SubjectName = "CN=SmallSquareCavity.AlarmReader" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory",
                    StorePath = Path.Combine(pkiDirectory, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory",
                    StorePath = Path.Combine(pkiDirectory, "issuers") },
                RejectedCertificateStore = new CertificateStoreIdentifier { StoreType = "Directory",
                    StorePath = Path.Combine(pkiDirectory, "rejected") },
                AutoAcceptUntrustedCertificates = false
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 10000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
        };
        await configuration.ValidateAsync(ApplicationType.Client, token).ConfigureAwait(false);
        using var discovery = await DiscoveryClient.CreateAsync(new Uri(options.EndpointUrl),
            EndpointConfiguration.Create(configuration), telemetry, DiagnosticsMasks.None, token).ConfigureAwait(false);
        var endpoints = await discovery.GetEndpointsAsync(null, token).ConfigureAwait(false);
        var endpoint = endpoints.FirstOrDefault(e => e.SecurityMode == MessageSecurityMode.None
            && e.SecurityPolicyUri == SecurityPolicies.None
            && e.UserIdentityTokens.Any(t => t.TokenType == UserTokenType.Anonymous))
            ?? throw new InvalidOperationException("PLC未提供匿名、SecurityMode=None端点；本次不会自动更改安全策略");
        // Discovery can advertise a PLC hostname inaccessible to this computer.
        // Keep its path and port contract, but connect to the exact endpoint explicitly entered by the user.
        endpoint.EndpointUrl = options.EndpointUrl;
        var session = await new DefaultSessionFactory(telemetry).CreateAsync(configuration,
            new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)),
            false, true, configuration.ApplicationName, 60000, new UserIdentity(), null, token).ConfigureAwait(false);
        return new OpcUaAlarmArraySession(session, options);
    }
}

public sealed partial class OpcUaAlarmArraySession : IAlarmArraySession, IAlarmNodeBrowser
{
    private readonly ISession _session;
    private readonly OpcUaAlarmOptions _options;
    private readonly TaskCompletionSource<string> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NodeId? _array;
    private Subscription? _subscription;
    private MonitoredItem? _item;
    private MonitoredItemNotificationEventHandler? _handler;

    public OpcUaAlarmArraySession(ISession session, OpcUaAlarmOptions options)
    {
        _session = session;
        _options = options;
        session.KeepAliveInterval = 1000;
        session.KeepAlive += OnKeepAlive;
    }

    public async Task FindArrayAsync(string browseName, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try { _array = await AlarmArrayDiscovery.FindAsync(this, browseName, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("报警数组节点浏览超过60秒，无法确认唯一节点"); }
    }

    public async Task<IReadOnlyList<AlarmBrowseNode>> BrowseAsync(NodeId parent, CancellationToken token)
    {
        var result = new List<AlarmBrowseNode>();
        byte[]? continuation = null;
        try
        {
            var response = await _session.BrowseAsync(null, null, 500,
                new BrowseDescriptionCollection { new BrowseDescription
                {
                    NodeId = parent, BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences, IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                    ResultMask = (uint)BrowseResultMask.All
                } }, token).ConfigureAwait(false);
            var page = response.Results.Single();
            while (true)
            {
                continuation = page.ContinuationPoint;
                if (StatusCode.IsBad(page.StatusCode)) throw new ServiceResultException(page.StatusCode);
                foreach (var reference in page.References)
                {
                    if (reference.NodeId.ServerIndex != 0) continue;
                    var id = ExpandedNodeId.ToNodeId(reference.NodeId, _session.NamespaceUris);
                    if (id is not null && !NodeId.IsNull(id))
                        result.Add(new AlarmBrowseNode(id, reference.BrowseName.Name, reference.NodeClass));
                }
                if (continuation is not { Length: > 0 }) break;
                var next = await _session.BrowseNextAsync(null, false, new ByteStringCollection { continuation }, token)
                    .ConfigureAwait(false);
                page = next.Results.Single();
            }
        }
        finally
        {
            if (continuation is { Length: > 0 })
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _session.BrowseNextAsync(null, true, new ByteStringCollection { continuation }, cleanup.Token).ConfigureAwait(false); }
                catch { /* Session close will also release continuation points. */ }
            }
        }
        return result;
    }

    public async Task<bool> IsReadableArrayAsync(NodeId node, CancellationToken token)
    {
        var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Neither,
            new ReadValueIdCollection {
                new ReadValueId { NodeId = node, AttributeId = Attributes.ValueRank },
                new ReadValueId { NodeId = node, AttributeId = Attributes.UserAccessLevel }
            }, token).ConfigureAwait(false);
        return response.Results.Count == 2 && response.Results.All(v => StatusCode.IsGood(v.StatusCode))
            && response.Results[0].Value is int rank && rank is 0 or 1
            && response.Results[1].Value is byte access && (access & AccessLevels.CurrentRead) != 0;
    }

    public async Task<DataValue> ReadAsync(CancellationToken token)
    {
        var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both,
            new ReadValueIdCollection { new ReadValueId { NodeId = _array!, AttributeId = Attributes.Value } }, token)
            .ConfigureAwait(false);
        return response.Results.Single();
    }

    public async Task SubscribeAsync(Action<DataValue> onValue, CancellationToken token)
    {
        _subscription = new Subscription(_session.DefaultSubscription)
        {
            DisplayName = "EventDef.Array.ReadOnly", PublishingEnabled = true,
            PublishingInterval = _options.PublishingIntervalMs, KeepAliveCount = 10,
            LifetimeCount = 30, SequentialPublishing = true
        };
        _item = new MonitoredItem(_subscription.DefaultItem)
        {
            DisplayName = _options.ArrayBrowseName, StartNodeId = _array!, AttributeId = Attributes.Value,
            SamplingInterval = _options.SamplingIntervalMs, QueueSize = 100,
            DiscardOldest = true, MonitoringMode = MonitoringMode.Reporting
        };
        _handler = (item, _) =>
        {
            foreach (var value in item.DequeueValues())
            {
                onValue(value);
                if (value.StatusCode.Overflow)
                    _failure.TrySetResult("报警数组订阅队列溢出，正在重新核对状态");
            }
        };
        _item.Notification += _handler;
        _subscription.AddItem(_item);
        _session.AddSubscription(_subscription);
        await _subscription.CreateAsync(token).ConfigureAwait(false);
        if (!_item.Status.Created || ServiceResult.IsBad(_item.Status.Error))
            throw new InvalidOperationException($"报警数组订阅失败：{_item.Status.Error}");
    }

    public async Task WaitForDisconnectAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (_failure.Task.IsCompleted) throw new InvalidOperationException(await _failure.Task.ConfigureAwait(false));
            if (!_session.Connected || _subscription?.PublishingStopped == true)
                throw new InvalidOperationException("PLC报警订阅已中断");
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    private void OnKeepAlive(ISession session, KeepAliveEventArgs args)
    {
        if (ServiceResult.IsBad(args.Status)) _failure.TrySetResult($"PLC通信中断：{args.Status}");
    }

    public async ValueTask DisposeAsync()
    {
        _session.KeepAlive -= OnKeepAlive;
        DetachEquipmentHandlers();
        if (_item is not null && _handler is not null) _item.Notification -= _handler;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _session.CloseAsync(true, timeout.Token).ConfigureAwait(false); }
        catch { /* Connection failure is reported by the controller; cleanup is bounded. */ }
        _subscription?.Dispose();
        _session.Dispose();
    }
}
