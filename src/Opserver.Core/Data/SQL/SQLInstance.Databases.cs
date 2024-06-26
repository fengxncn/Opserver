﻿using System.Text.RegularExpressions;
using Dapper;
using EnumsNET;

namespace Opserver.Data.SQL;

public partial class SQLInstance
{
    private Cache<List<Database>> _databases;

    public Cache<List<Database>> Databases =>
        _databases ??= GetSqlCache(nameof(Databases),
            async conn =>
            {
                var sql = QueryLookup.GetOrAdd(
                    Tuple.Create(nameof(Databases), Engine),
                    k =>
                        GetFetchSQL<Database>(k.Item2) + "\n" +
                        GetFetchSQL<DatabaseLastBackup>(k.Item2) + "\n" +
                        GetFetchSQL<DatabaseFile>(k.Item2) + "\n" +
                        GetFetchSQL<DatabaseVLF>(k.Item2)
                );

                List<Database> dbs;
                using (var multi = await conn.QueryMultipleAsync(sql))
                {
                    dbs = await multi.ReadAsync<Database>().AsList();
                    var backups = await multi.ReadAsync<DatabaseLastBackup>().AsList();
                    var files = await multi.ReadAsync<DatabaseFile>().AsList();
                    var vlfs = await multi.ReadAsync<DatabaseVLF>().AsList();

                    // Safe groups
                    var backupLookup = backups.GroupBy(b => b.DatabaseId).ToDictionary(g => g.Key, g => g.ToList());
                    var fileLookup = files.GroupBy(f => f.DatabaseId).ToDictionary(g => g.Key, g => g.ToList());
                    var vlfsLookup = vlfs.GroupBy(f => f.DatabaseId)
                        .ToDictionary(g => g.Key, g => g.FirstOrDefault());

                    foreach (var db in dbs)
                    {
                        db.Backups = backupLookup.TryGetValue(db.Id, out var b) ? b : [];
                        db.Files = fileLookup.TryGetValue(db.Id, out var f) ? f : [];
                        db.VLFCount = vlfsLookup.TryGetValue(db.Id, out var v) ? v?.VLFCount : null;
                    }
                }
                return dbs;
            },
            cacheDuration: 5.Minutes());

    public LightweightCache<List<DatabaseFile>> GetFileInfo(string databaseName) =>
        DatabaseFetch<DatabaseFile>(databaseName);

    public LightweightCache<List<DatabaseTable>> GetTableInfo(string databaseName) =>
        DatabaseFetch<DatabaseTable>(databaseName);

    public LightweightCache<List<DatabaseView>> GetViewInfo(string databaseName) =>
        DatabaseFetch<DatabaseView>(databaseName, 60.Seconds());

    public LightweightCache<List<StoredProcedure>> GetStoredProcedureInfo(string databaseName) =>
        DatabaseFetch<StoredProcedure>(databaseName, 60.Seconds());

    public LightweightCache<List<DatabaseBackup>> GetBackupInfo(string databaseName) =>
        DatabaseFetch<DatabaseBackup>(databaseName, RefreshInterval);

    public LightweightCache<List<MissingIndex>> GetMissingIndexes(string databaseName) =>
        DatabaseFetch<MissingIndex>(databaseName, RefreshInterval);

    public LightweightCache<List<RestoreHistory>> GetRestoreInfo(string databaseName) =>
        DatabaseFetch<RestoreHistory>(databaseName, RefreshInterval);

    public LightweightCache<List<DatabaseColumn>> GetColumnInfo(string databaseName) =>
        DatabaseFetch<DatabaseColumn>(databaseName);

    public LightweightCache<List<TableIndex>> GetIndexInfo(string databaseName) =>
        DatabaseFetch<TableIndex>(databaseName);

    public LightweightCache<List<DatabaseDataSpace>> GetDataSpaceInfo(string databaseName) =>
        DatabaseFetch<DatabaseDataSpace>(databaseName);

    public LightweightCache<List<DatabasePartition>> GetPartitionInfo(string databaseName) =>
        DatabaseFetch<DatabasePartition>(databaseName);

    public LightweightCache<List<DatabaseIndex>> GetIndexInfoDetiled(string databaseName) =>
        DatabaseFetch<DatabaseIndex>(databaseName);

    public Database GetDatabase(string databaseName) => Databases.Data?.FirstOrDefault(db => db.Name == databaseName);

    private LightweightCache<List<T>> DatabaseFetch<T>(string databaseName, TimeSpan? duration = null) where T : ISQLVersioned, new()
    {
        return TimedCache(typeof(T).Name + "Info-" + databaseName,
            conn =>
            {
                if (Engine.Edition != SQLServerEditions.Azure)
                {
                    conn.ChangeDatabase(databaseName);
                }
                return conn.Query<T>(GetFetchSQL<T>(), new { databaseName }).AsList();
            },
            duration ?? 5.Minutes(),
            staleDuration: 5.Minutes());
    }

    public static readonly HashSet<string> SystemDatabaseNames =
        [
            "master",
            "model",
            "msdb",
            "tempdb"
        ];

    public class Database : ISQLVersioned, IMonitorStatus
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string OverallStateDescription
        {
            get
            {
                if (IsReadOnly) return "Read-only";
                // TODO: Other statuses, e.g. Not Synchronizing
                return State.AsString(EnumFormat.Description);
            }
        }

        public MonitorStatus MonitorStatus
        {
            get
            {
                if (IsReadOnly)
                    return MonitorStatus.Warning;

                return State switch
                {
                    DatabaseStates.Restoring or DatabaseStates.Recovering or DatabaseStates.RecoveryPending => MonitorStatus.Unknown,
                    DatabaseStates.Copying => MonitorStatus.Warning,
                    DatabaseStates.Suspect or DatabaseStates.Emergency or DatabaseStates.Offline => MonitorStatus.Critical,
                    //case DatabaseStates.Online:
                    _ => MonitorStatus.Good,
                };
            }
        }

        public string MonitorStatusReason
        {
            get
            {
                if (IsReadOnly)
                    return Name + " database is read-only";

                return State switch
                {
                    DatabaseStates.Online => null,
                    _ => Name + " database is " + State.AsString(EnumFormat.Description),
                };
            }
        }

