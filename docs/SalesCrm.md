# Sales CRM

## Purpose

Depot's Sales CRM is an ERP-native commercial pipeline directly upstream of the existing Customer, Quote, Pricing and Sales Order workflows. It manages leads, opportunities and planned sales activities without introducing a separate marketing platform or a second customer/contact master.

## Domain boundary

The CRM layer owns leads and qualification state, opportunities linked to Customers or converted Leads, ordered opportunity stages, expected close/value/probability data, planned sales activities, ownership, optimistic concurrency and controlled conversions.

CustomerService remains authoritative for customer business rules. SalesQuoteService remains authoritative for quote creation. Opportunity amounts do not post revenue or create accounting evidence.

## Persistence

Sales feature schema **15** adds SalesLeads, SalesOpportunityStages, SalesOpportunities and SalesActivities plus bounded indexes for owner, status, stage, due-date and search access paths. Core schema remains **30**.

Lead conversion is transactional and persists ConvertedCustomerId and ConvertedOpportunityId so duplicate conversion is prevented. Mutable CRM records use optimistic Version values where stale mutation matters.

## Lead workflow

A Lead captures company/person identity, contact channels, source, owner, status and notes. Conversion can link an existing Customer or create one through the existing Customer authority and creates the initial Opportunity in the same controlled workflow. Converted Leads retain immutable conversion linkage.

## Opportunity workflow

An Opportunity belongs to a Customer and has an owner, stage, currency, expected amount, expected close date, user-maintained probability and optional next-activity date. Open Opportunities can move between active stages, schedule Activities, create one linked draft Sales Quote and close as Won or Lost with an explicit reason.

## Activities and My Work

Activities support Call, Email, Meeting and FollowUp work with due time, owner, subject, notes and explicit completion/cancellation evidence. My Work surfaces bounded owner-scoped due/overdue Activities and open Opportunities whose next activity is missing or due.

## Search and role centers

Global Search includes Leads and Opportunities for authorized users and deep-links to the exact record. The Sales role center includes owned open Opportunities and database-aggregated pipeline KPIs. Interactive paths remain bounded and do not load the complete pipeline client-side.

## Authorization

Service-layer authorization is authoritative. SalesCrm.View controls read access; SalesCrmRecords.Manage controls operational record mutation; SalesCrm.Manage controls configuration and broader ownership authority; SalesCrmActivities.View and SalesCrmActivities.Manage control activity access.

Sales User receives operational CRM permissions. Sales Manager additionally receives CRM management authority. Management Viewer receives read-only CRM visibility.

## Validation

Targeted integration tests cover conversion and duplicate prevention, optimistic concurrency, pipeline/closure, Opportunity-to-Quote linkage, Activity/My Work behavior, bounded search and service-layer RBAC. The existing Sales provider acceptance additionally executes a CRM persistence roundtrip on SQL Server, MariaDB and MySQL.

## Non-goals

The CRM feature does not provide marketing campaigns/automation, bulk email/newsletters, inbox synchronization, tracking pixels, opaque lead scoring, social enrichment, a second Customer/contact master, or accounting/revenue recognition from pipeline values.
