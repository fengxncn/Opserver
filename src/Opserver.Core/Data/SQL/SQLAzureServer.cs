using System.Collections.Concurrent;
using System.Data.SqlClient;

namespace Opserver.Data.SQL;

public class SQLAzureServer(SQLModule module, SQLSettings.Instance settings) : SQLInstance(module, settings)
{
    private readonly ConcurrentDictionary<string, SQLInstance> _instancesByKey = [];

    private Cache<List<SQLInstance>> _instanceCache;
    public Cache<List<SQLInstance>> Instances =>
        _instanceCache ??= GetSqlCache(
            nameof(Instances), async conn =>
            {
                var instances = new List<SQLInstance>();
                // grab the list of databases in the SQL Azure instance
                // and generate a SQLInstance for each one
                var databases = await conn.QueryAsync<string>("Select name From sys.databases");
                foreach (var database in databases)
                {
                    // is there an existing instance?
                    var key = Settings.Name + ":" + database;
                    var instance = _instancesByKey.GetOrAdd(
                        key,
                        key => new SQLInstance(
                            Module,
                            new SQLSettings.Instance
                            {
                                Name = key,
                                ConnectionString = new SqlConnectionStringBuilder(ConnectionString)
                                {
                                    InitialCatalog = database
                                }.ConnectionString,
                                RefreshIntervalSeconds = Settings.RefreshIntervalSeconds,
                            }
                        )
                    );

                    instances.Add(instance);
                    // make sure we're monitoring this instance
                    instance.TryAddToGlobalPollers();
                }
                return instances;
            });

    public override IEnumerable<Cache> DataPollers
    {
        get
        {
            yield return Instances;
        }
    }
}
