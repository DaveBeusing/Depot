# Historical Inventory Valuation

## Accounting authority

FIFO issues and negative inventory adjustments consume the reporting carrying value established by the original receipt and its landed-cost journals. A change in the exchange rate after acquisition does not change the inventory asset removed on consumption. For example, a 100 USD receipt recognized at 90 EUR is fully depleted by a 90 EUR inventory credit even if the shipment-date rate would translate 100 USD to 80 EUR.

The original transaction-currency quantity, unit cost and consumption amount remain in the inventory event and consumption records. The journal retains transaction-currency lines as well as explicit historical reporting amounts. The General Ledger remains the only journal-posting authority; normal manual and profile posting still require their normal persisted exchange-rate references.

## Retained evidence and rounding

Finance schema **13** adds `FinanceInventoryReportingConsumptions`, keyed by the existing consumption ID. Each new issue records its reporting currency and exact reporting amount in the same transaction as the layer quantity, inventory event, General Ledger entry and audit evidence. Core schema does not change.

Receipt carrying amounts come from immutable original journal lines, with matching source operation, event, amount, accounting book, item, quantity and currency checks. Landed costs retain their own journal exchange-rate result. Their transaction and reporting totals are allocated across persisted layer-allocation weights in layer-ID order using cumulative currency rounding, so all layer shares exactly equal the respective posted totals. Fractional unit-cost allocation rows remain unchanged; consuming an individual layer does not require posting a fraction of a currency minor unit. Unit-cost changes are never used to retranslate historical reporting values.

A partial consumption allocates its share of the remaining transaction and reporting balances at the respective currency precision. Full depletion takes the exact remaining amounts. Persisting each consumption's reporting amount allows a reversal to restore the original amount even when another, later consumption remains active. Legitimate zero-rounded reporting amounts are retained; the narrow inventory posting path permits them without weakening normal public posting rules.

Inventory posting profiles must allocate each amount key fully to both debit and credit accounts. Split profile lines use cumulative rounding to preserve each total. Accounting-book, account, period, dimension, source identity, balancing, number-sequence and audit validation remain in the General Ledger service. Historical amounts are included in the posting fingerprint.

For a historical issue, the journal header has no market exchange-rate ID. Its numeric rate is a derived summary of the retained reporting and transaction totals, which may combine multiple acquisition rates. When one rounded total is zero, or the ratio is below the header field precision, the header uses the positive identity placeholder `1`; no market rate is implied. The exact journal lines and per-consumption reporting evidence are authoritative. Reversal swaps the original transaction and reporting debits and credits; it never resolves a new exchange rate.

## Historical reports and reconciliation

Inventory reconciliation and the Inventory Valuation financial report use the same carrying-value projection. Receipt, landed-cost, issue and reversal inclusion follows the immutable journals' posting dates. Backdated accounting is therefore evaluated by accounting date rather than the database insertion timestamp.

The projection subtracts recorded consumption amounts from the original receipt and landed-cost totals. Zero-quantity layers with a non-zero legacy carrying amount remain visible for investigation; they are not silently dropped from valuation. An inventory issue cannot consume a future receipt or a negative carrying balance. A new consumption must also be dated on or after the latest retained posting or reversal affecting its layer, so backdating cannot use a later landed-cost value or quantity restored by a later reversal.

## Upgrade and legacy evidence

The migration is additive and repeatable on SQLite, SQL Server, MariaDB and MySQL. Existing journal entries, layer rows and consumption rows are not rewritten or repriced. Current-feature metadata recovery must verify that the new reporting-consumption table exists before advertising Finance schema 13.

Legacy consumptions without a reporting-evidence row are reconstructed from their own original immutable issue journal. The journal's exact reporting total is allocated across its persisted consumption amounts in layer-ID order with deterministic residual rounding. This reproduces what was actually credited rather than assuming the old issue used the receipt rate. Any remaining value is still available to the next consumption, whose full depletion removes the complete retained balance.

Missing original journals, mismatched book/item/currency/amount evidence, partially missing new reporting evidence, and disagreement between retained quantities and current layers stop the operation with an explicit error. Migration does not invent missing accounting evidence. A residual already left on a fully depleted legacy layer requires an explicit accounting correction after investigation; this change does not create retrospective journals automatically.

## Validation

Focused SQLite regressions cover changed-rate full depletion, mixed acquisition rates, landed cost including a one-cent allocation across three layers, partial consumption, out-of-order reversals, rejection of consumption dated before later landed costs or reversals, currency rounding including zero reporting amounts, negative adjustments, legacy journal reconstruction, missing-evidence rejection, transactional audit failure and repeatable migration from Finance 12. Full database-provider acceptance exercises the same production service boundaries on SQL Server, MariaDB and MySQL and checks both reconciliation and Inventory Valuation projections against the inventory General Ledger balance.
