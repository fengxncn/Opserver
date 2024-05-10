﻿using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace Opserver.Data.SQL.QueryPlans;

public partial class ShowPlanXML
{
    [XmlIgnore]
    public List<BaseStmtInfoType> Statements
    {
        get
        {
            if (BatchSequence == null) return [];
            return BatchSequence.SelectMany(bs =>
                    bs.SelectMany(b => b.Items?.SelectMany(bst => bst.Statements))
                ).ToList();
        }
    }
}

public partial class BaseStmtInfoType
{
    public virtual IEnumerable<BaseStmtInfoType> Statements
    {
        get { yield return this; }
    }

    public bool IsMinor
        => StatementType switch
        {
            "COND" or "RETURN NONE" => true,
            _ => false,
        };

    private const string declareFormat = "Declare {0} {1} = {2};";
    private static readonly Regex emptyLineRegex = GetEmptyLineRegex();
    private static readonly Regex initParamsTrimRegex = GetInitParamsTrimRegex();
    private static readonly Regex paramRegex = GetParamRegex();
    private static readonly Regex paramSplitRegex = GetParamSplitRegex();
    private static readonly char[] startTrimChars = ['\n', '\r', ';'];

    public string ParameterDeclareStatement
        => this is StmtSimpleType ss ? GetDeclareStatement(ss.QueryPlan) : "";

    public string StatementTextWithoutInitialParams
        => this is StmtSimpleType ss ? emptyLineRegex.Replace(paramRegex.Replace(ss.StatementText ?? "", ""), "").Trim().TrimStart(startTrimChars) : "";

    public string StatementTextWithoutInitialParamsTrimmed
    {
        get
        {
            var orig = StatementTextWithoutInitialParams;
            return orig?.Length == 0 ? string.Empty : initParamsTrimRegex.Replace(orig, string.Empty).Trim();
        }
    }

    private string GetDeclareStatement(QueryPlanType queryPlan)
    {
        if (queryPlan?.ParameterList == null || queryPlan.ParameterList.Length == 0) return "";

        var result = StringBuilderCache.Get();
        var paramTypeList = paramRegex.Match(StatementText);
        if (!paramTypeList.Success) return "";
        // TODO: get test cases and move this to a single multi-match regex
        var paramTypes = paramSplitRegex.Split(paramTypeList.Groups[1].Value).Select(p => p.Split(StringSplits.Space));

        foreach (var p in queryPlan.ParameterList)
        {
            var paramType = paramTypes.FirstOrDefault(pt => pt[0] == p.Column);
            if (paramType != null)
            {
                result.AppendFormat(declareFormat, p.Column, paramType[1], p.ParameterCompiledValue)
                      .AppendLine();
            }
        }
        return result.Length > 0 ? result.Insert(0, "-- Compiled Params\n").ToStringRecycle() : result.ToStringRecycle();
    }

    [GeneratedRegex(@"^\s+$[\r\n]*", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex GetEmptyLineRegex();
    [GeneratedRegex(@"^\s*(begin|end)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled, "zh-CN")]
    private static partial Regex GetInitParamsTrimRegex();
    [GeneratedRegex(@"^\(( [^\(\)]* ( ( (?<Open>\() [^\(\)]* )+ ( (?<Close-Open>\)) [^\(\)]* )+ )* (?(Open)(?!)) )\)", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex GetParamRegex();
    [GeneratedRegex(",(?=[@])", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex GetParamSplitRegex();
}

public partial class StmtCondType
{
    public override IEnumerable<BaseStmtInfoType> Statements
    {
        get
        {
            yield return this;
            if (Then?.Statements != null)
            {
                foreach (var s in Then.Statements.Items) yield return s;
            }
            if (Else?.Statements != null)
            {
                foreach (var s in Else.Statements.Items) yield return s;
            }
        }
    }
}
