# Development roadmap

This is a sequence of learning slices, not a claim that features already work. The [scope](Foodhub_Project_Scope.md) defines agreed behavior. Technical defaults remain provisional until implementation guidance settles them.

| Milestone | Prerequisite and work | Observable completion |
|---|---|---|
| 1. Connection and configuration | Existing shell and schema. Make connection configuration explicit, document local setup, establish simple view-model/service composition, and display connection failures clearly. | App opens on a Windows development machine; one async read succeeds; unreachable SQL Server produces useful feedback without freezing the UI. |
| 2. Profiles and addresses | Connection works. Create/select/edit profiles and their saved addresses, with required-field validation. | Data survives restart; switching profiles displays only the selected profile's addresses; invalid fields are explained. |
| 3. Menu and basket | Data access and selected profile. Add categories, search, local images/placeholders, availability, quantities, and preview totals. | A demonstrator can find food, combine duplicate additions, edit quantities, remove items, and see correct totals and empty states. |
| 4. Checkout and confirmation | Profiles, addresses, and basket. Validate the review screen, call `PlaceOrder`, then reload the receipt. | Valid checkout saves exactly the intended items and shows an order number and saved total; empty/unavailable baskets and another customer's address fail without partial orders. |
| 5. Tracking and cancellation | Persisted orders. Add current status, ordered timeline, labeled demo advancement, and eligible cancellation through `UpdateOrderStatus`. | Each normal stage is demonstrable; cancellation works only from Order placed; terminal states and skipped/backward/repeated transitions are rejected. |
| 6. History and presentation | Complete ordering flow. Add selected-customer history and receipt details; finish navigation, keyboard behavior, loading/error/empty states, and restart demonstration. | Delivered and cancelled orders reopen correctly after restart; later customer/menu/settings edits do not change saved receipt snapshots. |

## Verification strategy

Use focused unit tests for authored basket calculations and application decisions once those exist. Use SQL Server integration checks for the procedures, transactions, constraints, and persisted snapshots. UI acceptance needs manual WinUI execution; graph inspection does not replace it.

Before declaring the full journey complete, demonstrate:

1. Select/create a customer and address, browse by category/search, add food, adjust quantities, and review the total.
2. Place an order and compare the persisted items, amount, address, and order number with the confirmation.
3. Try an empty basket, missing address, wrong-owner address, and unavailable item; confirm useful feedback and no partial order records.
4. Advance one order through every normal stage. Cancel a different newly placed order. Reject cancellation after preparation and all transitions from terminal states.
5. Attempt competing status actions against one order; confirm a consistent final state and history without duplicate progression.
6. Restart the application and reopen orders. Edit current customer/menu/settings data in the test setup and confirm earlier receipts retain their saved values.
7. Verify keyboard access, status labels without color, missing-image fallback, duplicate-submit prevention, database outage feedback, and safe recovery from an uncertain checkout result.

Record actual build, database, and UI results with each implementation milestone. Never mark a milestone complete solely because entities, screens, or graph nodes exist.

## Later decisions

Discuss UI guidance and MVVM/composition choices during the first implementation slice. Review price-change handling before checkout implementation. Employees, dispatch, GPS, collection, sign-in, menu administration, and payments require a separate scope decision; they are not prerequisites for this roadmap.