        private bool? _isSystemDatabase;
        public int Id { get; internal set; }
        public bool IsSystemDatabase => _isSystemDatabase ??= SystemDatabaseNames.Contains(Name);
        public string Name { get; internal set; }
        public DatabaseStates State { get; internal set; }
        public CompatabilityLevels CompatibilityLevel { get; internal set; }
        public RecoveryModels RecoveryModel { get; internal set; }
        public PageVerifyOptions PageVerifyOption { get; internal set; }
        public LogReuseWaits LogReuseWait { get; internal set; }
        public Guid? ReplicaId { get; internal set; }
        public Guid? GroupDatabaseId { get; internal set; }
        public UserAccesses UserAccess { get; internal set; }
        public bool IsFullTextEnabled { get; internal set; }
        public bool IsReadOnly { get; internal set; }
        public bool IsReadCommittedSnapshotOn { get; internal set; }
        public SnapshotIsolationStates SnapshotIsolationState { get; internal set; }
        public Containments? Containment { get; internal set; }
        public string LogVolumeId { get; internal set; }
        public double TotalSizeMB { get; internal set; }
        public double RowSizeMB { get; internal set; }
        public double StreamSizeMB { get; internal set; }
        public double TextIndexSizeMB { get; internal set; }
        public double? LogSizeMB { get; internal set; }
        public double? LogSizeUsedMB { get; internal set; }

        public List<DatabaseLastBackup> Backups { get; internal set; }
        public List<DatabaseFile> Files { get; internal set; }
        public int? VLFCount { get; internal set; }

        public double? LogPercentUsed => LogSizeMB > 0 ? 100 * LogSizeUsedMB / LogSizeMB : null;

        internal const string FetchSQL2012Columns = @"
       db.replica_id ReplicaId,
       db.group_database_id GroupDatabaseId,
       db.containment Containment, 
       v.LogVolumeId,
";
        internal const string FetchSQL2012Joins = @"
     Left Join (Select mf.database_id, vs.volume_id LogVolumeId
                  From sys.master_files mf
                       Cross Apply sys.dm_os_volume_stats(mf.database_id, mf.file_id) vs
                 Where type = 1) v On db.database_id = v.database_id
";

        internal const string FetchSQL = @"
Select db.database_id Id,
       db.name Name,
       db.state State,
       db.compatibility_level CompatibilityLevel,
       db.recovery_model RecoveryModel,
       db.page_verify_option PageVerifyOption,
       db.log_reuse_wait LogReuseWait,
       db.user_access UserAccess,
       db.is_fulltext_enabled IsFullTextEnabled,
       db.is_read_only IsReadOnly,
       db.is_read_committed_snapshot_on IsReadCommittedSnapshotOn,
       db.snapshot_isolation_state SnapshotIsolationState, {0}
       (Cast(st.TotalSize As Float)*8)/1024 TotalSizeMB,
       (Cast(sr.RowSize As Float)*8)/1024 RowSizeMB,
       (Cast(ss.StreamSize As Float)*8)/1024 StreamSizeMB,
       (Cast(sti.TextIndexSize As Float)*8)/1024 TextIndexSizeMB,
       Cast(logs.cntr_value as Float)/1024 LogSizeMB,
       Cast(logu.cntr_value as Float)/1024 LogSizeUsedMB
From sys.databases db
     Left Join sys.dm_os_performance_counters logu On db.name = logu.instance_name And logu.counter_name LIKE N'Log File(s) Used Size (KB)%' 
     Left Join sys.dm_os_performance_counters logs On db.name = logs.instance_name And logs.counter_name LIKE N'Log File(s) Size (KB)%' 
     Left Join (Select database_id, Sum(Cast(size As Bigint)) TotalSize 
                  From sys.master_files 
              Group By database_id) st On db.database_id = st.database_id
     Left Join (Select database_id, Sum(Cast(size As Bigint)) RowSize 
                  From sys.master_files 
                 Where type = 0 
              Group By database_id) sr On db.database_id = sr.database_id
     Left Join (Select database_id, Sum(Cast(size As Bigint)) StreamSize 
                  From sys.master_files 
                 Where type = 2
              Group By database_id) ss On db.database_id = ss.database_id
     Left Join (Select database_id, Sum(Cast(size As Bigint)) TextIndexSize 
                  From sys.master_files 
                 Where type = 4
              Group By database_id) sti On db.database_id = sti.database_id {1};";

        internal const string AzureFetchSQL = @"
Select db.database_id Id,
       db.name Name,
       db.state State,
       db.compatibility_level CompatibilityLevel,
       db.recovery_model RecoveryModel,
       db.page_verify_option PageVerifyOption,
       db.log_reuse_wait LogReuseWait,
       db.user_access UserAccess,
       db.is_fulltext_enabled IsFullTextEnabled,
       db.is_read_only IsReadOnly,
       db.is_read_committed_snapshot_on IsReadCommittedSnapshotOn,
       db.snapshot_isolation_state SnapshotIsolationState,
	   (Select Top 1 Cast(DatabasePropertyEx(DB_NAME(), 'MaxSizeInBytes') As bigint)/1024/1024) TotalSizeMB,
	   (Select (Cast(size As Bigint)*8)/1024 From sys.database_files Where type = 0) RowSizeMB,
	   (Select (Cast(size As Bigint)*8)/1024 From sys.database_files Where type = 2) StreamSizeMB,
	   (Select (Cast(size As Bigint)*8)/1024 From sys.database_files Where type = 4) TextIndexSize,
       Cast(logs.cntr_value as Float)/1024 LogSizeMB,
       Cast(logu.cntr_value as Float)/1024 LogSizeUsedMB
From sys.databases db
     Left Join sys.dm_os_performance_counters logu
       On db.physical_database_name = logu.instance_name And logu.counter_name LIKE N'Log File(s) Used Size (KB)%' 
     Left Join sys.dm_os_performance_counters logs
       On db.physical_database_name = logs.instance_name And logs.counter_name LIKE N'Log File(s) Size (KB)%'
Where db.database_id = DB_ID()";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return AzureFetchSQL;
            }

            if (e.Version >= SQLServerVersions.SQL2012.RTM)
            {
                return string.Format(FetchSQL, FetchSQL2012Columns, FetchSQL2012Joins);
            }

            return string.Format(FetchSQL, "", "");
        }
    }

    public class DatabaseLastBackup : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.AllExceptAzure;

        public int DatabaseId { get; internal set; }
        public string Name { get; internal set; }

        public char? LastBackupType { get; internal set; }
        public string LastBackupTypeDescription => DatabaseBackup.GetTypeDescription(LastBackupType);
        public DateTime? LastBackupStartDate { get; internal set; }
        public DateTime? LastBackupFinishDate { get; internal set; }
        public long? LastBackupSizeBytes { get; internal set; }
        public long? LastBackupCompressedSizeBytes { get; internal set; }
        public MediaDeviceTypes? LastBackupMediaDeviceType { get; internal set; }
        public string LastBackupLogicalDeviceName { get; internal set; }
        public string LastBackupPhysicalDeviceName { get; internal set; }

        public DateTime? LastFullBackupStartDate { get; internal set; }
        public DateTime? LastFullBackupFinishDate { get; internal set; }
        public long? LastFullBackupSizeBytes { get; internal set; }
        public long? LastFullBackupCompressedSizeBytes { get; internal set; }
        public MediaDeviceTypes? LastFullBackupMediaDeviceType { get; internal set; }
        public string LastFullBackupLogicalDeviceName { get; internal set; }
        public string LastFullBackupPhysicalDeviceName { get; internal set; }

        internal const string FetchSQL = @"
