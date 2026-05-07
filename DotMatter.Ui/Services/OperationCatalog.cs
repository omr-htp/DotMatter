using DotMatter.Ui.Models;

namespace DotMatter.Ui.Services;

internal static class OperationCatalog
{
    private const string EmptyArrayJson = "[]";

    public static IReadOnlyList<ApiOperationDefinition> GlobalOperations
    {
        get;
    } =
    [
        new("runtime", "Runtime snapshot", "Read the safe authenticated runtime snapshot for the controller process.", ApiOperationMethod.Get, "/api/system/runtime"),
        new("diagnostics", "Detailed diagnostics", "Read the detailed diagnostics payload when the endpoint is enabled.", ApiOperationMethod.Get, "/api/system/diagnostics"),
        new("fabric-acls", "Fabric ACL query", "Query AccessControl ACL entries across all known devices on the controller fabric.", ApiOperationMethod.Get, "/api/acls"),
        new("fabric-bindings", "Fabric binding query", "Query Binding entries across all known devices on the controller fabric.", ApiOperationMethod.Get, "/api/bindings",
            [
                new("endpoint", "Endpoint (optional)", ApiFieldLocation.Query, DefaultValue: "1", Placeholder: "1")
            ])
    ];

    public static IReadOnlyList<ApiOperationDefinition> CommissioningOperations
    {
        get;
    } =
    [
        new("thread-commission", "BLE commissioning", "Commission a BLE device onto the fabric. Leave network provisioning enabled for Thread devices, or skip it for devices that are already on the IP network.", ApiOperationMethod.Post, "/api/commission",
            [
                new("Discriminator", "Discriminator", ApiFieldLocation.Body, ApiFieldInputKind.Integer, true, "3840"),
                new("Passcode", "Passcode", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "20202021"),
                new("FabricName", "Fabric name", ApiFieldLocation.Body, DefaultValue: ""),
                new("ManualCode", "Manual pairing code", ApiFieldLocation.Body, DefaultValue: ""),
                new("QrCode", "QR code payload", ApiFieldLocation.Body, DefaultValue: ""),
                new("SkipNetworkProvisioning", "Skip network provisioning", ApiFieldLocation.Body, ApiFieldInputKind.Boolean, DefaultBool: false,
                    HelpText: "Enable this for devices that are already on the IP network and reject Thread/Wi-Fi Network Commissioning.")
            ],
            "Commission",
            LongRunning: true),
        new("wifi-commission", "Wi-Fi commissioning", "Commission a new Wi-Fi device over BLE and provision Wi-Fi credentials in the same flow.", ApiOperationMethod.Post, "/api/commission/wifi",
            [
                new("Discriminator", "Discriminator", ApiFieldLocation.Body, ApiFieldInputKind.Integer, true, "3840"),
                new("Passcode", "Passcode", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "20202021"),
                new("FabricName", "Fabric name", ApiFieldLocation.Body, DefaultValue: ""),
                new("WifiSsid", "Wi-Fi SSID", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("WifiPassword", "Wi-Fi password", ApiFieldLocation.Body),
                new("ManualCode", "Manual pairing code", ApiFieldLocation.Body, DefaultValue: ""),
                new("QrCode", "QR code payload", ApiFieldLocation.Body, DefaultValue: "")
            ],
            "Commission",
            LongRunning: true)
    ];

    public static IReadOnlyList<ApiOperationDefinition> DeviceReadOperations
    {
        get;
    } =
    [
        new("device-detail", "Device detail", "Read the selected device detail payload.", ApiOperationMethod.Get, "/api/devices/{deviceId}"),
        new("device-state", "Current state", "Read the selected device state snapshot.", ApiOperationMethod.Get, "/api/devices/{deviceId}/state"),
        new("device-commissioning", "Commissioning state", "Read administrator/general commissioning state from endpoint 0.", ApiOperationMethod.Get, "/api/devices/{deviceId}/commissioning"),
        new("device-fabrics", "Fabric inventory", "Read the operational credentials inventory for the selected device.", ApiOperationMethod.Get, "/api/devices/{deviceId}/fabrics"),
        new("device-network", "Network commissioning state", "Read the promoted Network Commissioning state from endpoint 0.", ApiOperationMethod.Get, "/api/devices/{deviceId}/network-commissioning",
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("device-groups", "Groups state", "Read the promoted Groups state for one application endpoint.", ApiOperationMethod.Get, "/api/devices/{deviceId}/groups",
            [
                new("endpoint", "Endpoint", ApiFieldLocation.Query, ApiFieldInputKind.Text, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("device-group-keys", "Group key state", "Read Group Key Management state from endpoint 0.", ApiOperationMethod.Get, "/api/devices/{deviceId}/group-keys",
            RequiredCapability: DeviceOperationCapability.GroupKeys),
        new("device-scenes", "Scenes state", "Read the promoted Scenes Management state for one application endpoint.", ApiOperationMethod.Get, "/api/devices/{deviceId}/scenes",
            [
                new("endpoint", "Endpoint", ApiFieldLocation.Query, ApiFieldInputKind.Text, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("device-acl", "Device ACL query", "Read AccessControl entries from endpoint 0.", ApiOperationMethod.Get, "/api/devices/{deviceId}/acl",
            RequiredCapability: DeviceOperationCapability.AccessControl),
        new("device-bindings", "Device binding query", "Read Binding entries from the selected source endpoint. Endpoint defaults to 1.", ApiOperationMethod.Get, "/api/devices/{deviceId}/bindings",
            [
                new("endpoint", "Endpoint", ApiFieldLocation.Query, DefaultValue: "1")
            ],
            RequiredCapability: DeviceOperationCapability.Binding)
    ];

    public static IReadOnlyList<ApiOperationDefinition> ControlOperations
    {
        get;
    } =
    [
        new("turn-on", "Turn on", "Send the On command to the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/on",
            RequiredCapability: DeviceOperationCapability.OnOff),
        new("turn-off", "Turn off", "Send the Off command to the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/off",
            RequiredCapability: DeviceOperationCapability.OnOff),
        new("toggle", "Toggle", "Send the Toggle command to the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/toggle",
            RequiredCapability: DeviceOperationCapability.OnOff),
        new("set-level", "Set level", "Write a target brightness level and transition time.", ApiOperationMethod.Post, "/api/devices/{deviceId}/level",
            [
                new("Level", "Level", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "128"),
                new("TransitionTime", "Transition time", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "5")
            ],
            RequiredCapability: DeviceOperationCapability.Level),
        new("set-color", "Set hue/saturation", "Set the current hue, saturation, and transition time.", ApiOperationMethod.Post, "/api/devices/{deviceId}/color",
            [
                new("Hue", "Hue", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "120"),
                new("Saturation", "Saturation", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "180"),
                new("TransitionTime", "Transition time", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "5")
            ],
            RequiredCapability: DeviceOperationCapability.ColorHueSaturation),
        new("set-color-xy", "Set CIE xy color", "Set the device color using CIE XY coordinates.", ApiOperationMethod.Post, "/api/devices/{deviceId}/color-xy",
            [
                new("X", "X", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "25000"),
                new("Y", "Y", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "25000"),
                new("TransitionTime", "Transition time", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "5")
            ],
            RequiredCapability: DeviceOperationCapability.ColorXy)
    ];

    public static IReadOnlyList<ApiOperationDefinition> AdminOperations
    {
        get;
    } =
    [
        new("basic-window", "Open basic window", "Open a basic commissioning window on endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/commissioning/window/basic",
            [
                new("CommissioningTimeout", "Commissioning timeout", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "180")
            ]),
        new("enhanced-window", "Open enhanced window", "Open an enhanced commissioning window using explicit PAKE parameters.", ApiOperationMethod.Post, "/api/devices/{deviceId}/commissioning/window/enhanced",
            [
                new("CommissioningTimeout", "Commissioning timeout", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "180"),
                new("PakePasscodeVerifierHex", "PAKE verifier hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("Discriminator", "Discriminator", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "3840"),
                new("Iterations", "Iterations", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "1000"),
                new("SaltHex", "Salt hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true)
            ]),
        new("revoke-window", "Revoke window", "Revoke any open commissioning window on the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/commissioning/revoke"),
        new("complete-commissioning", "Complete commissioning", "Send CommissioningComplete to the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/commissioning/complete"),
        new("fabric-label", "Update fabric label", "Update the current fabric label for the selected device.", ApiOperationMethod.Post, "/api/devices/{deviceId}/fabrics/label",
            [
                new("Label", "Label", ApiFieldLocation.Body, ApiFieldInputKind.Text, true)
            ]),
        new("remove-fabric", "Remove fabric", "Remove a fabric from the selected device by fabric index.", ApiOperationMethod.Delete, "/api/devices/{deviceId}/fabrics/{fabricIndex}",
            [
                new("fabricIndex", "Fabric index", ApiFieldLocation.Path, ApiFieldInputKind.Text, true)
            ]),
        new("delete-device", "Delete device", "Remove the controller fabric from the node and clean up local state, or use localOnly=true for local cleanup only.", ApiOperationMethod.Delete, "/api/devices/{deviceId}",
            [
                new("localOnly", "Local only", ApiFieldLocation.Query, ApiFieldInputKind.Boolean, DefaultBool: false)
            ])
    ];

    public static IReadOnlyList<ApiOperationDefinition> NetworkOperations
    {
        get;
    } =
    [
        new("scan-networks", "Scan networks", "Run ScanNetworks on endpoint 0. Ssid is optional and only applies to Wi-Fi scans.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/scan",
            [
                new("Ssid", "SSID", ApiFieldLocation.Body, DefaultValue: ""),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("add-wifi", "Add or update Wi-Fi", "Add or update one Wi-Fi network on endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/wifi",
            [
                new("Ssid", "SSID", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("Credentials", "Credentials", ApiFieldLocation.Body, DefaultValue: ""),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: ""),
                new("NetworkIdentityHex", "Network identity hex", ApiFieldLocation.Body, DefaultValue: ""),
                new("ClientIdentifierHex", "Client identifier hex", ApiFieldLocation.Body, DefaultValue: ""),
                new("PossessionNonceHex", "Possession nonce hex", ApiFieldLocation.Body, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("add-thread", "Add or update Thread", "Add or update one Thread operational dataset on endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/thread",
            [
                new("OperationalDatasetHex", "Operational dataset hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("connect-network", "Connect network", "Connect to one configured network using a hexadecimal network identifier.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/connect",
            [
                new("NetworkIdHex", "Network ID hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("remove-network", "Remove network", "Remove one configured network using its hexadecimal network identifier.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/remove",
            [
                new("NetworkIdHex", "Network ID hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("reorder-network", "Reorder network", "Move a configured network to a new index.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/reorder",
            [
                new("NetworkIdHex", "Network ID hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("NetworkIndex", "Network index", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "0"),
                new("Breadcrumb", "Breadcrumb", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning),
        new("interface-enabled", "Set interface enabled", "Write the Network Commissioning InterfaceEnabled attribute.", ApiOperationMethod.Post, "/api/devices/{deviceId}/network-commissioning/interface-enabled",
            [
                new("InterfaceEnabled", "Interface enabled", ApiFieldLocation.Body, ApiFieldInputKind.Boolean, DefaultBool: true)
            ],
            RequiredCapability: DeviceOperationCapability.NetworkCommissioning)
    ];

    public static IReadOnlyList<ApiOperationDefinition> GroupSceneOperations
    {
        get;
    } =
    [
        new("groups-add", "Add group", "Add one group to the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/add",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupName", "Group name", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "Lab Group")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("groups-view", "View group", "Read one group from the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/view",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("groups-membership", "Group membership", "Read group membership from the selected endpoint. Leave GroupIds empty to query all.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/membership",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupIds", "Group IDs JSON", ApiFieldLocation.Body, ApiFieldInputKind.Json, DefaultValue: EmptyArrayJson, Placeholder: "[1,2]")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("groups-remove", "Remove group", "Remove one group from the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/remove",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("groups-remove-all", "Remove all groups", "Remove all groups from the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/remove-all",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("groups-add-if-identifying", "Add group if identifying", "Add a group only if the endpoint is currently identifying.", ApiOperationMethod.Post, "/api/devices/{deviceId}/groups/add-if-identifying",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupName", "Group name", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "Identify Group")
            ],
            RequiredCapability: DeviceOperationCapability.Groups),
        new("group-keys-write", "Write group key set", "Write one group key set to endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/group-keys/write",
            [
                new("GroupKeySetId", "Group key set ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupKeySecurityPolicy", "Security policy", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "TrustFirst"),
                new("EpochKey0Hex", "Epoch key 0 hex", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "000102030405060708090A0B0C0D0E0F"),
                new("EpochStartTime0", "Epoch start time 0", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "0"),
                new("EpochKey1Hex", "Epoch key 1 hex", ApiFieldLocation.Body, DefaultValue: ""),
                new("EpochStartTime1", "Epoch start time 1", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: ""),
                new("EpochKey2Hex", "Epoch key 2 hex", ApiFieldLocation.Body, DefaultValue: ""),
                new("EpochStartTime2", "Epoch start time 2", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.GroupKeys),
        new("group-keys-read", "Read group key set", "Read one group key set from endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/group-keys/read",
            [
                new("GroupKeySetId", "Group key set ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.GroupKeys),
        new("group-keys-remove", "Remove group key set", "Remove one group key set from endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/group-keys/remove",
            [
                new("GroupKeySetId", "Group key set ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.GroupKeys),
        new("group-keys-indices", "Read all group key indices", "Read all group key set identifiers from endpoint 0.", ApiOperationMethod.Post, "/api/devices/{deviceId}/group-keys/read-all-indices",
            RequiredCapability: DeviceOperationCapability.GroupKeys),
        new("scenes-add", "Add scene", "Add one scene to the selected endpoint using typed extension-field JSON.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/add",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneId", "Scene ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("TransitionTime", "Transition time", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "0"),
                new("SceneName", "Scene name", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "Scene Alpha"),
                new("ExtensionFieldSets", "Extension field sets JSON", ApiFieldLocation.Body, ApiFieldInputKind.Json, true, EmptyArrayJson)
            ],
            LongRunning: true,
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-view", "View scene", "Read one scene on the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/view",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneId", "Scene ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-remove", "Remove scene", "Remove one scene from the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/remove",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneId", "Scene ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-remove-all", "Remove all scenes", "Remove all scenes for one group from the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/remove-all",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-store", "Store scene", "Store one scene on the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/store",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneId", "Scene ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-recall", "Recall scene", "Recall one scene on the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/recall",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneId", "Scene ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("TransitionTime", "Transition time", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-membership", "Scene membership", "Read scene membership for one group on the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/membership",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupId", "Group ID", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes),
        new("scenes-copy", "Copy scene", "Copy one scene or all scenes between groups on the selected endpoint.", ApiOperationMethod.Post, "/api/devices/{deviceId}/scenes/copy",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("CopyAllScenes", "Copy all scenes", ApiFieldLocation.Body, ApiFieldInputKind.Boolean, DefaultBool: false),
                new("GroupIdentifierFrom", "Source group", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("SceneIdentifierFrom", "Source scene", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("GroupIdentifierTo", "Target group", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "2"),
                new("SceneIdentifierTo", "Target scene", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1")
            ],
            RequiredCapability: DeviceOperationCapability.Scenes)
    ];

    public static IReadOnlyList<ApiOperationDefinition> BindingAndEventOperations
    {
        get;
    } =
    [
        new("matter-events", "Read Matter events", "Read one-shot Matter event envelopes from the selected device.", ApiOperationMethod.Get, "/api/devices/{deviceId}/matter/events",
            [
                new("cluster", "Cluster", ApiFieldLocation.Query, ApiFieldInputKind.Text, true, "0x0006"),
                new("eventId", "Event ID", ApiFieldLocation.Query, ApiFieldInputKind.Text, DefaultValue: ""),
                new("endpoint", "Endpoint", ApiFieldLocation.Query, ApiFieldInputKind.Text, DefaultValue: ""),
                new("fabricFiltered", "Fabric filtered", ApiFieldLocation.Query, ApiFieldInputKind.Boolean, DefaultBool: false)
            ],
            RequiredCapability: DeviceOperationCapability.MatterEvents),
        new("binding-remove", "Remove binding entries", "Remove binding entries that match the supplied criteria.", ApiOperationMethod.Post, "/api/devices/{deviceId}/bindings/remove",
            [
                new("Endpoint", "Endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, true, "1"),
                new("NodeId", "Target node ID", ApiFieldLocation.Body, DefaultValue: ""),
                new("Group", "Target group", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: ""),
                new("TargetEndpoint", "Target endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: ""),
                new("Cluster", "Cluster", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.Binding),
        new("acl-remove", "Remove ACL entries", "Remove ACL entries that match the supplied privilege/auth mode and subjects/targets.", ApiOperationMethod.Post, "/api/devices/{deviceId}/acl/remove",
            [
                new("Privilege", "Privilege", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "Operate"),
                new("AuthMode", "Auth mode", ApiFieldLocation.Body, ApiFieldInputKind.Text, true, "CASE"),
                new("Subjects", "Subjects JSON", ApiFieldLocation.Body, ApiFieldInputKind.Json, DefaultValue: EmptyArrayJson),
                new("Targets", "Targets JSON", ApiFieldLocation.Body, ApiFieldInputKind.Json, DefaultValue: EmptyArrayJson),
                new("AuxiliaryType", "Auxiliary type", ApiFieldLocation.Body, DefaultValue: "")
            ],
            RequiredCapability: DeviceOperationCapability.AccessControl),
        new("bind-onoff", "Bind switch -> OnOff", "Write a switch binding and matching target ACL grant for OnOff control.", ApiOperationMethod.Post, "/api/devices/{deviceId}/bindings/onoff",
            [
                new("TargetDeviceId", "Target device ID", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("SourceEndpoint", "Source endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "1"),
                new("TargetEndpoint", "Target endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "1")
            ],
            RequiredCapability: DeviceOperationCapability.SwitchBinding),
        new("unbind-onoff", "Remove switch binding", "Remove a switch OnOff route and reconcile the matching ACL entry.", ApiOperationMethod.Post, "/api/devices/{deviceId}/bindings/onoff/remove",
            [
                new("TargetDeviceId", "Target device ID", ApiFieldLocation.Body, ApiFieldInputKind.Text, true),
                new("SourceEndpoint", "Source endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "1"),
                new("TargetEndpoint", "Target endpoint", ApiFieldLocation.Body, ApiFieldInputKind.UnsignedInteger, false, "1")
            ],
            RequiredCapability: DeviceOperationCapability.SwitchBinding)
    ];
}
