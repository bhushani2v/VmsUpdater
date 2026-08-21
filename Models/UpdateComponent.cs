namespace VmsUpdater.Models;

public class UpdateComponent
{
    public string Name { get; init; } = string.Empty;
    public string[] UninstallCommands { get; init; } = [];
    public string[] InstallCommands { get; init; } = [];
    public string[] PreCommands { get; init; } = [];
    public string[] PostCommands { get; init; } = [];

    /// <summary>
    /// Files (relative to extract dir) that must exist for this component to install.
    /// Checked before any uninstall/install begins.
    /// </summary>
    public string[] RequiredFiles { get; init; } = [];

    public static readonly UpdateComponent ManagementServer = new()
    {
        Name = "Management Server",
        RequiredFiles = ["I2v_managementserver-deb.deb"],
        PreCommands =
        [
            "echo 'Backing up VMS_Config.xml...'",
            "sudo cp /opt/I2v/Server/VMS_Config.xml /opt/I2v/Common/VmsUpdater/VMS_Config.xml"
        ],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f I2v_managementserver-deb.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i I2v_managementserver-deb.deb"],
        PostCommands =
        [
            "echo 'Restoring VMS_Config.xml...'",
            "sudo cp /opt/I2v/Common/VmsUpdater/VMS_Config.xml /opt/I2v/Server/VMS_Config.xml",
            "echo 'Restarting Management Server service...'",
            "sudo systemctl restart i2v_Management_Server.service"
        ]
    };

    public static readonly UpdateComponent RecordingServer = new()
    {
        Name = "Recording Server",
        RequiredFiles = ["I2v_recordingserver-deb.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f I2v_recordingserver-deb.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i I2v_recordingserver-deb.deb"]
    };

    public static readonly UpdateComponent MySqlPackages = new()
    {
        Name = "MySQL Packages",
        RequiredFiles = ["mysql-package_MS.deb", "mysql-package_RS.deb"],
        PreCommands =
        [
            "echo 'Stopping Management Server...'",
            "sudo systemctl stop i2v_Management_Server.service || true",
            "echo 'Stopping Recording Server...'",
            "sudo systemctl stop i2v_Recording_Server.service || true"
        ],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f mysql-package_MS.deb Package) || true",
            "sudo dpkg --purge $(dpkg-deb -f mysql-package_RS.deb Package) || true"
        ],
        InstallCommands =
        [
            "sudo dpkg -i mysql-package_MS.deb",
            "sudo dpkg -i mysql-package_RS.deb"
        ],
        PostCommands =
        [
            "echo 'Starting Management Server...'",
            "sudo systemctl start i2v_Management_Server.service || true",
            "echo 'Starting Recording Server...'",
            "sudo systemctl start i2v_Recording_Server || true"
        ]
    };

    public static readonly UpdateComponent ApacheServer = new()
    {
        Name = "Apache Server",
        RequiredFiles = ["i2v_Appache.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f i2v_Appache.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i i2v_Appache.deb"]
    };

    public static readonly UpdateComponent VmsClient = new()
    {
        Name = "VMS Client",
        RequiredFiles = ["VMS_CLIENT.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f VMS_CLIENT.deb Package) || true"
        ],
        InstallCommands =
        [
            "sudo dpkg -i VMS_CLIENT.deb",
            "sudo chmod -R 775 /opt/I2v/VMS_CLIENT"
        ]
    };

    public static readonly UpdateComponent ConfigurationManager = new()
    {
        Name = "Configuration Manager",
        RequiredFiles = ["Configuration_Manager-deb.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f Configuration_Manager-deb.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i Configuration_Manager-deb.deb"]
    };

    public static readonly UpdateComponent MosquittoBroker = new()
    {
        Name = "Mosquitto Broker",
        RequiredFiles = ["i2v_Mosquitto_Broker.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f i2v_Mosquitto_Broker.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i i2v_Mosquitto_Broker.deb"]
    };

    public static readonly UpdateComponent NetFailover = new()
    {
        Name = "Net Failover",
        RequiredFiles = ["i2v_NetFailover.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f i2v_NetFailover.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i i2v_NetFailover.deb"]
    };

    public static readonly UpdateComponent NetworkStatusChecker = new()
    {
        Name = "Network Status Checker",
        RequiredFiles = ["I2v_NetworkStatusChecker.deb"],
        UninstallCommands =
        [
            "sudo dpkg --purge $(dpkg-deb -f I2v_NetworkStatusChecker.deb Package) || true"
        ],
        InstallCommands = ["sudo dpkg -i I2v_NetworkStatusChecker.deb"]
    };

    public static readonly UpdateComponent SambaAndCifs = new()
    {
        Name = "Samba & CIFS",
        UninstallCommands =
        [
            "sudo dpkg --purge samba python3-samba python3-ldb samba-common samba-common-bin smbclient || true",
            "sudo dpkg --purge cifs-utils || true"
        ],
        InstallCommands =
        [
            "sudo dpkg --force-all -i ./Samba/samba-packages/*.deb",
            "sudo dpkg --force-all -i ./Samba/cifs-utils/*.deb"
        ]
    };

    public static readonly UpdateComponent[] All =
    [
        ManagementServer,
        RecordingServer,
        MySqlPackages,
        ApacheServer,
        VmsClient,
        ConfigurationManager,
        MosquittoBroker,
        NetFailover,
        NetworkStatusChecker,
        SambaAndCifs
    ];

    /// <summary>
    /// Maps CLI component keys to UpdateComponent instances.
    /// Used by --components flag (e.g. --components ms,rs,mysql)
    /// </summary>
    public static readonly Dictionary<string, UpdateComponent> ComponentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms"] = ManagementServer,
        ["rs"] = RecordingServer,
        ["mysql"] = MySqlPackages,
        ["apache"] = ApacheServer,
        ["client"] = VmsClient,
        ["configmgr"] = ConfigurationManager,
        ["mosquitto"] = MosquittoBroker,
        ["failover"] = NetFailover,
        ["netstatus"] = NetworkStatusChecker,
        ["samba"] = SambaAndCifs
    };
}