Select db.database_id DatabaseId,
       db.name Name, 
       lb.type LastBackupType,
       lb.backup_start_date LastBackupStartDate,
       lb.backup_finish_date LastBackupFinishDate,
       lb.backup_size LastBackupSizeBytes,
       lb.compressed_backup_size LastBackupCompressedSizeBytes,
       lbmf.device_type LastBackupMediaDeviceType,
       lbmf.logical_device_name LastBackupLogicalDeviceName,
       lbmf.physical_device_name LastBackupPhysicalDeviceName,
       fb.backup_start_date LastFullBackupStartDate,
       fb.backup_finish_date LastFullBackupFinishDate,
       fb.backup_size LastFullBackupSizeBytes,
       fb.compressed_backup_size LastFullBackupCompressedSizeBytes,
       fbmf.device_type LastFullBackupMediaDeviceType,
       fbmf.logical_device_name LastFullBackupLogicalDeviceName,
       fbmf.physical_device_name LastFullBackupPhysicalDeviceName
  From sys.databases db
       Left Outer Join (Select *
                          From (Select backup_set_id, 
                                       database_name,
                                       backup_start_date,
                                       backup_finish_date,
                                       backup_size,
                                       compressed_backup_size,
                                       media_set_id,
                                       type,
                                       Row_Number() Over(Partition By database_name Order By backup_start_date Desc) as rownum
                                  From msdb.dbo.backupset) b                                  
                           Where rownum = 1) lb 
         On lb.database_name = db.name
       Left Outer Join msdb.dbo.backupmediafamily lbmf
         On lb.media_set_id = lbmf.media_set_id And lbmf.media_count = 1
       Left Outer Join (Select *
                          From (Select backup_set_id, 
                                       database_name,
                                       backup_start_date,
                                       backup_finish_date,
                                       backup_size,
                                       compressed_backup_size,
                                       media_set_id,
                                       type,
                                       Row_Number() Over(Partition By database_name Order By backup_start_date Desc) as rownum
                                  From msdb.dbo.backupset
                                 Where type = 'D') b                                  
                           Where rownum = 1) fb 
         On fb.database_name = db.name
       Left Outer Join msdb.dbo.backupmediafamily fbmf
         On fb.media_set_id = fbmf.media_set_id And fbmf.media_count = 1";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return EmptyRecordsetSQL;
            }

            // Compressed backup info added in 2008
            if (e.Version < SQLServerVersions.SQL2008.RTM)
            {
                return FetchSQL.Replace("compressed_backup_size,", "null compressed_backup_size,");
            }
            return FetchSQL;
        }
    }

    public class MissingIndex : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2008.SP1;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public decimal AvgTotalUserCost { get; internal set; }
        public decimal AvgUserImpact { get; internal set; }
        public int UserSeeks { get; internal set; }
        public int UserScans { get; internal set; }
        public int UniqueCompiles { get; internal set; }
        public string EqualityColumns { get; internal set; }
        public string InEqualityColumns { get; internal set; }
        public string IncludedColumns { get; internal set; }
        public decimal EstimatedImprovement { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
 Select s.name SchemaName,
        o.name TableName,
        avg_total_user_cost AvgTotalUserCost,
        avg_user_impact AvgUserImpact,
        user_seeks UserSeeks,
        user_scans UserScans,
		unique_compiles UniqueCompiles,
		equality_columns EqualityColumns,
		inequality_columns InEqualityColumns,
		included_columns IncludedColumns,
        avg_total_user_cost* avg_user_impact *(user_seeks + user_scans) EstimatedImprovement
   From sys.dm_db_missing_index_details mid
        Join sys.dm_db_missing_index_groups mig On mig.index_handle = mid.index_handle
        Join sys.dm_db_missing_index_group_stats migs On migs.group_handle = mig.index_group_handle
        Join sys.databases d On d.database_id = mid.database_id
        Join sys.objects o On mid.object_id = o.object_id
        Join sys.schemas s On o.schema_id = s.schema_id
  Where d.name = @databaseName
    And avg_total_user_cost * avg_user_impact * (user_seeks + user_scans) > 0
  Order By EstimatedImprovement Desc";
    }

    public class TableIndex : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2008.SP1;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public string IndexName { get; internal set; }
        public DateTime? LastUpdated { get; internal set; }
        public IndexType Type { get; internal set; }
        public bool IsUnique { get; internal set; }
        public bool IsPrimaryKey { get; internal set; }
        public string ColumnNames { get; internal set; }
        public string IncludedColumnNames { get; internal set; }
        public bool HasFilter { get; internal set; }
        public string FilterDefinition { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
 Select sc.name SchemaName,
        t.name TableName,
		i.name IndexName, 
        Stats_Date(s.object_id, s.stats_id) LastUpdated,   
		i.type Type,
        i.is_unique IsUnique,
        i.is_primary_key IsPrimaryKey,
		i.is_disabled IsDisabled,
		Stuff((Select ', ' + c.name 
                 From sys.index_columns ic 
				      Join sys.columns c 
				        On ic.object_id = c.object_id 
				       And ic.column_id = c.column_id 
                Where i.object_id = ic.object_id 
				  And i.index_id = ic.index_id
				  and ic.is_included_column = 0
		     Order By ic.key_ordinal
                  For Xml Path('')), 1, 2, '') ColumnNames,
		Stuff((Select ', ' + c.name
                 From sys.index_columns ic 
				      Join sys.columns c 
				        On ic.object_id = c.object_id 
				       And ic.column_id = c.column_id 
                Where i.object_id = ic.object_id 
				  And i.index_id = ic.index_id
				  and ic.is_included_column = 1
		     Order By ic.key_ordinal
                  For Xml Path('')), 1, 2, '') IncludedColumnNames,
		i.has_filter HasFilter,
		i.filter_definition FilterDefinition
  From sys.indexes i 
       Join sys.tables t
         On i.object_id = t.object_id 
       Join sys.schemas sc
         On t.schema_id = sc.schema_id
       Join sys.stats s 
         On i.object_id = s.object_id
        And i.index_id = s.stats_id
 Where t.is_ms_shipped = 0 
Order By t.name, i.index_id";
    }

    public class StoredProcedure : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string SchemaName { get; internal set; }
        public string ProcedureName { get; internal set; }
        public DateTime CreationDate { get; internal set; }
        public DateTime LastModifiedDate { get; internal set; }
        public DateTime? LastExecuted { get; internal set; }
        public int? ExecutionCount { get; internal set; }
        public int? LastElapsedTime { get; internal set; }
        public int? MaxElapsedTime { get; internal set; }
        public int? MinElapsedTime { get; internal set; }
        public string Definition { get; internal set; }
        public string GetFetchSQL(in SQLServerEngine e) => @"
