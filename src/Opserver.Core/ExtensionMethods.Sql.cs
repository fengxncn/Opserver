﻿using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Dapper;

namespace Opserver;

public static partial class ExtensionMethods
{
    public static async Task<List<T>> AsList<T>(this Task<IEnumerable<T>> source)
    {
        var result = await source;
        return result != null && result is not List<T> ? result.ToList() : (List<T>) result;
    }

    public static async Task<int> ExecuteAsync(this DbConnection conn, string sql, dynamic param = null, IDbTransaction transaction = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null, int? commandTimeout = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await SqlMapper.ExecuteAsync(conn, MarkSqlString(sql, fromFile, onLine, comment), param as object, transaction, commandTimeout: commandTimeout);
        }
    }

    public static async Task<T> QueryFirstOrDefaultAsync<T>(this DbConnection conn, string sql, dynamic param = null, int? commandTimeout = null, IDbTransaction transaction = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryFirstOrDefaultAsync<T>(MarkSqlString(sql, fromFile, onLine, comment), param as object, transaction, commandTimeout);
        }
    }

    public static async Task<List<T>> QueryAsync<T>(this DbConnection conn, string sql, dynamic param = null, int? commandTimeout = null, IDbTransaction transaction = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryAsync<T>(MarkSqlString(sql, fromFile, onLine, comment), param as object, transaction, commandTimeout).AsList();
        }
    }

    public static async Task<List<TReturn>> QueryAsync<TFirst, TSecond, TReturn>(this DbConnection conn, string sql, Func<TFirst, TSecond, TReturn> map, dynamic param = null, IDbTransaction transaction = null, string splitOn = "Id", int? commandTimeout = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryAsync(MarkSqlString(sql, fromFile, onLine, comment), map, param as object, transaction, true, splitOn, commandTimeout).AsList();
        }
    }

    public static async Task<List<TReturn>> QueryAsync<TFirst, TSecond, TThird, TReturn>(this DbConnection conn, string sql, Func<TFirst, TSecond, TThird, TReturn> map, dynamic param = null, IDbTransaction transaction = null, string splitOn = "Id", int? commandTimeout = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryAsync(MarkSqlString(sql, fromFile, onLine, comment), map, param as object, transaction, true, splitOn, commandTimeout).AsList();
        }
    }

    public static async Task<List<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TReturn>(this DbConnection conn, string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, dynamic param = null, IDbTransaction transaction = null, string splitOn = "Id", int? commandTimeout = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryAsync(MarkSqlString(sql, fromFile, onLine, comment), map, param as object, transaction, true, splitOn, commandTimeout).AsList();
        }
    }

    public static async Task<List<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(this DbConnection conn, string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, dynamic param = null, IDbTransaction transaction = null, string splitOn = "Id", int? commandTimeout = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await conn.QueryAsync(MarkSqlString(sql, fromFile, onLine, comment), map, param as object, transaction, true, splitOn, commandTimeout).AsList();
        }
    }

    public static async Task<SqlMapper.GridReader> QueryMultipleAsync(this DbConnection conn, string sql, dynamic param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null, [CallerFilePath]string fromFile = null, [CallerLineNumber]int onLine = 0, string comment = null)
    {
        using (await conn.EnsureOpenAsync())
        {
            return await SqlMapper.QueryMultipleAsync(conn, MarkSqlString(sql, fromFile, onLine, comment), param, transaction, commandTimeout, commandType);
        }
    }

    public static async Task<IDisposable> EnsureOpenAsync(this DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        switch (connection.State)
        {
            case ConnectionState.Open:
                return null;
            case ConnectionState.Closed:
                await connection.OpenAsync();
                try
                {
                    await connection.SetReadUncommittedAsync();
                    return new ConnectionCloser(connection);
                }
                catch
                {
                    try { connection.Close(); }
                    catch { /* we're already trying to handle, kthxbye */ }
                    throw;
                }

            default:
                throw new InvalidOperationException("Cannot use EnsureOpen when connection is " + connection.State);
        }
    }

    private static readonly ConcurrentDictionary<int, string> _markedSql = [];

    /// <summary>
    /// Takes a SQL query, and inserts the path and line in as a comment. Ripped right out of Stack Overflow proper.
    /// </summary>
    /// <param name="sql">The SQL that needs commenting</param>
    /// <param name="path">The path of the calling file</param>
    /// <param name="lineNumber">The line number of the calling function</param>
    /// <param name="comment">The specific manual comment to add</param>
    private static string MarkSqlString(string sql, string path, int lineNumber, string comment)
    {
        if (path.IsNullOrEmpty() || lineNumber == 0) return sql;

        int key = 17;
        unchecked
        {
            key = (key * 23) + sql.GetHashCode();
            key = (key * 23) + path.GetHashCode();
            key = (key * 23) + lineNumber.GetHashCode();
            if (comment.HasValue()) key = (key * 23) + comment.GetHashCode();
        }

        // Have we seen this before???
        if (_markedSql.TryGetValue(key, out string output)) return output;

        // nope
        var commentWrap = " ";
        var i = sql.IndexOf(Environment.NewLine, StringComparison.InvariantCultureIgnoreCase);

        // if we didn't find \n, or it was the very end, go to the first space method
        if (i < 0 || i == sql.Length - 1)
        {
            i = sql.IndexOf(' ');
            commentWrap = Environment.NewLine;
        }

        if (i < 0) return sql;

        // Grab one directory and the file name worth of the path this dodges problems with the build server using temp dirs
        // but also gives us enough info to uniquely identify a queries location
        var split = path.LastIndexOf('\\') - 1;
        if (split < 0) return sql;
        split = path.LastIndexOf('\\', split);

        if (split < 0) return sql;
        split++; // just for Craver

        var ret = sql[..i] + " /* " + path[split..] + "@" + lineNumber.ToString() + (comment.HasValue() ? " - " + comment : "") + " */" + commentWrap + sql[i..];
        // Cache, don't allocate all this pass again
        _markedSql[key] = ret;
        return ret;
    }

    public static async Task<int> SetReadUncommittedAsync(this DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED";
            await cmd.ExecuteNonQueryAsync();
        }
        return 1;
    }

    private class ConnectionCloser(DbConnection connection) : IDisposable
    {
        private DbConnection _connection = connection;

        public void Dispose()
        {
            var cn = _connection;
            _connection = null;
            try { cn?.Close(); }
            catch { /* throwing from Dispose() is so lame */ }
        }
    }
}
