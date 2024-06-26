﻿using System.ComponentModel;

namespace Opserver.Data.HAProxy;

/// <summary>
/// Current server statuses
/// </summary>
public enum ProxyServerStatus
{
    [Description("Status Unknown!")]
    None = 0, //Won't be populated for backends

    [Description("Server is up, status normal.")]
    ActiveUp = 2,
    [Description("Server has not responded to checks in a timely manner, going down.")]
    ActiveUpGoingDown = 8,
    [Description("Server is responsive and recovering.")]
    ActiveDownGoingUp = 6,

    [Description("Backup server is up, status normal.")]
    BackupUp = 3,
    [Description("Backup server has not responded to checks in a timely manner, going down.")]
    BackupUpGoingDown = 9,
    [Description("Backup server is responsive and recovering.")]
    BackupDownGoingUp = 7,

    [Description("Server is not checked.")]
    NotChecked = 4,
    [Description("Server is down and receiving no requests.")]
    Down = 10,
    [Description("Server is in maintenance and receiving no requests.")]
    Maintenance = 5,
    [Description("Front end is open to receiving requests.")]
    Open = 1,
    [Description("Server is draining and receiving no requests.")]
    Drain = 11,
}

public static class ProxyServerStatusExtensions
{
    public static string ShortDescription(this ProxyServerStatus status) =>
        status switch
        {
            ProxyServerStatus.ActiveUp => "Active",
            ProxyServerStatus.ActiveUpGoingDown => "Active (Up -> Down)",
            ProxyServerStatus.ActiveDownGoingUp => "Active (Down -> Up)",
            ProxyServerStatus.BackupUp => "Backup",
            ProxyServerStatus.BackupUpGoingDown => "Backup (Up -> Down)",
            ProxyServerStatus.BackupDownGoingUp => "Backup (Down -> Up)",
            ProxyServerStatus.NotChecked => "Not Checked",
            ProxyServerStatus.Down => "Down",
            ProxyServerStatus.Maintenance => "Maintenance",
            ProxyServerStatus.Open => "Open",
            //case ProxyServerStatus.None:
            _ => "Unknown",
        };

    public static bool IsBad(this ProxyServerStatus status)
        => status switch
        {
            ProxyServerStatus.ActiveUpGoingDown or
            ProxyServerStatus.BackupUpGoingDown or
            ProxyServerStatus.Drain or
            ProxyServerStatus.Down => true,
            _ => false,
        };
}