Select p.object_id,
       s.name as SchemaName,
       p.name ProcedureName,
       p.create_date CreationDate,
       p.modify_date LastModifiedDate,
       Max(ps.last_execution_time) as LastExecuted,
       Max(ps.execution_count) as ExecutionCount,
       Max(ps.last_elapsed_time/1000) as LastElapsedTime,
       Max(ps.max_elapsed_time/1000) as MaxElapsedTime,
       Max(ps.min_elapsed_time/1000) as MinElapsedTime,
       sm.definition [Definition]
  From sys.procedures p
       Join sys.schemas s
         On p.schema_id = s.schema_id
       Join sys.sql_modules sm 
         On p.object_id  = sm.object_id 
       Left Join sys.dm_exec_procedure_stats ps 
         On p.object_id	= ps.object_id
 Where p.is_ms_shipped = 0
 Group By p.object_id, s.name, p.name, p.create_date, p.modify_date, sm.definition";
    }

    public class RestoreHistory : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2008.SP1;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.AllExceptAzure;

        public DateTime RestoreFinishDate { get; internal set; }
        public string UserName { get; internal set; }
        public string BackupMedia { get; internal set; }
        public DateTime BackupStartDate { get; internal set; }
        public DateTime BackupFinishDate { get; internal set; }
        public char? RestoreType { get; internal set; }
        public string RestoreTypeDescription => GetTypeDescription(RestoreType);

        public static string GetTypeDescription(char? type) =>
            type switch
            {
                'D' => "Database",
                'F' => "File",
                'G' => "Filegroup",
                'I' => "Differential",
                'L' => "Log",
                'V' => "Verify Only",
                null => "",
                _ => "Unknown",
            };

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return EmptyRecordsetSQL;
            }

            return @"
Select r.restore_date RestoreFinishDate, 
       r.user_name UserName, 
       bmf.physical_device_name BackupMedia,
	   bs.backup_start_date BackupStartDate,
	   bs.backup_finish_date BackupFinishDate,
	   r.restore_type RestoreType
  From msdb.dbo.[restorehistory] r 
       Join [msdb].[dbo].[backupset] bs 
         On r.backup_set_id = bs.backup_set_id
       Join [msdb].[dbo].[backupmediafamily] bmf 
         On bs.media_set_id = bmf.media_set_id
 Where r.destination_database_name = @databaseName";
        }
    }

    public class DatabaseBackup : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.AllExceptAzure;

        public char? Type { get; internal set; }
        public string TypeDescription => GetTypeDescription(Type);
        public DateTime? StartDate { get; internal set; }
        public DateTime? FinishDate { get; internal set; }
        public long? SizeBytes { get; internal set; }
        public long? CompressedSizeBytes { get; internal set; }
        public MediaDeviceTypes? MediaDeviceType { get; internal set; }
        public string LogicalDeviceName { get; internal set; }
        public string PhysicalDeviceName { get; internal set; }

        public static string GetTypeDescription(char? type) =>
            type switch
            {
                'D' => "Full",
                'I' => "Differential (DB)",
                'L' => "Log",
                'F' => "File/Filegroup",
                'G' => "Differential (File)",
                'P' => "Partial",
                'Q' => "Differential (Partial)",
                null => "",
                _ => "Unknown",
            };

        internal const string FetchSQL = @"
Select Top 100
       b.type Type,
       b.backup_start_date StartDate,
       b.backup_finish_date FinishDate,
       b.backup_size SizeBytes,
       b.compressed_backup_size CompressedSizeBytes,
       bmf.device_type MediaDeviceType,
       bmf.logical_device_name LogicalDeviceName,
       bmf.physical_device_name PhysicalDeviceName
  From msdb.dbo.backupset b
       Left Join msdb.dbo.backupmediafamily bmf
         On b.media_set_id = bmf.media_set_id And bmf.media_count = 1
  Where b.database_name = @databaseName
  Order By FinishDate Desc";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return EmptyRecordsetSQL;
            }

            // Compressed backup info added in 2008
            if (e.Version < SQLServerVersions.SQL2008.RTM)
            {
                return FetchSQL.Replace("b.compressed_backup_size", "null");
            }
            return FetchSQL;
        }
    }

    public partial class DatabaseFile : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        private string _volumeMountPoint;
        public string VolumeMountPoint => _volumeMountPoint ??= PhysicalName?.Split(StringSplits.Colon)[0];
        public int DatabaseId { get; internal set; }
        public string DatabaseName { get; internal set; }
        public int FileId { get; internal set; }
        public string FileName { get; internal set; }
        public string PhysicalName { get; internal set; }
        public int DataSpaceId { get; internal set; }
        public DatabaseFileTypes FileType { get; internal set; }
        public DatabaseFileStates FileState { get; internal set; }
        public long FileSizePages { get; internal set; }
        public long FileMaxSizePages { get; internal set; }
        public long FileGrowthRaw { get; internal set; }
        public bool FileIsPercentGrowth { get; internal set; }
        public bool FileIsReadOnly { get; internal set; }
        public long StallReadMs { get; internal set; }
        public long NumReads { get; internal set; }
        public long StallWriteMs { get; internal set; }
        public long NumWrites { get; internal set; }
        public long UsedSizeBytes { get; internal set; }
        public long TotalSizeBytes { get; internal set; }

        public double AvgReadStallMs => NumReads == 0 ? 0 : StallReadMs / (double)NumReads;
        public double AvgWriteStallMs => NumWrites == 0 ? 0 : StallWriteMs / (double)NumWrites;

        private static readonly Regex _shortPathRegex = GetShortPathRegex();
        private string _shortPhysicalName;
        public string ShortPhysicalName =>
                _shortPhysicalName ??= _shortPathRegex.Replace(PhysicalName ?? "", @"C:\Program...MSSQLSERVER\MSSQL\DATA");

        public string GrowthDescription
        {
            get
            {
                if (FileGrowthRaw == 0) return "None";

                if (FileIsPercentGrowth) return FileGrowthRaw.ToString() + "%";

                // Growth that's not percent-based is 8KB pages rounded to the nearest 64KB
                return (FileGrowthRaw * 8 * 1024).ToHumanReadableSize();
            }
        }

        public string MaxSizeDescription =>
            FileMaxSizePages switch
            {
                0 => "At Max - No Growth",
                -1 => "No Limit - Disk Capacity",
                268435456 => "2 TB",
                _ => (FileMaxSizePages * 8 * 1024).ToHumanReadableSize(),
            };

        private const string NonAzureSQL = @"
