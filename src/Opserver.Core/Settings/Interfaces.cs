﻿namespace Opserver;

public interface ISecurableModule
{
    bool Enabled { get; }
    // TODO: List<string>
    string ViewGroups { get; }
    string AdminGroups { get; }

    string ViewRole { get; }
    string AdminRole { get; }
}

public interface ISettingsCollectionItem
{
    string Name { get; }
}
