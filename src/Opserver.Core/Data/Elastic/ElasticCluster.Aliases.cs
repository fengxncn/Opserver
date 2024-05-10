﻿using System.Runtime.Serialization;

namespace Opserver.Data.Elastic;

public partial class ElasticCluster
{
    private Cache<IndexAliasInfo> _aliases;
    public Cache<IndexAliasInfo> Aliases =>
        _aliases ??= GetElasticCache(async () =>
        {
            var aliases = await GetAsync<Dictionary<string, IndexAliasList>>("_aliases");
            return new IndexAliasInfo
            {
                Aliases = aliases?.Where(a => a.Value?.Aliases != null && a.Value.Aliases.Count > 0)
                    .ToDictionary(a => a.Key, a => a.Value.Aliases.Keys.ToList())
                          ?? []
            };
        });

    public string GetIndexAliasedName(string index)
    {
        if (Aliases.Data?.Aliases == null)
            return index;

        return Aliases.Data.Aliases.TryGetValue(index, out var aliases)
                   ? aliases[0].IsNullOrEmptyReturn(index)
                   : index;
    }

    public class IndexAliasInfo
    {
        public Dictionary<string, List<string>> Aliases { get; internal set; }
    }

    public class IndexAliasList
    {
        [DataMember(Name = "aliases")]
        public Dictionary<string, object> Aliases { get; internal set; }
    }
}