Select mf.database_id DatabaseId,
       DB_Name(mf.database_id) DatabaseName,
       mf.file_id FileId,
       mf.name FileName,
       mf.physical_name PhysicalName,
       mf.data_space_id DataSpaceId,
       mf.type FileType,
       mf.state FileState,
       mf.size FileSizePages,
       mf.max_size FileMaxSizePages,
       mf.growth FileGrowthRaw,
       mf.is_percent_growth FileIsPercentGrowth,
       mf.is_read_only FileIsReadOnly,
       fs.io_stall_read_ms StallReadMs,
       fs.num_of_reads NumReads,
       fs.io_stall_write_ms StallWriteMs,
       fs.num_of_writes NumWrites,
       Cast(FileProperty(mf.name, 'SpaceUsed') As BigInt)*8*1024 UsedSizeBytes,
       Cast(mf.size as BigInt)*8*1024 TotalSizeBytes
  From sys.dm_io_virtual_file_stats(null, null) fs
       Join sys.master_files mf 
         On fs.database_id = mf.database_id
         And fs.file_id = mf.file_id
 Where (FileProperty(mf.name, 'SpaceUsed') Is Not Null Or DB_Name() = 'master')
";

        private const string AzureSQL = @"
Select DB_ID() DatabaseId,
       DB_Name() DatabaseName,
       mf.file_id FileId,
       mf.name FileName,
       mf.physical_name PhysicalName,
       mf.data_space_id DataSpaceId,
       mf.type FileType,
       mf.state FileState,
       mf.size FileSizePages,
       mf.max_size FileMaxSizePages,
       mf.growth FileGrowthRaw,
       mf.is_percent_growth FileIsPercentGrowth,
       mf.is_read_only FileIsReadOnly,
       fs.io_stall_read_ms StallReadMs,
       fs.num_of_reads NumReads,
       fs.io_stall_write_ms StallWriteMs,
       fs.num_of_writes NumWrites,
       Cast(FileProperty(mf.name, 'SpaceUsed') As BigInt)*8*1024 UsedSizeBytes,
       Cast(mf.size as BigInt)*8*1024 TotalSizeBytes
  From sys.dm_io_virtual_file_stats(null, null) fs
       Join sys.database_files mf 
         On fs.database_id = DB_ID()
         And fs.file_id = mf.file_id
 Where (FileProperty(mf.name, 'SpaceUsed') Is Not Null Or DB_Name() = 'master')
";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return AzureSQL;
            }

            return NonAzureSQL;
        }

        [GeneratedRegex(@"C:\\Program Files\\Microsoft SQL Server\\MSSQL\d+.MSSQLSERVER\\MSSQL\\DATA", RegexOptions.Compiled)]
        private static partial Regex GetShortPathRegex();
    }

    public class DatabaseDataSpace : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public string Type { get; internal set; }
        public string TypeDescription => GetTypeDescription(Type);
        public bool IsDefault { get; internal set; }
        public bool IsSystem { get; internal set; }

        public static string GetTypeDescription(string type) =>
            type switch
            {
                "FG" => "Filegroup",
                "PS" => "Partition Scheme",
                "FD" => "FILESTREAM",
                null => "",
                _ => "Unknown",
            };

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select data_space_id Id,
       name Name,
       type Type,
       is_default IsDefault,
       is_system IsSystem
  From sys.data_spaces";
    }

    public class DatabaseVLF : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.AllExceptAzure;

        public int DatabaseId { get; internal set; }
        public string DatabaseName { get; internal set; }
        public int VLFCount { get; internal set; }

        internal const string FetchSQL = @"
Create Table #VLFCounts (DatabaseId int, DatabaseName sysname, VLFCount int);
Create Table #vlfTemp (
    RecoveryUnitId int,
    FileId int, 
    FileSize nvarchar(255), 
    StartOffset nvarchar(255), 
    FSeqNo nvarchar(255), 
    Status int, 
    Parity int, 
    CreateLSN nvarchar(255)
);

Declare @dbId int, @dbName sysname;
Declare dbs Cursor Local Fast_Forward For (
    Select db.database_id, db.name From sys.databases db
    Left Join sys.database_mirroring m ON db.database_id = m.database_id
    Where db.state <> 6
        and ( db.state <> 1 
            or ( m.mirroring_role = 2 and m.mirroring_state = 4 )
            )
    );
