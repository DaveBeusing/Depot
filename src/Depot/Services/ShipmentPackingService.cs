// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ShipmentPackingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ShipmentRepository _shipments;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public ShipmentPackingService(IDatabaseTransactionRunner transactions, ShipmentRepository shipments, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions=transactions; _shipments=shipments; _auditEntries=auditEntries; _audit=audit; _authorization=authorization;
	}

	public bool CanPack => _authorization.HasPermission(ApplicationPermission.ShipmentsEdit);

	public async Task<Shipment> SetStatusAsync(long shipmentId,long version,ShipmentPackingStatus status,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsEdit);
		var user=_authorization.CurrentUser??throw new UnauthorizedAccessException();
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var before=await _shipments.GetByIdAsync(tx,shipmentId,ct)??throw new InvalidOperationException("Shipment was not found.");
			if(before.Status!=ShipmentStatus.Draft)throw new InvalidOperationException("Only draft shipments can be packed.");
			if(before.Version!=version)throw new ConcurrencyConflictException("shipment");
			DateTime? packedAt=status==ShipmentPackingStatus.Packed?DateTime.UtcNow:null;
			long? packedBy=status==ShipmentPackingStatus.Packed?user.Id:null;
			if(!await _shipments.SetPackingStatusAsync(tx,shipmentId,version,status,packedBy,packedAt,ct))throw new ConcurrencyConflictException("shipment");
			var after=await _shipments.GetByIdAsync(tx,shipmentId,ct)??throw new InvalidOperationException("Shipment could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(shipmentId,before,after),ct);
			return after;
		},token);
	}
}
