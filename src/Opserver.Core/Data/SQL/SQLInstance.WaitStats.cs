namespace Opserver.Data.SQL;

public partial class SQLInstance
{
    private Cache<List<WaitStatRecord>> _waitStats;
    public Cache<List<WaitStatRecord>> WaitStats =>
        _waitStats ??= GetSqlCache(
            nameof(WaitStats), conn =>
            {
                var sql = GetFetchSQL<WaitStatRecord>();
                return conn.QueryAsync<WaitStatRecord>(sql, new { secondsBetween = 15 });
            });

    public class WaitStatRecord : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string WaitType { get; internal set; }
        public int SecondsBetween { get; internal set; }
        public DateTime CreationDate { get; internal set; }
        public long WaitTimeMs { get; internal set; }
        public long WaitTaskCount { get; internal set; }

        private bool? _isIgnorable;

        public bool IsIgnorable => _isIgnorable ??= IsIgnorableWait(WaitType);

        public static bool IsIgnorableWait(string waitType)
            => waitType switch
            {
                "BROKER_EVENTHANDLER" or
                "BROKER_RECEIVE_WAITFOR" or
                "BROKER_TASK_STOP" or
                "BROKER_TO_FLUSH" or
                "CHECKPOINT_QUEUE" or
                "CLR_AUTO_EVENT" or
                "CLR_MANUAL_EVENT" or
                "DBMIRROR_DBM_MUTEX" or
                "DBMIRROR_EVENTS_QUEUE" or
                "DBMIRRORING_CMD" or
                "DIRTY_PAGE_POLL" or
                "DISPATCHER_QUEUE_SEMAPHORE" or
                "FT_IFTS_SCHEDULER_IDLE_WAIT" or
                "FT_IFTSHC_MUTEX" or
                "HADR_FILESTREAM_IOMGR_IOCOMPLETION" or
                "LAZYWRITER_SLEEP" or
                "LOGMGR_QUEUE" or
                "ONDEMAND_TASK_QUEUE" or
                "QDS_CLEANUP_STALE_QUERIES_TASK_MAIN_LOOP_SLEEP" or
                "QDS_PERSIST_TASK_MAIN_LOOP_SLEEP" or
                "REQUEST_FOR_DEADLOCK_SEARCH" or
                "SLEEP_TASK" or
                "SP_SERVER_DIAGNOSTICS_SLEEP" or
                "SQLTRACE_BUFFER_FLUSH" or
                "SQLTRACE_INCREMENTAL_FLUSH_SLEEP" or
                "WAITFOR" or
                "XE_DISPATCHER_WAIT" or
                "XE_TIMER_EVENT" => true,
                _ => false,
            };

        public double AverageWaitTime => (double)WaitTimeMs / SecondsBetween;

        public double AverageTaskCount => (double)WaitTaskCount / SecondsBetween;

        public string GetFetchSQL(in SQLServerEngine e) => @"
Declare @delayInterval char(8) = Convert(Char(8), DateAdd(Second, @secondsBetween, '00:00:00'), 108);

If Object_Id('tempdb..#PWaitStats') Is Not Null
    Drop Table #PWaitStats;
If Object_Id('tempdb..#CWaitStats') Is Not Null
    Drop Table #CWaitStats;

  Select wait_type WaitType,
         GETDATE() CreationDate,
         Sum(wait_time_ms) WaitTimeMs,
         Sum(waiting_tasks_count) WaitTaskCount
    Into #PWaitStats
    From sys.dm_os_wait_stats
Group By wait_type;

WaitFor Delay @delayInterval;

  Select wait_type WaitType,
         GETDATE() CreationDate,
         Sum(wait_time_ms) WaitTimeMs,
         Sum(waiting_tasks_count) WaitTaskCount
    Into #CWaitStats
    From sys.dm_os_wait_stats
Group By wait_type;

Select cw.WaitType,
       DateDiff(Second, pw.CreationDate, cw.CreationDate) SecondsBetween,
       cw.CreationDate,
       cw.WaitTimeMs - pw.WaitTimeMs WaitTimeMs,
       cw.WaitTaskCount - pw.WaitTaskCount WaitTaskCount
  From #PWaitStats pw
       Join #CWaitStats cw On pw.WaitType = cw.WaitType
 Where cw.WaitTaskCount - pw.WaitTaskCount > 0

Drop Table #PWaitStats;
Drop Table #CWaitStats;
";
    }
}