Open dbs;
Fetch Next From dbs Into @dbId, @dbName;
While @@FETCH_STATUS = 0
Begin
    IF IS_SRVROLEMEMBER ('sysadmin') = 1
       Insert Into #vlfTemp
       Exec('DBCC LOGINFO(''' + @dbName + ''') WITH NO_INFOMSGS');
    Insert Into #VLFCounts (DatabaseId, DatabaseName, VLFCount)
    Values (@dbId, @dbName, @@ROWCOUNT);
    Truncate Table #vlfTemp;
    Fetch Next From dbs Into @dbId, @dbName;
End
Close dbs;
Deallocate dbs;

Select * From #VLFCounts;

Drop Table #VLFCounts;
Drop Table #vlfTemp;";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Edition == SQLServerEditions.Azure)
            {
                return EmptyRecordsetSQL;
            }

            if (e.Version < SQLServerVersions.SQL2012.RTM)
            {
                return FetchSQL.Replace("RecoveryUnitId int,", "");
            }

            return FetchSQL;
        }
    }

    public class DatabaseTable : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public int Id { get; internal set; }
        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public DateTime CreationDate { get; internal set; }
        public DateTime LastModifiedDate { get; internal set; }
        public int IndexCount { get; internal set; }
        public long RowCount { get; internal set; }
        public long PartitionCount { get; internal set; }
        public long DataTotalSpaceKB { get; internal set; }
        public long IndexTotalSpaceKB { get; internal set; }
        public long UsedSpaceKB { get; internal set; }
        public long TotalSpaceKB { get; internal set; }
        public long FreeSpaceKB => TotalSpaceKB - UsedSpaceKB;
        public TableTypes TableType { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select object_id, index_id, type Into #indexes From sys.indexes;
Select object_id, index_id, partition_id Into #parts From sys.partitions;
Select object_id, index_id, row_count, partition_id Into #partStats From sys.dm_db_partition_stats;

Select t.object_id Id,
       s.name SchemaName,
       t.name TableName,
       t.create_date CreationDate,
       t.modify_date LastModifiedDate,
       Count(Distinct i.index_id) IndexCount,
       Max(ddps.row_count) [RowCount],
       Count(Distinct (Case When i.type In (0, 1, 5) Then p.partition_id Else Null End)) PartitionCount,
       Sum(Case When i.type In (0, 1, 5) Then a.total_pages Else 0 End) * 8 DataTotalSpaceKB,
       Sum(Case When i.type Not In (0, 1, 5) Then a.total_pages Else 0 End) * 8 IndexTotalSpaceKB,
       Sum(a.used_pages) * 8 UsedSpaceKB,
       Sum(a.total_pages) * 8 TotalSpaceKB,
       (Case Max(i.type) When 0 Then 0 Else 1 End) as TableType
  From sys.tables t
       Join sys.schemas s
         On t.schema_id = s.schema_id
       Join #indexes i 
         On t.object_id = i.object_id
       Join #parts p 
         On i.object_id = p.object_id 
         And i.index_id = p.index_id
       Join (Select container_id,
                    Sum(used_pages) used_pages,
                    Sum(total_pages) total_pages
               From sys.allocation_units
           Group By container_id) a
         On p.partition_id = a.container_id
       Left Join #partStats ddps
         On i.object_id = ddps.object_id
         And i.index_id = ddps.index_id
         And i.type In (0, 1, 5) -- Heap, Clustered, Clustered Columnstore      
         And p.partition_id = ddps.partition_id  
 Where t.is_ms_shipped = 0
   And i.object_id > 255
Group By t.object_id, t.Name, t.create_date, t.modify_date, s.name;

Drop Table #indexes;
Drop Table #parts;
Drop Table #partStats;";
    }

    public class DatabaseView : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public int Id { get; internal set; }
        public string SchemaName { get; internal set; }
        public string ViewName { get; internal set; }
        public DateTime CreationDate { get; internal set; }
        public DateTime LastModifiedDate { get; internal set; }
        public bool IsReplicated { get; internal set; }
        public string Definition { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select v.object_id Id,
       s.name SchemaName,
       v.name ViewName,
       v.create_date CreationDate,
       v.modify_date LastModifiedDate,
       v.is_replicated IsReplicated,
       sm.definition Definition
  From sys.views v
       Join sys.schemas s
         On v.schema_id = s.schema_id
       Join sys.sql_modules sm 
         On sm.object_id = v.object_id  
 Where v.is_ms_shipped = 0";
    }

    public class DatabaseColumn : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string Id => SchemaName + "." + TableName + "." + ColumnName;

        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public string ViewName { get; internal set; }
        public int Position { get; internal set; }
        public int ObjectId { get; internal set; }
        public string ColumnName { get; internal set; }
        public string DataType { get; internal set; }
        public bool IsNullable { get; internal set; }
        public int MaxLength { get; internal set; }
        public byte Scale { get; internal set; }
        public byte Precision { get; internal set; }
        public string ColumnDefault { get; internal set; }
        public string CollationName { get; internal set; }
        public bool IsIdentity { get; internal set; }
        public bool IsComputed { get; internal set; }
        public bool IsFileStream { get; internal set; }
        public bool IsSparse { get; internal set; }
        public bool IsColumnSet { get; internal set; }
        public string PrimaryKeyConstraint { get; internal set; }
        public string ForeignKeyConstraint { get; internal set; }
        public string ForeignKeyTargetSchema { get; internal set; }
        public string ForeignKeyTargetTable { get; internal set; }
        public string ForeignKeyTargetColumn { get; internal set; }

        public string DataTypeDescription
        {
            get
            {
                var props = new List<string>();
                if (IsSparse) props.Add("sparse");
                switch (DataType)
                {
                    case "varchar":
                    case "nvarchar":
                    case "varbinary":
                        props.Add($"{DataType}({(MaxLength == -1 ? "max" : MaxLength.ToString())})");
                        break;
                    case "decimal":
                    case "numeric":
                        props.Add($"{DataType}({Scale},{Precision})");
                        break;
                    default:
                        props.Add(DataType);
                        break;
                }
                props.Add(IsNullable ? "null" : "not null");
                return string.Join(", ", props);
            }
        }

        // For non-SQL later
        //            internal const string FetchSQL = @"
        //Select c.TABLE_SCHEMA SchemaName,
        //       c.TABLE_NAME TableName,
        //       c.ORDINAL_POSITION Position,
        //       c.COLUMN_NAME ColumnName,
        //       c.COLUMN_DEFAULT ColumnDefault,
        //       Cast(Case c.IS_NULLABLE When 'YES' Then 1 Else 0 End as BIT) IsNullable,
        //       c.DATA_TYPE DataType,
        //       c.CHARACTER_MAXIMUM_LENGTH MaxLength,
        //       c.NUMERIC_PRECISION Precision,
        //       c.NUMERIC_PRECISION_RADIX NumericPrecisionRadix,
        //       c.NUMERIC_SCALE NumericScale,
        //       c.DATETIME_PRECISION DatetimePrecision,
        //       c.COLLATION_NAME CollationName,
        //       kcu.PrimaryKeyConstraint,
        //       kcu.ForeignKeyConstraint,
        //       kcu.ForeignKeyTargetSchema,
        //       kcu.ForeignKeyTargetTable,
        //       kcu.ForeignKeyTargetColumn
        //  From INFORMATION_SCHEMA.COLUMNS c
        //       Left Join (Select cu.TABLE_SCHEMA, 
        //                         cu.TABLE_NAME, 
        //                         cu.COLUMN_NAME,
        //                         (Case When OBJECTPROPERTY(OBJECT_ID(cu.CONSTRAINT_NAME), 'IsPrimaryKey') = 1
        //                               Then cu.CONSTRAINT_NAME
        //                               Else Null
        //                          End) as PrimaryKeyConstraint,
        //                          rc.CONSTRAINT_NAME ForeignKeyConstraint,
        //                          cut.TABLE_SCHEMA ForeignKeyTargetSchema,
        //                          cut.TABLE_NAME ForeignKeyTargetTable,
        //                          cut.COLUMN_NAME ForeignKeyTargetColumn
        //                    From INFORMATION_SCHEMA.KEY_COLUMN_USAGE cu
        //                         Left Join INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
        //                           On cu.CONSTRAINT_CATALOG = rc.CONSTRAINT_CATALOG
        //                          And cu.CONSTRAINT_SCHEMA = rc.CONSTRAINT_SCHEMA
        //                          And cu.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
        //                         Left Join INFORMATION_SCHEMA.KEY_COLUMN_USAGE cut
        //                          On rc.UNIQUE_CONSTRAINT_CATALOG = cut.CONSTRAINT_CATALOG
        //                          And rc.UNIQUE_CONSTRAINT_SCHEMA = cut.CONSTRAINT_SCHEMA
        //                          And rc.UNIQUE_CONSTRAINT_NAME = cut.CONSTRAINT_NAME) kcu
        //         On c.TABLE_SCHEMA = kcu.TABLE_SCHEMA
        //         And c.TABLE_NAME = kcu.TABLE_NAME
        //         And c.COLUMN_NAME = kcu.COLUMN_NAME
        //Order By c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION";
        internal const string FetchSQL2008Columns = @"
       c.is_sparse IsSparse,
       c.is_column_set IsColumnSet,
";

        internal const string FetchSQL = @"
Select s.name SchemaName,
       t.name TableName,
       v.name ViewName,
       c.column_id Position,
       c.object_id ObjectId,
       c.name ColumnName,
       ty.name DataType,
       c.is_nullable IsNullable,
       (Case When ty.name In ('nchar', 'ntext','nvarchar') And c.max_length <> -1 Then c.max_length / 2 Else c.max_length End) MaxLength,
       c.scale Scale,
       c.precision Precision,
       object_definition(c.default_object_id) ColumnDefault,
       c.collation_name CollationName,
       c.is_identity IsIdentity,
       c.is_computed IsComputed,
       c.is_filestream IsFileStream, {0}
       (Select Top 1 i.name
          From sys.indexes i 
               Join sys.index_columns ic On i.object_id = ic.object_id And i.index_id = ic.index_id
         Where i.object_id = t.object_id
           And ic.column_id = c.column_id
           And i.is_primary_key = 1) PrimaryKeyConstraint,
       object_name(fkc.constraint_object_id) ForeignKeyConstraint,
       fs.name ForeignKeyTargetSchema,
       ft.name ForeignKeyTargetTable,
       fc.name ForeignKeyTargetColumn
  From sys.columns c
       Left Join sys.tables t On c.object_id = t.object_id
       Left Join sys.views v On c.object_id = v.object_id
       Join sys.schemas s On (t.schema_id = s.schema_id Or v.schema_id = s.schema_id)
       Join sys.types ty On c.user_type_id = ty.user_type_id
       Left Join sys.foreign_key_columns fkc On fkc.parent_object_id = t.object_id And fkc.parent_column_id = c.column_id
       Left Join sys.tables ft On fkc.referenced_object_id = ft.object_id
       Left Join sys.schemas fs On ft.schema_id = fs.schema_id
       Left Join sys.columns fc On fkc.referenced_object_id = fc.object_id And fkc.referenced_column_id = fc.column_id
Order By 1, 2, 3";

        public string GetFetchSQL(in SQLServerEngine e)
        {
            if (e.Version >= SQLServerVersions.SQL2008.RTM)
                return string.Format(FetchSQL, FetchSQL2008Columns);

            return string.Format(FetchSQL, "");
        }
    }

    public class DatabasePartition : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public string IndexName { get; internal set; }
        public int PartitionNumber { get; internal set; }
        public string Filegroup { get; internal set; }
        public string Scheme { get; internal set; }
        public string Function { get; internal set; }
        public string FunctionType { get; internal set; }
        public int Fanout { get; internal set; }
        public bool IsRight { get; internal set; }
        public object RangeValue { get; internal set; }
        public PartitionDataCompression DataCompression { get; internal set; }
        public long RowCount { get; internal set; }
        public long ReservedSpaceKB { get; internal set; }
        public int IndexCount { get; internal set; }

        public string RangeValueString =>
            RangeValue switch
            {
                null => string.Empty,
                DateTime date => date.ToString(date.TimeOfDay.Ticks == 0 ? "yyyy-MM-dd" : "u"),
                _ => RangeValue.ToString(),
            };

        public string GetFetchSQL(in SQLServerEngine e) => @"
  Select s.name SchemaName,
         t.name TableName,
         i.name IndexName,
         p.partition_number PartitionNumber,
         ds.name [Filegroup],
         ps.name Scheme,
         pf.name [Function],
         pf.type_desc FunctionType,
         pf.fanout Fanout,
         pf.boundary_value_on_right IsRight,
         prv.value RangeValue,
         p.data_compression DataCompression,
         Sum(Case When i.index_id In (1, 0) Then p.rows Else 0 End) [RowCount],
         Sum(dbps.reserved_page_count) * 8 ReservedSpaceKB,
         Sum(Case IsNull(i.index_id, 0) When 0 Then 0 Else 1 End) IndexCount
    From sys.destination_data_spaces dds
         Join sys.data_spaces ds 
           On dds.data_space_id = ds.data_space_id
         Join sys.partition_schemes ps 
           On dds.partition_scheme_id = ps.data_space_id
         Join sys.partition_functions pf 
           On ps.function_id = pf.function_id
         Left Join sys.partition_range_values prv 
           On pf.function_id = prv.function_id
           And dds.destination_id = (Case pf.boundary_value_on_right When 0 Then prv.boundary_id Else prv.boundary_id + 1 End)
         Left Join sys.indexes i 
           On dds.partition_scheme_id = i.data_space_id
         Left Join sys.tables t
           On i.object_id = t.object_id
         Left Join sys.schemas s
           On t.schema_id = s.schema_id
         Left Join sys.partitions p 
           On i.object_id = p.object_id
           And i.index_id = p.index_id
           And dds.destination_id = p.partition_number
         Left Join sys.dm_db_partition_stats dbps 
           On p.object_id = dbps.object_id
           And p.partition_id = dbps.partition_id
Group By t.name,
         s.name,
         i.name,
         p.partition_number,
         ds.name,
         ps.name,
         pf.name,
         pf.type_desc,
         pf.fanout,
         pf.boundary_value_on_right,
         prv.value,
         p.data_compression";
    }

    public class DatabaseIndex : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string SchemaName { get; internal set; }
        public string TableName { get; internal set; }
        public string IndexName { get; internal set; }
        public int IndexId { get; internal set; }
        public IndexType IndexType { get; internal set; }
        public DateTime? LastUserRead { get; internal set; }
        public DateTime? LastUserUpdate { get; internal set; }
        public string Definition { get; internal set; }
        public string KeyDefinition { get; internal set; }
        public string IncludeDefinition { get; internal set; }
        public string FilterDefinition { get; internal set; }
        public long ReservedInRowBytes { get; internal set; }
        public long ReservedLobBytes { get; internal set; }
        public long RowCount { get; internal set; }
        public long UserSeeks { get; internal set; }
        public long UserScans { get; internal set; }
        public long UserLookups { get; internal set; }
        public long UserUpdates { get; internal set; }
        public int PartitionCount { get; internal set; }
        public bool AllowPageLocks { get; internal set; }
        public bool AllowRowLocks { get; internal set; }
        public bool IsHypothetical { get; internal set; }
        public bool IsPrimaryKey { get; internal set; }
        public bool IsFiltered { get; internal set; }
        public bool IsUnique { get; internal set; }
        public byte FillFactor { get; internal set; }
        public string PartitionFunction { get; internal set; }
        public string PartitionScheme { get; internal set; }
        public string Filegroup { get; internal set; }
        public DateTime? StatsLastUpdated { get; internal set; }

        public long TotalBytes => ReservedInRowBytes + ReservedLobBytes;

        // A slightly tweaked version of the awesome index creation query by Kendra Little
        // Blog link: https://littlekendra.com/2016/05/05/how-to-script-out-indexes-from-sql-server/
        // Licensed under MIT: https://gist.github.com/LitKnd/2668396699c82220384d2ca2c19bbc32
        public string GetFetchSQL(in SQLServerEngine e) => @"
Select sc.name SchemaName,
       t.name AS TableName,
       si.name IndexName,
       si.index_id IndexId,
       si.Type IndexType,
       Cast((Select Max(user_reads) 
          From (VALUES (last_user_seek), (last_user_scan), (last_user_lookup)) value(user_reads)) as DateTime) LastUserRead,
       last_user_update LastUserUpdate,
       Case si.index_id When 0 Then N'/* No create statement (Heap) */'
       Else 
           Case is_primary_key When 1 Then
               N'ALTER TABLE ' + QuoteName(sc.name) + N'.' + QuoteName(t.name) + + char(10) + N'  ADD CONSTRAINT ' + QuoteName(si.name) + N' PRIMARY KEY ' +
                   Case When si.index_id > 1 Then N'NON' Else N'' End + N'CLUSTERED '
               Else N'CREATE ' + 
                   Case When si.is_unique = 1 Then N'UNIQUE ' Else N'' End +
                   Case When si.index_id > 1 Then N'NON' Else N'' End + N'CLUSTERED ' +
                   N'INDEX ' + QuoteName(si.name) + N' ON ' + QuoteName(sc.name) + N'.' + QuoteName(t.name) + char(10) + N' '
           End +
           /* key def */ N'(' + key_definition + N')' +
           /* includes */ Case When include_definition Is Not Null Then 
               char(10) + N' INCLUDE (' + include_definition + N')'
               Else N''
           End +
           /* filters */ Case When filter_definition Is Not Null Then 
               char(10) + N' WHERE ' + filter_definition Else N''
           End +
           /* with clause - compression goes here */
           Case When row_compression_partition_list Is Not Null OR page_compression_partition_list Is Not Null 
               Then char(10) + N' WITH (' +
                   Case When row_compression_partition_list Is Not Null Then
                       N'DATA_COMPRESSION = ROW ' + Case When psc.name IS NULL Then N'' Else + N' ON PARTITIONS (' + row_compression_partition_list + N')' End
                   Else N'' End +
                   Case When row_compression_partition_list Is Not Null AND page_compression_partition_list Is Not Null Then N', ' Else N'' End +
                   Case When page_compression_partition_list Is Not Null Then
                       N'DATA_COMPRESSION = PAGE ' + Case When psc.name IS NULL Then N'' Else + N' ON PARTITIONS (' + page_compression_partition_list + N')' End
                   Else N'' End
               + N')'
               Else N''
           End +
           /* ON where? filegroup? partition scheme? */
           ' ON ' + Case When psc.name is null 
               Then ISNULL(QuoteName(fg.name),N'')
               Else psc.name + N' (' + partitioning_column.column_name + N')' 
               End
           + N';'
       End Definition,
       key_definition KeyDefinition,
	   include_definition IncludeDefinition,
       filter_definition FilterDefinition,
       partition_sums.reserved_in_row_bytes ReservedInRowBytes,
       partition_sums.reserved_LOB_bytes ReservedLobBytes,
       partition_sums.row_count [RowCount],
       stat.user_seeks UserSeeks,
       stat.user_scans UserScans,
       stat.user_lookups UserLookups,
       user_updates UserUpdates,
       partition_sums.partition_count PartitionCount,
       si.allow_page_locks AllowPageLocks,
       si.allow_row_locks AllowRowLocks,
       si.is_hypothetical IsHypothetical,
       si.is_primary_key IsPrimaryKey,
       si.has_filter IsFiltered,
       si.is_unique IsUnique,
       si.fill_factor [FillFactor],
       pf.name PartitionFunction,
       psc.name PartitionScheme,
       fg.name Filegroup,
       Stats_Date(si.object_id, si.index_id) StatsLastUpdated
  From sys.indexes si
       Join sys.tables t On si.object_id = t.object_id
       Join sys.schemas sc On t.schema_id = sc.schema_id
       Left Join sys.dm_db_index_usage_stats stat
                 On stat.database_id = DB_ID()
                 And si.object_id = stat.object_id
                 And si.index_id=stat.index_id
       Left Join sys.partition_schemes psc On si.data_space_id = psc.data_space_id
       Left Join sys.partition_functions pf On psc.function_id = pf.function_id
       Left Join sys.filegroups fg On si.data_space_id = fg.data_space_id
       Outer Apply (Select Stuff( /* Key list */
                        (Select N', ' + QuoteName(c.name) + (Case ic.is_descending_key When 1 then N' DESC' Else N'' End)
                      From sys.index_columns ic 
                           Join sys.columns c
                                On ic.column_id = c.column_id
                                And ic.object_id = c.object_id
                     Where ic.object_id = si.object_id
                       And ic.index_id = si.index_id
                       And ic.key_ordinal > 0
                  Order By ic.key_ordinal For XML Path(''), Type).value('.', 'NVARCHAR(MAX)'),1,2,'')) keys (key_definition)
       Outer Apply (Select Max(QuoteName(c.name)) column_name /* Partitioning Ordinal */
                      From sys.index_columns ic 
                           Join sys.columns c
                                On ic.column_id = c.column_id
                                And ic.object_id = c.object_id
                     Where ic.object_id = si.object_id
                       And ic.index_id = si.index_id
                       And ic.partition_ordinal = 1) partitioning_column
       Outer Apply (Select Stuff( /* Include list */
                        (Select N', ' + QuoteName(c.name)
                           From sys.index_columns ic
                                Join sys.columns c
                                     On ic.column_id = c.column_id
                                     And ic.object_id = c.object_id
                          Where ic.object_id = si.object_id
                            And ic.index_id = si.index_id
                            And ic.is_included_column = 1
                       Order By c.name For XML Path(''), Type).value('.', 'NVARCHAR(MAX)'),1,2,'')) includes (include_definition)
      Outer Apply (Select Count(*) partition_count, /* Partitions */
                          Sum(ps.in_row_reserved_page_count)*8*2014 reserved_in_row_bytes,
                          Sum(ps.lob_reserved_page_count)*8*1024 reserved_LOB_bytes,
                          Sum(ps.row_count) row_count
                     From sys.partitions p
                          Join sys.dm_db_partition_stats ps 
                               On p.partition_id = ps.partition_id
                    Where p.object_id = si.object_id
                      And p.index_id = si.index_id) partition_sums
      Outer Apply (Select Stuff( /* row compression list by partition */
                        (Select N', ' + Cast(p.partition_number AS VARCHAR(32))
                           From sys.partitions p
                          Where p.object_id = si.object_id
                            And p.index_id = si.index_id
                            And p.data_compression = 1
                       Order By p.partition_number For XML Path(''), Type).value('.', 'NVARCHAR(MAX)'),1,2,'')) row_compression_clause (row_compression_partition_list)
      Outer Apply (Select Stuff( /* data compression list by partition */
                        (Select N', ' + Cast(p.partition_number AS VARCHAR(32))
                           From sys.partitions p
                          Where p.object_id = si.object_id
                            And p.index_id = si.index_id
                            And p.data_compression = 2
                       Order By p.partition_number For XML Path(''), Type).value('.', 'NVARCHAR(MAX)'),1,2,'')) page_compression_clause (page_compression_partition_list)
 Where si.type In (0,1,2) /* heap, clustered, nonclustered */
Order By sc.name, t.name, si.index_id
Option (Recompile);";
    }
}
