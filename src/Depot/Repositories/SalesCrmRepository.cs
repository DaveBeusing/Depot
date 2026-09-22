// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesCrmRepository : DatabaseRepository
{
	private const string LeadColumns = "l.Id,l.LeadNumber,l.CompanyName,l.PersonName,l.Email,l.Phone,l.Source,l.OwnerUserId,u.DisplayName,l.Status,l.NotesSummary,l.CreatedAtUtc,l.UpdatedAtUtc,l.ConvertedCustomerId,l.ConvertedOpportunityId,l.ConvertedAtUtc,l.Version";
	private const string OpportunityColumns = "o.Id,o.OpportunityNumber,o.CustomerId,c.Name,o.LeadId,o.OwnerUserId,u.DisplayName,o.StageId,s.Name,s.SortOrder,o.ExpectedCloseDate,o.Currency,o.ExpectedAmount,o.ProbabilityPercent,o.NextActivityDate,o.Outcome,o.CloseReason,o.ClosedAtUtc,o.LinkedSalesQuoteId,o.CreatedAtUtc,o.UpdatedAtUtc,o.Version";
	private const string ActivityColumns = "a.Id,a.LeadId,a.OpportunityId,a.Type,a.DueAtUtc,a.OwnerUserId,u.DisplayName,a.Status,a.Subject,a.Notes,a.CompletedAtUtc,a.CompletedByUserId,a.CancelledAtUtc,a.CancelledByUserId,a.Version";

	public SalesCrmRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<SalesLead>> SearchLeadsAsync(string? searchText, SalesLeadStatus? status, long? ownerUserId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters=new List<string>();var parameters=new List<DatabaseParameter>();
		if(status is not null){filters.Add("l.Status=$Status");parameters.Add(Parameter("$Status",(int)status.Value));}
		if(ownerUserId is > 0){filters.Add("l.OwnerUserId=$OwnerUserId");parameters.Add(Parameter("$OwnerUserId",ownerUserId.Value));}
		var plan=SearchQueryPlan.Create(searchText);string? rank=null;
		if(plan is { } search)
		{
			var prefix=new[]{"l.LeadNumber","l.CompanyName","l.PersonName"};
			filters.Add(search.BuildPredicate(prefix,["l.LeadNumber","l.CompanyName","l.PersonName","l.Email","l.Phone","l.Source"]));
			parameters.Add(Parameter("$SearchExact",search.Exact));parameters.Add(Parameter("$SearchPrefix",search.Prefix));
			if(search.AllowContains){parameters.Add(Parameter("$SearchWordPrefix",search.WordPrefix));parameters.Add(Parameter("$SearchContains",search.Contains));}
			rank=search.BuildRankExpression(prefix,["l.CompanyName","l.PersonName"]);
		}
		var where=filters.Count==0?string.Empty:$"WHERE {string.Join(" AND ",filters)}";
		var order=rank is null?"l.UpdatedAtUtc DESC,l.Id DESC":$"{rank},l.UpdatedAtUtc DESC,l.Id DESC";
		return Database.QueryPageAsync($"SELECT {LeadColumns} FROM SalesLeads l INNER JOIN Users u ON u.Id=l.OwnerUserId {where} ORDER BY {order}",$"SELECT COUNT(*) FROM SalesLeads l {where}",ReadLead,pageNumber,pageSize,cancellationToken,parameters.ToArray());
	}

	public Task<SalesLead?> GetLeadAsync(long id,CancellationToken cancellationToken)=>
		Database.QuerySingleOrDefaultAsync($"SELECT {LeadColumns} FROM SalesLeads l INNER JOIN Users u ON u.Id=l.OwnerUserId WHERE l.Id=$Id;",ReadLead,cancellationToken,Parameter("$Id",id));

	public Task<SalesLead?> GetLeadAsync(DatabaseTransactionContext transaction,long id,CancellationToken cancellationToken)=>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {LeadColumns} FROM SalesLeads l INNER JOIN Users u ON u.Id=l.OwnerUserId WHERE l.Id=$Id;",ReadLead,cancellationToken,Parameter("$Id",id));

	public async Task<SalesLead> CreateLeadAsync(DatabaseTransactionContext transaction,SalesLead lead,CancellationToken cancellationToken)
	{
		lead.CreatedAtUtc=DateTime.UtcNow;lead.UpdatedAtUtc=lead.CreatedAtUtc;lead.LeadNumber=$"PENDING-{Guid.NewGuid():N}";
		lead.Id=await transaction.Session.InsertAsync("INSERT INTO SalesLeads (LeadNumber,CompanyName,PersonName,Email,Phone,Source,OwnerUserId,Status,NotesSummary,CreatedAtUtc,UpdatedAtUtc,ConvertedCustomerId,ConvertedOpportunityId,ConvertedAtUtc) VALUES ($LeadNumber,$CompanyName,$PersonName,$Email,$Phone,$Source,$OwnerUserId,$Status,$NotesSummary,$CreatedAtUtc,$UpdatedAtUtc,$ConvertedCustomerId,$ConvertedOpportunityId,$ConvertedAtUtc);",cancellationToken,LeadParameters(lead));
		lead.LeadNumber=$"LD-{lead.Id:000000}";
		await transaction.Session.ExecuteAsync("UPDATE SalesLeads SET LeadNumber=$LeadNumber WHERE Id=$Id;",cancellationToken,Parameter("$LeadNumber",lead.LeadNumber),Parameter("$Id",lead.Id));
		return lead;
	}

	public async Task<bool> UpdateLeadAsync(DatabaseTransactionContext transaction,SalesLead lead,long expectedVersion,CancellationToken cancellationToken)
	{
		lead.UpdatedAtUtc=DateTime.UtcNow;
		var updated=await transaction.Session.ExecuteAsync("UPDATE SalesLeads SET CompanyName=$CompanyName,PersonName=$PersonName,Email=$Email,Phone=$Phone,Source=$Source,OwnerUserId=$OwnerUserId,Status=$Status,NotesSummary=$NotesSummary,UpdatedAtUtc=$UpdatedAtUtc,ConvertedCustomerId=$ConvertedCustomerId,ConvertedOpportunityId=$ConvertedOpportunityId,ConvertedAtUtc=$ConvertedAtUtc,Version=Version+1 WHERE Id=$Id AND Version=$Version;",cancellationToken,LeadParameters(lead).Concat([Parameter("$Id",lead.Id),Parameter("$Version",expectedVersion)]).ToArray());
		if(updated==1)lead.Version=expectedVersion+1;return updated==1;
	}

	public Task<PageResult<SalesOpportunity>> SearchOpportunitiesAsync(string? searchText,long? stageId,SalesOpportunityOutcome? outcome,long? ownerUserId,int pageNumber,int pageSize,CancellationToken cancellationToken)
	{
		var filters=new List<string>();var parameters=new List<DatabaseParameter>();
		if(stageId is > 0){filters.Add("o.StageId=$StageId");parameters.Add(Parameter("$StageId",stageId.Value));}
		if(outcome is not null){filters.Add("o.Outcome=$Outcome");parameters.Add(Parameter("$Outcome",(int)outcome.Value));}
		if(ownerUserId is > 0){filters.Add("o.OwnerUserId=$OwnerUserId");parameters.Add(Parameter("$OwnerUserId",ownerUserId.Value));}
		var plan=SearchQueryPlan.Create(searchText);string? rank=null;
		if(plan is { } search)
		{
			var prefix=new[]{"o.OpportunityNumber","c.Name"};
			filters.Add(search.BuildPredicate(prefix,["o.OpportunityNumber","c.Name","o.CloseReason","s.Name"]));
			parameters.Add(Parameter("$SearchExact",search.Exact));parameters.Add(Parameter("$SearchPrefix",search.Prefix));
			if(search.AllowContains){parameters.Add(Parameter("$SearchWordPrefix",search.WordPrefix));parameters.Add(Parameter("$SearchContains",search.Contains));}
			rank=search.BuildRankExpression(prefix,["c.Name","s.Name"]);
		}
		var where=filters.Count==0?string.Empty:$"WHERE {string.Join(" AND ",filters)}";
		var from="FROM SalesOpportunities o INNER JOIN Customers c ON c.Id=o.CustomerId INNER JOIN Users u ON u.Id=o.OwnerUserId INNER JOIN SalesOpportunityStages s ON s.Id=o.StageId";
		var order=rank is null?"CASE WHEN o.Outcome=0 THEN 0 ELSE 1 END,s.SortOrder,o.ExpectedCloseDate,o.Id DESC":$"{rank},s.SortOrder,o.ExpectedCloseDate,o.Id DESC";
		return Database.QueryPageAsync($"SELECT {OpportunityColumns} {from} {where} ORDER BY {order}",$"SELECT COUNT(*) {from} {where}",ReadOpportunity,pageNumber,pageSize,cancellationToken,parameters.ToArray());
	}

	public Task<SalesOpportunity?> GetOpportunityAsync(long id,CancellationToken cancellationToken)=>
		Database.QuerySingleOrDefaultAsync($"SELECT {OpportunityColumns} FROM SalesOpportunities o INNER JOIN Customers c ON c.Id=o.CustomerId INNER JOIN Users u ON u.Id=o.OwnerUserId INNER JOIN SalesOpportunityStages s ON s.Id=o.StageId WHERE o.Id=$Id;",ReadOpportunity,cancellationToken,Parameter("$Id",id));

	public Task<SalesOpportunity?> GetOpportunityAsync(DatabaseTransactionContext transaction,long id,CancellationToken cancellationToken)=>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {OpportunityColumns} FROM SalesOpportunities o INNER JOIN Customers c ON c.Id=o.CustomerId INNER JOIN Users u ON u.Id=o.OwnerUserId INNER JOIN SalesOpportunityStages s ON s.Id=o.StageId WHERE o.Id=$Id;",ReadOpportunity,cancellationToken,Parameter("$Id",id));

	public Task<SalesOpportunity?> GetOpportunityByLeadAsync(DatabaseTransactionContext transaction,long leadId,CancellationToken cancellationToken)=>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {OpportunityColumns} FROM SalesOpportunities o INNER JOIN Customers c ON c.Id=o.CustomerId INNER JOIN Users u ON u.Id=o.OwnerUserId INNER JOIN SalesOpportunityStages s ON s.Id=o.StageId WHERE o.LeadId=$LeadId;",ReadOpportunity,cancellationToken,Parameter("$LeadId",leadId));

	public async Task<SalesOpportunity> CreateOpportunityAsync(DatabaseTransactionContext transaction,SalesOpportunity value,CancellationToken cancellationToken)
	{
		value.CreatedAtUtc=DateTime.UtcNow;value.UpdatedAtUtc=value.CreatedAtUtc;value.OpportunityNumber=$"PENDING-{Guid.NewGuid():N}";
		value.Id=await transaction.Session.InsertAsync("INSERT INTO SalesOpportunities (OpportunityNumber,CustomerId,LeadId,OwnerUserId,StageId,ExpectedCloseDate,Currency,ExpectedAmount,ProbabilityPercent,NextActivityDate,Outcome,CloseReason,ClosedAtUtc,LinkedSalesQuoteId,CreatedAtUtc,UpdatedAtUtc) VALUES ($OpportunityNumber,$CustomerId,$LeadId,$OwnerUserId,$StageId,$ExpectedCloseDate,$Currency,$ExpectedAmount,$ProbabilityPercent,$NextActivityDate,$Outcome,$CloseReason,$ClosedAtUtc,$LinkedSalesQuoteId,$CreatedAtUtc,$UpdatedAtUtc);",cancellationToken,OpportunityParameters(value));
		value.OpportunityNumber=$"OP-{value.Id:000000}";
		await transaction.Session.ExecuteAsync("UPDATE SalesOpportunities SET OpportunityNumber=$OpportunityNumber WHERE Id=$Id;",cancellationToken,Parameter("$OpportunityNumber",value.OpportunityNumber),Parameter("$Id",value.Id));
		return value;
	}

	public async Task<bool> UpdateOpportunityAsync(DatabaseTransactionContext transaction,SalesOpportunity value,long expectedVersion,CancellationToken cancellationToken)
	{
		value.UpdatedAtUtc=DateTime.UtcNow;
		var updated=await transaction.Session.ExecuteAsync("UPDATE SalesOpportunities SET CustomerId=$CustomerId,OwnerUserId=$OwnerUserId,StageId=$StageId,ExpectedCloseDate=$ExpectedCloseDate,Currency=$Currency,ExpectedAmount=$ExpectedAmount,ProbabilityPercent=$ProbabilityPercent,NextActivityDate=$NextActivityDate,Outcome=$Outcome,CloseReason=$CloseReason,ClosedAtUtc=$ClosedAtUtc,LinkedSalesQuoteId=$LinkedSalesQuoteId,UpdatedAtUtc=$UpdatedAtUtc,Version=Version+1 WHERE Id=$Id AND Version=$Version;",cancellationToken,OpportunityParameters(value).Concat([Parameter("$Id",value.Id),Parameter("$Version",expectedVersion)]).ToArray());
		if(updated==1)value.Version=expectedVersion+1;return updated==1;
	}

	public Task<IReadOnlyList<SalesOpportunityStage>> ListStagesAsync(bool activeOnly,CancellationToken cancellationToken)=>
		Database.QueryAsync($"SELECT Id,Code,Name,SortOrder,IsActive,Version FROM SalesOpportunityStages {(activeOnly?"WHERE IsActive=1":string.Empty)} ORDER BY SortOrder,Id;",ReadStage,cancellationToken);

	public async Task<SalesOpportunityStage> SaveStageAsync(SalesOpportunityStage stage,CancellationToken cancellationToken)
	{
		if(stage.Id==0)
		{
			stage.Id=await Database.InsertAsync("INSERT INTO SalesOpportunityStages (Code,Name,SortOrder,IsActive) VALUES ($Code,$Name,$SortOrder,$IsActive);",cancellationToken,StageParameters(stage));return stage;
		}
		var updated=await Database.ExecuteAsync("UPDATE SalesOpportunityStages SET Code=$Code,Name=$Name,SortOrder=$SortOrder,IsActive=$IsActive,Version=Version+1 WHERE Id=$Id AND Version=$Version;",cancellationToken,StageParameters(stage).Concat([Parameter("$Id",stage.Id),Parameter("$Version",stage.Version)]).ToArray());
		if(updated!=1)throw new Services.ConcurrencyConflictException("opportunity stage");stage.Version++;return stage;
	}

	public Task<IReadOnlyList<SalesPipelineStageSummary>> GetPipelineSummaryAsync(long? ownerUserId,CancellationToken cancellationToken)
	{
		var owner=ownerUserId is > 0?"AND o.OwnerUserId=$OwnerUserId":string.Empty;
		return Database.QueryAsync($"SELECT s.Id,s.Name,s.SortOrder,COUNT(o.Id),COALESCE(SUM(o.ExpectedAmount),0),COALESCE(SUM(o.ExpectedAmount*o.ProbabilityPercent/100.0),0) FROM SalesOpportunityStages s LEFT JOIN SalesOpportunities o ON o.StageId=s.Id AND o.Outcome=0 {owner} WHERE s.IsActive=1 GROUP BY s.Id,s.Name,s.SortOrder ORDER BY s.SortOrder,s.Id;",ReadPipeline,cancellationToken,ownerUserId is > 0?Parameter("$OwnerUserId",ownerUserId.Value):Parameter("$OwnerUserId",DBNull.Value));
	}

	public Task<IReadOnlyList<SalesActivity>> ListActivitiesAsync(long? leadId,long? opportunityId,int count,CancellationToken cancellationToken)
	{
		if(count is < 1 or > 500)throw new ArgumentOutOfRangeException(nameof(count));
		var filters=new List<string>();var parameters=new List<DatabaseParameter>();
		if(leadId is > 0){filters.Add("a.LeadId=$LeadId");parameters.Add(Parameter("$LeadId",leadId.Value));}
		if(opportunityId is > 0){filters.Add("a.OpportunityId=$OpportunityId");parameters.Add(Parameter("$OpportunityId",opportunityId.Value));}
		if(filters.Count==0)return Task.FromResult<IReadOnlyList<SalesActivity>>([]);
		return Database.QuerySliceAsync($"SELECT {ActivityColumns} FROM SalesActivities a INNER JOIN Users u ON u.Id=a.OwnerUserId WHERE {string.Join(" AND ",filters)} ORDER BY CASE WHEN a.Status=1 THEN 0 ELSE 1 END,a.DueAtUtc DESC,a.Id DESC",ReadActivity,0,count,cancellationToken,parameters.ToArray());
	}

	public Task<SalesActivity?> GetActivityAsync(DatabaseTransactionContext transaction,long id,CancellationToken cancellationToken)=>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {ActivityColumns} FROM SalesActivities a INNER JOIN Users u ON u.Id=a.OwnerUserId WHERE a.Id=$Id;",ReadActivity,cancellationToken,Parameter("$Id",id));

	public async Task<SalesActivity> CreateActivityAsync(DatabaseTransactionContext transaction,SalesActivity value,CancellationToken cancellationToken)
	{
		value.Id=await transaction.Session.InsertAsync("INSERT INTO SalesActivities (LeadId,OpportunityId,Type,DueAtUtc,OwnerUserId,Status,Subject,Notes,CompletedAtUtc,CompletedByUserId,CancelledAtUtc,CancelledByUserId) VALUES ($LeadId,$OpportunityId,$Type,$DueAtUtc,$OwnerUserId,$Status,$Subject,$Notes,$CompletedAtUtc,$CompletedByUserId,$CancelledAtUtc,$CancelledByUserId);",cancellationToken,ActivityParameters(value));return value;
	}

	public async Task<bool> UpdateActivityAsync(DatabaseTransactionContext transaction,SalesActivity value,long expectedVersion,CancellationToken cancellationToken)
	{
		var updated=await transaction.Session.ExecuteAsync("UPDATE SalesActivities SET Type=$Type,DueAtUtc=$DueAtUtc,OwnerUserId=$OwnerUserId,Status=$Status,Subject=$Subject,Notes=$Notes,CompletedAtUtc=$CompletedAtUtc,CompletedByUserId=$CompletedByUserId,CancelledAtUtc=$CancelledAtUtc,CancelledByUserId=$CancelledByUserId,Version=Version+1 WHERE Id=$Id AND Version=$Version;",cancellationToken,ActivityParameters(value).Concat([Parameter("$Id",value.Id),Parameter("$Version",expectedVersion)]).ToArray());
		if(updated==1)value.Version=expectedVersion+1;return updated==1;
	}

	public Task<IReadOnlyList<SalesActivity>> GetOwnedOpenActivitiesAsync(long ownerUserId,int count,CancellationToken cancellationToken)
	{
		if(count is < 1 or > 100)throw new ArgumentOutOfRangeException(nameof(count));
		return Database.QuerySliceAsync($"SELECT {ActivityColumns} FROM SalesActivities a INNER JOIN Users u ON u.Id=a.OwnerUserId WHERE a.OwnerUserId=$OwnerUserId AND a.Status=1 ORDER BY a.DueAtUtc,a.Id",ReadActivity,0,count,cancellationToken,Parameter("$OwnerUserId",ownerUserId));
	}

	public Task<IReadOnlyList<SalesOpportunity>> GetOwnedFollowUpOpportunitiesAsync(long ownerUserId,DateTime nowUtc,int count,CancellationToken cancellationToken)
	{
		if(count is < 1 or > 100)throw new ArgumentOutOfRangeException(nameof(count));
		return Database.QuerySliceAsync($"SELECT {OpportunityColumns} FROM SalesOpportunities o INNER JOIN Customers c ON c.Id=o.CustomerId INNER JOIN Users u ON u.Id=o.OwnerUserId INNER JOIN SalesOpportunityStages s ON s.Id=o.StageId WHERE o.OwnerUserId=$OwnerUserId AND o.Outcome=0 AND (o.NextActivityDate IS NULL OR o.NextActivityDate<=$NowUtc) ORDER BY CASE WHEN o.NextActivityDate IS NULL THEN 1 ELSE 0 END,o.NextActivityDate,o.ExpectedCloseDate,o.Id",ReadOpportunity,0,count,cancellationToken,Parameter("$OwnerUserId",ownerUserId),Parameter("$NowUtc",Utc(nowUtc)));
	}

	private static DatabaseParameter[] LeadParameters(SalesLead value)=>[
		Parameter("$LeadNumber",value.LeadNumber),Parameter("$CompanyName",value.CompanyName),Parameter("$PersonName",value.PersonName),Parameter("$Email",value.Email),Parameter("$Phone",value.Phone),Parameter("$Source",value.Source),Parameter("$OwnerUserId",value.OwnerUserId),Parameter("$Status",(int)value.Status),Parameter("$NotesSummary",value.NotesSummary),Parameter("$CreatedAtUtc",Utc(value.CreatedAtUtc)),Parameter("$UpdatedAtUtc",Utc(value.UpdatedAtUtc)),Parameter("$ConvertedCustomerId",value.ConvertedCustomerId),Parameter("$ConvertedOpportunityId",value.ConvertedOpportunityId),Parameter("$ConvertedAtUtc",value.ConvertedAtUtc is null?null:Utc(value.ConvertedAtUtc.Value))];
	private static DatabaseParameter[] OpportunityParameters(SalesOpportunity value)=>[
		Parameter("$OpportunityNumber",value.OpportunityNumber),Parameter("$CustomerId",value.CustomerId),Parameter("$LeadId",value.LeadId),Parameter("$OwnerUserId",value.OwnerUserId),Parameter("$StageId",value.StageId),Parameter("$ExpectedCloseDate",value.ExpectedCloseDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Currency",value.Currency),Parameter("$ExpectedAmount",value.ExpectedAmount),Parameter("$ProbabilityPercent",value.ProbabilityPercent),Parameter("$NextActivityDate",value.NextActivityDate is null?null:Utc(value.NextActivityDate.Value)),Parameter("$Outcome",(int)value.Outcome),Parameter("$CloseReason",value.CloseReason),Parameter("$ClosedAtUtc",value.ClosedAtUtc is null?null:Utc(value.ClosedAtUtc.Value)),Parameter("$LinkedSalesQuoteId",value.LinkedSalesQuoteId),Parameter("$CreatedAtUtc",Utc(value.CreatedAtUtc)),Parameter("$UpdatedAtUtc",Utc(value.UpdatedAtUtc))];
	private static DatabaseParameter[] StageParameters(SalesOpportunityStage value)=>[Parameter("$Code",value.Code),Parameter("$Name",value.Name),Parameter("$SortOrder",value.SortOrder),Parameter("$IsActive",value.IsActive)];
	private static DatabaseParameter[] ActivityParameters(SalesActivity value)=>[
		Parameter("$LeadId",value.LeadId),Parameter("$OpportunityId",value.OpportunityId),Parameter("$Type",(int)value.Type),Parameter("$DueAtUtc",Utc(value.DueAtUtc)),Parameter("$OwnerUserId",value.OwnerUserId),Parameter("$Status",(int)value.Status),Parameter("$Subject",value.Subject),Parameter("$Notes",value.Notes),Parameter("$CompletedAtUtc",value.CompletedAtUtc is null?null:Utc(value.CompletedAtUtc.Value)),Parameter("$CompletedByUserId",value.CompletedByUserId),Parameter("$CancelledAtUtc",value.CancelledAtUtc is null?null:Utc(value.CancelledAtUtc.Value)),Parameter("$CancelledByUserId",value.CancelledByUserId)];

	private static SalesLead ReadLead(DbDataReader r)=>new(){Id=r.GetInt64(0),LeadNumber=r.GetString(1),CompanyName=r.IsDBNull(2)?null:r.GetString(2),PersonName=r.IsDBNull(3)?null:r.GetString(3),Email=r.IsDBNull(4)?null:r.GetString(4),Phone=r.IsDBNull(5)?null:r.GetString(5),Source=r.IsDBNull(6)?null:r.GetString(6),OwnerUserId=r.GetInt64(7),OwnerDisplayName=r.IsDBNull(8)?null:r.GetString(8),Status=(SalesLeadStatus)Convert.ToInt32(r.GetValue(9),CultureInfo.InvariantCulture),NotesSummary=r.IsDBNull(10)?null:r.GetString(10),CreatedAtUtc=ReadUtc(r,11),UpdatedAtUtc=ReadUtc(r,12),ConvertedCustomerId=r.IsDBNull(13)?null:r.GetInt64(13),ConvertedOpportunityId=r.IsDBNull(14)?null:r.GetInt64(14),ConvertedAtUtc=r.IsDBNull(15)?null:ReadUtc(r,15),Version=r.GetInt64(16)};
	private static SalesOpportunity ReadOpportunity(DbDataReader r)=>new(){Id=r.GetInt64(0),OpportunityNumber=r.GetString(1),CustomerId=r.GetInt64(2),CustomerName=r.GetString(3),LeadId=r.IsDBNull(4)?null:r.GetInt64(4),OwnerUserId=r.GetInt64(5),OwnerDisplayName=r.IsDBNull(6)?null:r.GetString(6),StageId=r.GetInt64(7),StageName=r.GetString(8),StageSortOrder=Convert.ToInt32(r.GetValue(9),CultureInfo.InvariantCulture),ExpectedCloseDate=r.IsDBNull(10)?null:ReadDate(r,10),Currency=r.GetString(11),ExpectedAmount=Convert.ToDecimal(r.GetValue(12),CultureInfo.InvariantCulture),ProbabilityPercent=Convert.ToInt32(r.GetValue(13),CultureInfo.InvariantCulture),NextActivityDate=r.IsDBNull(14)?null:ReadUtc(r,14),Outcome=(SalesOpportunityOutcome)Convert.ToInt32(r.GetValue(15),CultureInfo.InvariantCulture),CloseReason=r.IsDBNull(16)?null:r.GetString(16),ClosedAtUtc=r.IsDBNull(17)?null:ReadUtc(r,17),LinkedSalesQuoteId=r.IsDBNull(18)?null:r.GetInt64(18),CreatedAtUtc=ReadUtc(r,19),UpdatedAtUtc=ReadUtc(r,20),Version=r.GetInt64(21)};
	private static SalesOpportunityStage ReadStage(DbDataReader r)=>new(){Id=r.GetInt64(0),Code=r.GetString(1),Name=r.GetString(2),SortOrder=Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),IsActive=Convert.ToBoolean(r.GetValue(4),CultureInfo.InvariantCulture),Version=r.GetInt64(5)};
	private static SalesActivity ReadActivity(DbDataReader r)=>new(){Id=r.GetInt64(0),LeadId=r.IsDBNull(1)?null:r.GetInt64(1),OpportunityId=r.IsDBNull(2)?null:r.GetInt64(2),Type=(SalesActivityType)Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),DueAtUtc=ReadUtc(r,4),OwnerUserId=r.GetInt64(5),OwnerDisplayName=r.IsDBNull(6)?null:r.GetString(6),Status=(SalesActivityStatus)Convert.ToInt32(r.GetValue(7),CultureInfo.InvariantCulture),Subject=r.GetString(8),Notes=r.IsDBNull(9)?null:r.GetString(9),CompletedAtUtc=r.IsDBNull(10)?null:ReadUtc(r,10),CompletedByUserId=r.IsDBNull(11)?null:r.GetInt64(11),CancelledAtUtc=r.IsDBNull(12)?null:ReadUtc(r,12),CancelledByUserId=r.IsDBNull(13)?null:r.GetInt64(13),Version=r.GetInt64(14)};
	private static SalesPipelineStageSummary ReadPipeline(DbDataReader r)=>new(r.GetInt64(0),r.GetString(1),Convert.ToInt32(r.GetValue(2),CultureInfo.InvariantCulture),Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture),Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture));
	private static DateTime ReadDate(DbDataReader r,int ordinal)=>r.GetValue(ordinal) is DateTime dt?dt.Date:DateTime.Parse(Convert.ToString(r.GetValue(ordinal),CultureInfo.InvariantCulture)??string.Empty,CultureInfo.InvariantCulture).Date;
	private static DateTime ReadUtc(DbDataReader r,int ordinal)=>r.GetValue(ordinal) is DateTime dt?DateTime.SpecifyKind(dt,DateTimeKind.Utc):DateTime.Parse(Convert.ToString(r.GetValue(ordinal),CultureInfo.InvariantCulture)??string.Empty,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static string Utc(DateTime value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
}
