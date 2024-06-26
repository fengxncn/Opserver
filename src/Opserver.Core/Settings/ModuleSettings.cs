﻿namespace Opserver;

public abstract class ModuleSettings : ISecurableModule
{
    /// <summary>
    /// Whether this section is enabled (has servers, has connection, etc.)
    /// </summary>
    public abstract bool Enabled { get; }

    /// <summary>
    /// Semilcolon delimited list of security groups that can see this section, but not perform actions.
    /// </summary>
    public string ViewGroups { get; set; }

    /// <summary>
    /// Semilcolon delimited list of security groups that can do anything in this section, including management actions.
    /// </summary>
    public string AdminGroups { get; set; }

    /// <summary>
    /// The role name to use for viewers of this module.
    /// </summary>
    public abstract string ViewRole { get; }

    /// <summary>
    /// The role name to use for admins of this module.
    /// </summary>
    public abstract string AdminRole { get; }
}
